using System.Collections.Generic;
using Bosses;
using Enemies;
using Gameplay;
using Hud;
using Items;
using UnityEngine;

namespace Saving
{
    /// <summary>
    /// The state of the floor around the player, for a telephone save: which enemies are alive and where, every door, the
    /// boss fight and the cage, the objectives, the items still lying around and what is left in the crates.
    ///
    /// Capture writes it into a CheckpointSave. Restoring comes in two parts because the enemies are made by the
    /// EnemySpawner when the NavMesh is ready, which can be after the loading screen:
    ///   Prepare   before the scene loads. Hands the saved enemies to the spawner, so it places those and rolls no new ones.
    ///   Apply     once the floor is built (SaveGame.ApplyPending). Doors, boss, cage, objectives, items and crates.
    ///
    /// Doors, pickups and crates have no id of their own. They are found again by their place (to a quarter of a metre),
    /// which works because the floor is rebuilt from the same seed. Crates and racks roll their loot from that seed too
    /// (WorldRandom), so the same crate holds the same loot.
    /// </summary>
    public static class WorldState
    {
        // ---------- ids ----------

        private static string Place(Vector3 p) =>
            $"{Mathf.RoundToInt(p.x * 4f)}:{Mathf.RoundToInt(p.y * 4f)}:{Mathf.RoundToInt(p.z * 4f)}";

        private static string PickupKey(ItemPickup pickup) => Place(pickup.transform.position) + ":" + pickup.Item.Id;

        // ---------- saving ----------

        public static void Capture(CheckpointSave cp)
        {
            cp.hasWorld = true;
            cp.enemies.Clear();
            cp.doors.Clear();
            cp.pickups.Clear();
            cp.crates.Clear();

            foreach (var enemy in EnemyController.All)
            {
                if (!enemy || enemy.IsDead) continue;

                var health = enemy.GetComponent<Health>();
                var p = enemy.transform.position;
                cp.enemies.Add(new EnemySave
                {
                    prefab = enemy.name.Replace("(Clone)", "").Trim(),
                    x = p.x, y = p.y, z = p.z,
                    yaw = enemy.transform.eulerAngles.y,
                    health = health ? health.Current : 1f
                });
            }

            foreach (var door in Door.Active)
            {
                if (!door || door.IsSealed) continue;

                cp.doors.Add(new DoorSave
                {
                    key = Place(door.transform.position), open = door.IsOpen, locked = door.IsLocked, sign = door.SwingSign
                });
            }

            foreach (var pickup in Object.FindObjectsByType<ItemPickup>())
            {
                if (!pickup || !pickup.Item) continue;
                cp.pickups.Add(new PickupSave { key = PickupKey(pickup), count = pickup.Count });
            }

            foreach (var crate in Object.FindObjectsByType<ItemCrate>())
            {
                if (!crate || crate.Grid == null) continue;

                cp.crates.Add(new CrateSave
                {
                    key = Place(crate.transform.position),
                    unlocked = crate.Unlocked,
                    stacks = SaveSystem.CaptureStacks(crate.Grid)
                });
            }

            cp.boss = new BossSave();
            var arena = Object.FindAnyObjectByType<BossArena>();
            if (arena)
            {
                cp.boss.hasArena = true;
                cp.boss.started = arena.Started;
                cp.boss.ended = arena.Ended;
            }

            var boss = arena && arena.Boss ? arena.Boss : Object.FindAnyObjectByType<BossController>();
            if (boss)
            {
                var p = boss.transform.position;
                cp.boss.hasBoss = true;
                cp.boss.x = p.x; cp.boss.y = p.y; cp.boss.z = p.z;
                cp.boss.yaw = boss.transform.eulerAngles.y;
                cp.boss.quadruped = boss.Form == BossForm.Quadruped;
            }

            var cage = Object.FindAnyObjectByType<CageTerminal>();
            if (cage)
            {
                cp.boss.hasCage = true;
                cp.boss.cageOpen = cage.IsOpen;
            }

            cp.quest = new QuestSave();
            var quest = Object.FindAnyObjectByType<Floor1Quest>();
            if (quest)
            {
                cp.quest.hasQuest = true;
                quest.Export(out cp.quest.clearActive, out cp.quest.cleared, out cp.quest.cageOpen, out cp.quest.photoDone, out cp.quest.escaped);
            }
        }

