using System.Collections;
using System.Collections.Generic;
using Core;
using Events;
using Gameplay;
using Generation;
using UnityEngine;
using UnityEngine.AI;

namespace Enemies
{
    /// <summary>
    /// Fills a generated level with enemies. It reads the encounter tag of every room (the tagger already decided them)
    /// and places enemies from an EnemySpawnTable. Put one in the scene.
    ///
    /// Flow: the generator publishes LevelGeneratedEvent -> the spawner waits until the NavMesh has been baked
    /// (it polls, so it does not matter whether your bake script runs before or after the event) -> spawns.
    ///
    /// Same seed = same enemies in the same rooms: every room uses its own random stream made from the level seed
    /// and its node id.
    ///
    /// What is built:
    ///   Combat   done. Idle enemies on random NavMesh points inside the room, away from the doors and the player.
    ///   Ambush, Patrol, Elite   empty functions with TODO comments (see the bottom of this file).
    ///   Safe, Transit, Boss     never get enemies.
    ///
    /// Enemies are spawned active, so ThreatTracker (the cage terminal) counts every one of them from the start.
    /// While the spawner waits for the bake it holds one pending threat, so the terminal can never say "clear" early.
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private LevelGenerator generator;
        [SerializeField] private EnemySpawnTable table;

        [Header("Limits")]
        [Tooltip("Most enemies on the whole floor. When the rooms want more, the fullest rooms lose enemies first.")]
        [SerializeField, Min(0)] private int maxEnemiesOnFloor = 12;

        [Header("Placement")]
        [Tooltip("Enemies stay this far from the walls.")]
        [SerializeField, Min(0f)] private float wallMargin = 3f;
        [Tooltip("Enemies are never placed closer than this to a doorway.")]
        [SerializeField, Min(0f)] private float doorClearance = 8f;
        [Tooltip("Enemies are never placed closer than this to the player (the player's position when the level appears).")]
        [SerializeField, Min(0f)] private float playerClearance = 30f;
        [Tooltip("Minimum distance between two enemies.")]
        [SerializeField, Min(0f)] private float enemySpacing = 5f;
        [Tooltip("Random points tried per enemy before giving up on it.")]
        [SerializeField, Min(1)] private int attemptsPerEnemy = 40;

        [Header("NavMesh")]
        [Tooltip("Seconds to wait for the NavMesh to be baked before giving up.")]
        [SerializeField, Min(1f)] private float navMeshTimeout = 20f;

        private class Plan
        {
            public LevelNode Node;
            public RoomInstance Room;
            public System.Random Rng;
            public int Count;
        }

        // Why points were rejected in the last TryFindPoint: no NavMesh, outside box, door, player, other enemy.
        private readonly int[] _rejects = new int[5];

        private EventBus _bus;
        private Coroutine _running;
        private Transform _player;

        private void OnEnable()
        {
            if (!generator) generator = FindAnyObjectByType<LevelGenerator>();
            if (!GameServices.TryGet(out _bus)) return;

            _bus.Subscribe<LevelGeneratedEvent>(OnLevelGenerated);
        }

        private void OnDisable()
        {
            _bus?.Unsubscribe<LevelGeneratedEvent>(OnLevelGenerated);
            _running = null;
        }

