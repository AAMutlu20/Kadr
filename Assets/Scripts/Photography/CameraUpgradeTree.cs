using System;
using System.Collections.Generic;
using UnityEngine;

namespace Photography
{
    public enum CameraBranch { None, Safe, Valuable }

    public enum CameraAbility { None, Stun, Disorient, Fireshow, Reveal }

    /// <summary>The numbers a branch tier can change. Each tier SETS the listed stats, later tiers overwrite earlier ones.</summary>
    public enum CameraStat
    {
        RaiseTime,
        WindupTime,
        RecoverTime,
        RaisedSpeed,
        WindupMoveSpeed,
        Vulnerability,
        Zoom,
        MaxDistance,
        AttackBonus,
        GroupBonusMax,
        PerfectFramingBonus,
        RepeatSecond,
        RepeatThird,
        RepeatFloor,
        AbilityCooldownMultiplier
    }

    [Serializable]
    public struct StatSet
    {
        public CameraStat stat;
        public float value;
    }

    [Serializable]
    public class BranchTier
    {
        public string title;
        [TextArea] public string description;
        [Tooltip("Price in camera points. The last tier of a branch costs 2.")]
        public int costPoints = 1;
        public List<StatSet> sets = new();
    }

    [Serializable]
    public class BranchData
    {
        public CameraBranch branch;
        public string title;
        [TextArea] public string tagline;
        public List<BranchTier> tiers = new();
    }

    [Serializable]
    public class AbilityLevel
    {
        [TextArea] public string description;
        [Tooltip("Seconds the effect lasts. Disorient: 0 or less means until the enemy dies.")]
        public float duration = 1f;
        [Tooltip("Seconds before the ability can fire again, after a photo that used it.")]
        public float cooldown = 10f;
        [Tooltip("Price in camera points. Ability levels cost 1, 2, 3.")]
        public int costPoints = 1;
    }

    [Serializable]
    public class AbilityData
    {
        public CameraAbility ability;
        public string title;
        [TextArea] public string description;
        [Tooltip("Fireshow only: damage per second while burning.")]
        public float damagePerSecond = 5f;
        public List<AbilityLevel> levels = new();
    }

    /// <summary>
    /// Everything the camera upgrades are: the two exclusive branches (4 tiers each) and the four flash abilities
    /// (3 or 4 levels each). One asset (Assets > Create > Kadr > Camera Upgrade Tree, or the menu Tools > Kadr > Create
    /// Camera Upgrade Tree), all numbers editable in the Inspector. If no asset is assigned anywhere, a default tree
    /// is made in memory, so everything works before the asset exists. Descriptions are plain text: when you change a
    /// number, change the description too.
    /// </summary>
    [CreateAssetMenu(menuName = "Kadr/Camera Upgrade Tree", fileName = "CameraUpgradeTree")]
    public class CameraUpgradeTree : ScriptableObject
    {
        public List<BranchData> branches = new();
        public List<AbilityData> abilities = new();

        public static CameraUpgradeTree Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;

        /// <summary>The tree everything shares. An assigned asset wins; with none, a default one is made.</summary>
        public static CameraUpgradeTree Resolve(CameraUpgradeTree assigned)
        {
            if (assigned)
            {
                Active = assigned;
                return Active;
            }

            if (!Active)
            {
                Active = CreateInstance<CameraUpgradeTree>();
                Active.name = "CameraUpgradeTree (default, in memory)";
                Active.ResetToDefaults();
            }

            return Active;
        }

        public BranchData Branch(CameraBranch branch)
        {
            foreach (var b in branches)
            {
                if (b.branch == branch) return b;
            }

            return null;
        }

        public AbilityData Ability(CameraAbility ability)
        {
            foreach (var a in abilities)
            {
                if (a.ability == ability) return a;
            }

            return null;
        }

        /// <summary>The data of an ability at a level (1 based). Null if the level is 0 or too high.</summary>
        public AbilityLevel Level(CameraAbility ability, int level)
        {
            var data = Ability(ability);
            if (data == null || level < 1 || level > data.levels.Count) return null;
            return data.levels[level - 1];
        }