        // ---------- loading ----------

        /// <summary>Before the scene loads: the spawner places the saved enemies and nothing else.</summary>
        public static void Prepare(CheckpointSave cp)
        {
            EnemySpawner.PendingRestore = null;
            if (cp == null || !cp.hasWorld) return;

            var list = new List<EnemySpawner.Snapshot>();
            foreach (var enemy in cp.enemies)
            {
                list.Add(new EnemySpawner.Snapshot
                {
                    Prefab = enemy.prefab, Position = new Vector3(enemy.x, enemy.y, enemy.z), Yaw = enemy.yaw, Health = enemy.health
                });
            }

            EnemySpawner.PendingRestore = list;
        }

        /// <summary>The floor is built: put the rest back.</summary>
        public static void Apply(CheckpointSave cp)
        {
            if (cp == null || !cp.hasWorld) return;

            ApplyBoss(cp.boss);     // first: starting the fight locks the arena's doors, the door states below then win
            ApplyDoors(cp.doors);
            ApplyPickups(cp.pickups);
            ApplyCrates(cp.crates);
            ApplyQuest(cp.quest);
        }

        private static void ApplyBoss(BossSave saved)
        {
            if (saved == null) return;

            var arena = Object.FindAnyObjectByType<BossArena>();
            if (saved.hasArena && arena) arena.RestoreState(saved.started, saved.ended);

            var cage = Object.FindAnyObjectByType<CageTerminal>();
            if (saved.hasCage && saved.cageOpen && cage) cage.RestoreOpen();

            var boss = arena && arena.Boss ? arena.Boss : Object.FindAnyObjectByType<BossController>();
            if (saved.hasBoss && boss) boss.RestoreState(new Vector3(saved.x, saved.y, saved.z), saved.yaw, saved.quadruped);
        }

        private static void ApplyDoors(List<DoorSave> saved)
        {
            var byKey = new Dictionary<string, DoorSave>();
            foreach (var door in saved) byKey[door.key] = door;

            foreach (var door in new List<Door>(Door.Active))
            {
                if (door && byKey.TryGetValue(Place(door.transform.position), out var state))
                {
                    door.RestoreState(state.open, state.locked, state.sign);
                }
            }
        }

        // What is no longer in the save was picked up: it goes. What is still there gets its saved count.
        private static void ApplyPickups(List<PickupSave> saved)
        {
            var left = new Dictionary<string, Queue<int>>();
            foreach (var entry in saved)
            {
                if (!left.TryGetValue(entry.key, out var queue)) left[entry.key] = queue = new Queue<int>();
                queue.Enqueue(entry.count);
            }

            foreach (var pickup in Object.FindObjectsByType<ItemPickup>())
            {
                if (!pickup || !pickup.Item) continue;

                if (left.TryGetValue(PickupKey(pickup), out var queue) && queue.Count > 0) pickup.SetCount(queue.Dequeue());
                else Object.Destroy(pickup.gameObject);
            }
        }

        // A crate that is not in the save was emptied and closed (they remove themselves then): it goes again.
        private static void ApplyCrates(List<CrateSave> saved)
        {
            var byKey = new Dictionary<string, CrateSave>();
            foreach (var crate in saved) byKey[crate.key] = crate;

            foreach (var crate in Object.FindObjectsByType<ItemCrate>())
            {
                if (!crate || crate.Grid == null) continue;

                if (!byKey.TryGetValue(Place(crate.transform.position), out var state))
                {
                    Object.Destroy(crate.gameObject);
                    continue;
                }

                crate.Unlocked = state.unlocked;
                SaveSystem.RestoreStacks(crate.Grid, state.stacks);
            }
        }

        private static void ApplyQuest(QuestSave saved)
        {
            if (saved == null || !saved.hasQuest) return;

            var quest = Object.FindAnyObjectByType<Floor1Quest>();
            if (!quest) return;

            quest.Import(saved.clearActive, saved.cleared, saved.cageOpen, saved.photoDone, saved.escaped);

            // The HUD would type out the first task a moment later, over the real one.
            if (saved.clearActive || saved.cleared || saved.cageOpen || saved.photoDone)
            {
                var hud = Object.FindAnyObjectByType<KadrHud>();
                if (hud) hud.SkipStartTask();
            }
        }
    }
}