        private void OnLevelGenerated(LevelGeneratedEvent e)
        {
            if (!generator || generator.Current == null)
            {
                Debug.LogWarning("[EnemySpawner] No LevelGenerator assigned or no level built.", this);
                return;
            }

            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Populate(generator.Current));
        }

        private IEnumerator Populate(GeneratedLevel level)
        {
            if (!table)
            {
                Debug.LogWarning("[EnemySpawner] No spawn table assigned: the level stays empty.", this);
                yield break;
            }

            var probeAgent = FirstAgentType();
            if (probeAgent == null)
            {
                Debug.LogWarning("[EnemySpawner] The spawn table has no enemy prefab with weight above 0.", this);
                yield break;
            }

            // Hold one pending threat until the enemies exist.
            ThreatTracker.AddPending();
            try
            {
                var filter = new NavMeshQueryFilter { agentTypeID = probeAgent.Value, areaMask = NavMesh.AllAreas };
                var probe = level.Rooms[LevelGraph.StartId].GetWorldBox().Center;
                var waited = 0f;

                while (!NavMesh.SamplePosition(probe, out _, 12f, filter))
                {
                    if (!level.Root) yield break;

                    waited += Time.unscaledDeltaTime;
                    if (waited > navMeshTimeout)
                    {
                        Debug.LogError("[EnemySpawner] No NavMesh appeared within the timeout. Bake the Humanoid surface after the level is generated.", this);
                        yield break;
                    }

                    yield return null;
                }

                if (level.Root) Spawn(level);
            }
            finally
            {
                ThreatTracker.RemovePending();
                _running = null;
            }
        }

        private void Spawn(GeneratedLevel level)
        {
            var player = FindAnyObjectByType<PlayerController>();
            _player = player ? player.transform : null;

            var plans = new List<Plan>();

            foreach (var node in level.Graph.Nodes)
            {
                if (!level.Rooms.TryGetValue(node.Id, out var room) || !room) continue;

                // Each room has its own stream, so changing one room's rules does not shuffle the others.
                var rng = new System.Random(unchecked(level.Seed * 31 + level.Attempt * 7919 + node.Id * 104729));

                switch (node.Encounter)
                {
                    case EncounterType.Combat:
                        plans.Add(PlanCombat(node, room, rng));
                        break;
                    case EncounterType.Ambush:
                        SpawnAmbush(level, node, room, rng);
                        break;
                    case EncounterType.Patrol:
                        SpawnPatrol(level, node, room, rng);
                        break;
                    case EncounterType.Elite:
                        SpawnElite(level, node, room, rng);
                        break;
                    // Safe, Transit and Boss rooms get nothing.
                }
            }

            ApplyFloorCap(plans);

            var taken = new List<Vector3>();
            var total = 0;
            foreach (var plan in plans)
            {
                total += PlaceCombat(level, plan, taken);
            }

            Debug.Log($"[EnemySpawner] {total} enemies in {plans.Count} combat rooms (cap {maxEnemiesOnFloor}).");
        }

        // Combat

        private Plan PlanCombat(LevelNode node, RoomInstance room, System.Random rng)
        {
            var wanted = rng.Next(table.CombatMin, table.CombatMax + 1);

            var box = room.Bounds;
            var scale = box.transform.lossyScale;
            var area = box.size.x * Mathf.Abs(scale.x) * box.size.z * Mathf.Abs(scale.z);
            var fits = Mathf.Max(1, Mathf.FloorToInt(area / table.AreaPerEnemy));

            return new Plan { Node = node, Room = room, Rng = rng, Count = Mathf.Min(wanted, fits) };
        }

        // Takes one enemy at a time from the fullest room until the floor is within the cap.
        private void ApplyFloorCap(List<Plan> plans)
        {
            var total = 0;
            foreach (var plan in plans) total += plan.Count;

            while (total > maxEnemiesOnFloor)
            {
                Plan fullest = null;
                foreach (var plan in plans)
                {
                    if (plan.Count > 0 && (fullest == null || plan.Count >= fullest.Count)) fullest = plan;
                }

                if (fullest == null) return;

                fullest.Count--;
                total--;
            }
        }

        private int PlaceCombat(GeneratedLevel level, Plan plan, List<Vector3> taken)
        {
            var placed = 0;

            for (var i = 0; i < plan.Count; i++)
            {
                var prefab = PickPrefab(plan.Rng);
                if (!prefab) break;

                if (!TryFindPoint(plan.Room, prefab, plan.Rng, taken, out var point))
                {
                    Debug.LogWarning($"[EnemySpawner] Room {plan.Room.name}: found no free spot for enemy {i + 1} of {plan.Count}. " +
                                     $"Rejected points: {_rejects[0]} no NavMesh, {_rejects[1]} outside the room box, " +
                                     $"{_rejects[2]} too close to a door, {_rejects[3]} too close to the player, " +
                                     $"{_rejects[4]} too close to another enemy.", plan.Room);
                    break;
                }

                var yaw = (float)(plan.Rng.NextDouble() * 360.0);
                Instantiate(prefab, point, Quaternion.Euler(0f, yaw, 0f), level.Root);
                taken.Add(point);
                placed++;
            }

            return placed;
        }

        private EnemyController PickPrefab(System.Random rng)
        {
            var entries = table.Enemies;
            var weights = new float[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                weights[i] = entries[i] != null && entries[i].prefab ? entries[i].weight : 0f;
            }

            var index = RngUtil.PickWeighted(weights, rng);
            return index >= 0 ? entries[index].prefab : null;
        }

        private int? FirstAgentType()
        {
            foreach (var entry in table.Enemies)
            {
                if (entry == null || !entry.prefab || entry.weight <= 0f) continue;

                var agent = entry.prefab.GetComponent<NavMeshAgent>();
                return agent ? agent.agentTypeID : 0;
            }

            return null;
        }

        /// <summary>
        /// A random walkable point inside the room's bounds box that keeps its distance from the doors, the player
        /// and the enemies already placed. The NavMesh is asked with the prefab's own agent type.
        /// </summary>
        private bool TryFindPoint(RoomInstance room, EnemyController prefab, System.Random rng,
            List<Vector3> taken, out Vector3 point)
        {
            point = default;
            System.Array.Clear(_rejects, 0, _rejects.Length);

            var box = room.Bounds;
            var t = box.transform;
            var scale = t.lossyScale;
            var half = box.size * 0.5f;

            var agent = prefab.GetComponent<NavMeshAgent>();
            var filter = new NavMeshQueryFilter
            {
                agentTypeID = agent ? agent.agentTypeID : 0,
                areaMask = NavMesh.AllAreas
            };

            // Margins converted to the box's local units.
            var marginX = Mathf.Min(wallMargin / Mathf.Max(0.001f, Mathf.Abs(scale.x)), half.x * 0.5f);
            var marginZ = Mathf.Min(wallMargin / Mathf.Max(0.001f, Mathf.Abs(scale.z)), half.z * 0.5f);
            var reach = Mathf.Max(2f, box.size.y * Mathf.Abs(scale.y) * 0.5f);

            for (var attempt = 0; attempt < attemptsPerEnemy; attempt++)
            {
                var local = new Vector3(
                    box.center.x + Mathf.Lerp(-half.x + marginX, half.x - marginX, (float)rng.NextDouble()),
                    box.center.y,
                    box.center.z + Mathf.Lerp(-half.z + marginZ, half.z - marginZ, (float)rng.NextDouble()));

                if (!NavMesh.SamplePosition(t.TransformPoint(local), out var hit, reach, filter))
                {
                    _rejects[0]++;
                    continue;
                }

                // The sample can land in the next room or on another level of the same room: it must be inside the box.
                var back = t.InverseTransformPoint(hit.position) - box.center;
                if (Mathf.Abs(back.x) > half.x - marginX || Mathf.Abs(back.z) > half.z - marginZ ||
                    Mathf.Abs(back.y) > half.y)
                {
                    _rejects[1]++;
                    continue;
                }

                var reason = TooClose(hit.position, room, taken);
                if (reason > 0)
                {
                    _rejects[reason]++;
                    continue;
                }

                point = hit.position;
                return true;
            }

            return false;
        }

        // 0 = fine, 2 = door, 3 = player, 4 = other enemy (matches the _rejects slots).
        private int TooClose(Vector3 position, RoomInstance room, List<Vector3> taken)
        {
            foreach (var socket in room.Sockets)
            {
                if (Flat(position, socket.WorldPosition) < doorClearance) return 2;
            }

            if (_player && Flat(position, _player.position) < playerClearance) return 3;

            foreach (var other in taken)
            {
                if (Vector3.Distance(position, other) < enemySpacing) return 4;
            }

            return 0;
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // Encounter types that are not built yet

        /// <summary>
        /// TODO: Ambush. The room looks empty and safe. A trigger volume (placed deeper in the room, not at the door) springs it
        /// once the player steps in. Enemies appear at authored spawn markers that sit out of the player's line of sight
        /// (behind pillars, in side alcoves), never in view. The room's doors lock with Door.Lock until every ambusher is dead.
        /// While armed it must call ThreatTracker.AddPending(count), and RemovePending(count) when the enemies spawn or
        /// the ambush is cancelled, so the cage terminal counts them from the start.
        /// </summary>
        private void SpawnAmbush(GeneratedLevel level, LevelNode node, RoomInstance room, System.Random rng)
        {
        }

        /// <summary>
        /// TODO: Patrol. One or two enemies that walk a loop between authored waypoints in the room (or across the room and
        /// the corridor next to it) instead of standing idle. They go back to the loop when they lose the player.
        /// Needs a new FSM state (EnemyPatrolState) with the waypoints on the enemy, and Idle to hand over to it.
        /// </summary>
        private void SpawnPatrol(GeneratedLevel level, LevelNode node, RoomInstance room, System.Random rng)
        {
        }

        /// <summary>
        /// TODO: Elite. A tougher encounter: a stronger variant of an existing enemy (more health, faster, extra damage)
        /// or a small group of 3 to 4 with one leader. It is placed like Combat but is worth more photo XP, so the
        /// tables need a separate elite prefab list and count range.
        /// </summary>
        private void SpawnElite(GeneratedLevel level, LevelNode node, RoomInstance room, System.Random rng)
        {
        }
    }
}