        [ContextMenu("Reset to defaults")]
        public void ResetToDefaults()
        {
            branches = new List<BranchData>
            {
                new()
                {
                    branch = CameraBranch.Safe, title = "REPORTER",
                    tagline = "Survive the photo. Shorter windup, less exposure, faster flash.",
                    tiers = new List<BranchTier>
                    {
                        Tier("Quick shutter", "Windup 0.6 s to 0.45 s. Takes 1.25x damage instead of 1.5x while shooting.", 1,
                            S(CameraStat.WindupTime, 0.45f), S(CameraStat.Vulnerability, 1.25f)),
                        Tier("Fast flash", "Flash abilities recharge twice as fast. Camera comes up quicker.", 1,
                            S(CameraStat.AbilityCooldownMultiplier, 0.5f), S(CameraStat.RaiseTime, 0.25f)),
                        Tier("Steady hands", "Walk at half speed during the windup. Recovery is halved.", 1,
                            S(CameraStat.WindupMoveSpeed, 0.5f), S(CameraStat.RecoverTime, 0.4f)),
                        Tier("Instant", "No windup: the photo is taken on click. No extra damage taken.", 2,
                            S(CameraStat.WindupTime, 0f), S(CameraStat.Vulnerability, 1f))
                    }
                },
                new()
                {
                    branch = CameraBranch.Valuable, title = "ARTIST",
                    tagline = "Earn more per photo. The camera gets slower and riskier to use.",
                    tiers = new List<BranchTier>
                    {
                        Tier("Telephoto", "Zoom 12 to 20 and subjects count from 45 m instead of 30 m.", 1,
                            S(CameraStat.Zoom, 20f), S(CameraStat.MaxDistance, 45f)),
                        Tier("Studio eye", "Repeat photos lose less value: 1, 0.6, 0.35, then 0.15.", 1,
                            S(CameraStat.RepeatSecond, 0.6f), S(CameraStat.RepeatThird, 0.35f), S(CameraStat.RepeatFloor, 0.15f)),
                        Tier("Long exposure", "Attack bonus 1.5x to 2x, group bonus up to +60%. But the windup takes 0.9 s.", 1,
                            S(CameraStat.AttackBonus, 2f), S(CameraStat.GroupBonusMax, 0.6f), S(CameraStat.WindupTime, 0.9f)),
                        Tier("Masterpiece", "A perfectly framed, fully visible subject is worth 1.5x.", 2,
                            S(CameraStat.PerfectFramingBonus, 1.5f))
                    }
                }
            };

            abilities = new List<AbilityData>
            {
                new()
                {
                    ability = CameraAbility.Stun, title = "FLASH STUN",
                    description = "The flash freezes every enemy in the photo. Bosses are stunned half as long.",
                    levels = new List<AbilityLevel>
                    {
                        Level("Stun 0.5 s", 0.5f, 6f, 1), Level("Stun 1 s", 1f, 6f, 1), Level("Stun 1.5 s", 1.5f, 6f, 2)
                    }
                },
                new()
                {
                    ability = CameraAbility.Disorient, title = "DISORIENT",
                    description = "Enemies in the photo turn on each other. Bosses are immune.",
                    levels = new List<AbilityLevel>
                    {
                        Level("Fight each other for 1.5 s", 1.5f, 15f, 1), Level("Fight each other for 4 s", 4f, 15f, 1),
                        Level("Fight each other until one dies", 0f, 30f, 2)
                    }
                },
                new()
                {
                    ability = CameraAbility.Fireshow, title = "FIRESHOW", damagePerSecond = 5f,
                    description = "Enemies in the photo catch fire and burn for 5 damage every second.",
                    levels = new List<AbilityLevel>
                    {
                        Level("Burns for 3 s", 3f, 12f, 1), Level("Burns for 4 s", 4f, 12f, 1), Level("Burns for 5 s", 5f, 12f, 2)
                    }
                },
                new()
                {
                    ability = CameraAbility.Reveal, title = "REVEAL",
                    description = "After a photo the lens switches to night vision. Lowering the camera ends it.",
                    levels = new List<AbilityLevel>
                    {
                        Level("Night vision for 5 s", 5f, 25f, 1), Level("Night vision for 10 s", 10f, 25f, 1),
                        Level("Night vision for 15 s", 15f, 25f, 2)
                    }
                }
            };

            ResetCostsOnly();
        }

        /// <summary>
        /// Sets every price to the points rule (branch tiers 1 point each, 2 for the last tier; ability levels 1, 2, 3 points) and leaves all texts and numbers alone. Right click the asset header to run it.
        /// </summary>
        [ContextMenu("Reset costs only (keeps your texts)")]
        public void ResetCostsOnly()
        {
            foreach (var branch in branches)
            {
                for (var i = 0; i < branch.tiers.Count; i++) branch.tiers[i].costPoints = i == branch.tiers.Count - 1 ? 2 : 1;
            }

            foreach (var ability in abilities)
            {
                for (var i = 0; i < ability.levels.Count; i++) ability.levels[i].costPoints = i + 1; // 1, 2, 3
            }
        }

        private static StatSet S(CameraStat stat, float value) => new() { stat = stat, value = value };

        private static BranchTier Tier(string title, string description, int cost, params StatSet[] sets) =>
            new() { title = title, description = description, costPoints = cost, sets = new List<StatSet>(sets) };

        private static AbilityLevel Level(string description, float duration, float cooldown, int cost) =>
            new() { description = description, duration = duration, cooldown = cooldown, costPoints = cost };
    }
}