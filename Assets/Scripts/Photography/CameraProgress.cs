using System;
using UnityEngine;

namespace Photography
{
    /// <summary>The numbers that are not part of CameraStats or ScoringSettings but still come from upgrades.</summary>
    public sealed class CameraLoadout
    {
        public float[] repeatTable = { 1f, 0.5f, 0.25f, 0.1f };
        public float abilityCooldownMultiplier = 1f;
    }

    /// <summary>
    /// What the player owns of the camera upgrades: one branch (locked in by the first purchase), how many tiers of it,
    /// the level of every ability and which ability is equipped. Plain C#, no Unity objects, so it can be saved later.
    /// One shared instance (Current) outlives a run, like PlayerXp.
    /// </summary>
    public sealed class CameraProgress
    {
        public static CameraProgress Current { get; private set; } = new();

        private readonly int[] _levels = new int[5]; // by CameraAbility

        public CameraBranch Branch { get; private set; }
        public int BranchTier { get; private set; }
        public CameraAbility Equipped { get; private set; }

        /// <summary>Raised after any purchase or equip.</summary>
        public event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = new CameraProgress();

        public int AbilityLevel(CameraAbility ability) => _levels[(int)ability];

        public bool IsBranchLocked(CameraBranch branch) => Branch != CameraBranch.None && Branch != branch;

        // ---------- buying ----------

        /// <summary>XP price of the next tier of this branch. -1 if there is none, or the branch is locked out.</summary>
        public int NextBranchTierCost(CameraUpgradeTree tree, CameraBranch branch)
        {
            if (IsBranchLocked(branch)) return -1;

            var data = tree.Branch(branch);
            var next = (Branch == branch ? BranchTier : 0) + 1;
            if (data == null || next > data.tiers.Count) return -1;

            return data.tiers[next - 1].costUnits * tree.xpUnit;
        }

        public bool TryBuyBranchTier(CameraUpgradeTree tree, CameraBranch branch)
        {
            var cost = NextBranchTierCost(tree, branch);
            if (cost < 0 || !PlayerXp.TrySpend(cost)) return false;

            Branch = branch;
            BranchTier++;
            Changed?.Invoke();
            return true;
        }

        /// <summary>XP price of the next level of an ability. -1 if it is at its top level.</summary>
        public int NextAbilityLevelCost(CameraUpgradeTree tree, CameraAbility ability)
        {
            var next = _levels[(int)ability] + 1;
            var level = tree.Level(ability, next);
            return level == null ? -1 : level.costUnits * tree.xpUnit;
        }

        public bool TryBuyAbilityLevel(CameraUpgradeTree tree, CameraAbility ability)
        {
            var cost = NextAbilityLevelCost(tree, ability);
            if (cost < 0 || !PlayerXp.TrySpend(cost)) return false;

            _levels[(int)ability]++;
            if (Equipped == CameraAbility.None) Equipped = ability; // the first ability is equipped by itself
            Changed?.Invoke();
            return true;
        }

        public bool Equip(CameraAbility ability)
        {
            if (ability == CameraAbility.None || _levels[(int)ability] < 1) return false;

            Equipped = ability;
            Changed?.Invoke();
            return true;
        }

        // ---------- using it ----------

        /// <summary>Writes the owned branch tiers into the camera's numbers. The inputs are copies of the base values.</summary>
        public CameraLoadout Apply(CameraUpgradeTree tree, CameraStats stats, ScoringSettings scoring)
        {
            var loadout = new CameraLoadout();
            var data = Branch == CameraBranch.None ? null : tree.Branch(Branch);
            if (data == null) return loadout;

            for (var i = 0; i < BranchTier && i < data.tiers.Count; i++)
            {
                foreach (var set in data.tiers[i].sets)
                {
                    switch (set.stat)
                    {
                        case CameraStat.RaiseTime: stats.raiseTime = set.value; break;
                        case CameraStat.WindupTime: stats.windupTime = set.value; break;
                        case CameraStat.RecoverTime: stats.recoverTime = set.value; break;
                        case CameraStat.RaisedSpeed: stats.raisedSpeed = set.value; break;
                        case CameraStat.WindupMoveSpeed: stats.windupMoveSpeed = set.value; break;
                        case CameraStat.Vulnerability: stats.vulnerability = set.value; break;
                        case CameraStat.Zoom: stats.zoom = set.value; break;
                        case CameraStat.MaxDistance: scoring.maxDistance = set.value; break;
                        case CameraStat.AttackBonus: scoring.attackBonus = set.value; break;
                        case CameraStat.GroupBonusMax: scoring.groupBonusMax = set.value; break;
                        case CameraStat.PerfectFramingBonus: scoring.perfectFramingBonus = set.value; break;
                        case CameraStat.RepeatSecond: loadout.repeatTable[1] = set.value; break;
                        case CameraStat.RepeatThird: loadout.repeatTable[2] = set.value; break;
                        case CameraStat.RepeatFloor: loadout.repeatTable[3] = set.value; break;
                        case CameraStat.AbilityCooldownMultiplier: loadout.abilityCooldownMultiplier = set.value; break;
                    }
                }
            }

            return loadout;
        }
    }
}