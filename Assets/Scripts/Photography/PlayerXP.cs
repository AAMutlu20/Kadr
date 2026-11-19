using System;
using System.Collections.Generic;
using Core;
using Events;
using UnityEngine;

namespace Photography
{
    /// <summary>
    /// The player's XP. XP is only spent on camera upgrades, in the suitcase. Put this on the Player (anything that
    /// is always in the scene works).
    ///
    /// Earned by:
    ///  - kills: a number per enemy type (EnemyKilledEvent), so enemies killed by fire or by each other count too
    ///  - discovery: the first good photo of each enemy type, once per type, ever
    ///
    /// The total and the discovery list are static, so they survive a restart of the run within one session.
    /// Saving to disk comes later with everything else. Publishes XpChangedEvent, which the inventory screen shows.
    ///
    /// If you already have a class that awards XP for kills, do not use both: remove its award, or this component's kill part.
    /// </summary>
    public class PlayerXp : MonoBehaviour
    {
        [Serializable]
        public struct KillXp
        {
            public string enemyId;
            public int xp;
        }

        [Header("Kills")]
        [SerializeField] private int defaultKillXp = 10;
        [Tooltip("Per enemy type, by the EnemyStats id. Types not listed give the default.")]
        [SerializeField] private List<KillXp> killXp = new() { new KillXp { enemyId = "Crawler", xp = 20 } };

        [Header("Discovery")]
        [Tooltip("XP for the first photo of a new enemy type.")]
        [SerializeField] private int discoveryXp = 25;
        [Tooltip("How much of the enemy must be in the photo for it to count as discovered.")]
        [SerializeField, Range(0f, 1f)] private float discoveryCoverage = 0.5f;

        private static int _total;
        private static readonly HashSet<string> Discovered = new();

        private EventBus _bus;

        public static int Total => _total;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _total = 0;
            Discovered.Clear();
        }

        public static void Add(int amount)
        {
            if (amount <= 0) return;

            _total += amount;
            Publish(amount);
        }

        /// <summary>Takes the XP if the player has it. False (and nothing taken) if not.</summary>
        public static bool TrySpend(int amount)
        {
            if (amount < 0 || _total < amount) return false;

            _total -= amount;
            Publish(-amount);
            return true;
        }

        private static void Publish(int delta)
        {
            if (GameServices.TryGet(out EventBus bus)) bus.Publish(new XpChangedEvent(_total, delta));
        }

        private void OnEnable()
        {
            if (!GameServices.TryGet(out _bus)) return;

            _bus.Subscribe<EnemyKilledEvent>(OnKilled);
            _bus.Subscribe<PhotoTakenEvent>(OnPhoto);
        }

        private void OnDisable()
        {
            if (_bus == null) return;

            _bus.Unsubscribe<EnemyKilledEvent>(OnKilled);
            _bus.Unsubscribe<PhotoTakenEvent>(OnPhoto);
            _bus = null;
        }

        private void Start()
        {
            Publish(0); // so the inventory screen shows the real total straight away
        }

        private void OnKilled(EnemyKilledEvent e)
        {
            // If the next line does not compile, your event's id field is named differently: change e.EnemyId.
            var xp = defaultKillXp;
            foreach (var entry in killXp)
            {
                if (entry.enemyId == e.EnemyId) { xp = entry.xp; break; }
            }

            Add(xp);
        }

        private void OnPhoto(PhotoTakenEvent e)
        {
            foreach (var subject in e.Photo.Subjects)
            {
                if (subject.Coverage < discoveryCoverage || !Discovered.Add(subject.Id)) continue;

                Add(discoveryXp);
                if (_bus != null) _bus.Publish(new NoticeEvent($"New creature photographed: {subject.Name}  +{discoveryXp} XP"));
            }
        }
    }
}