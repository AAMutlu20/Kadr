using System;
using System.Collections.Generic;
using Core;
using Events;
using UnityEngine;

namespace Photography
{
    /// <summary>
    /// The player's XP and camera points. Put this on the Player (anything always in the scene works).
    ///
    /// XP fills a bar. When the bar is full it gives ONE point and starts again, with a bigger target: the Point Costs
    /// list is the XP the 1st, 2nd, 3rd... point needs. Past the end of the list, every further point costs the last
    /// number. Points are spent on camera upgrades in the suitcase (CameraProgress).
    ///
    /// XP comes from:
    ///  - kills: a number per enemy type (EnemyKilledEvent), so enemies killed by fire or by each other count too
    ///  - discovery: the first good photo of each enemy type, once per type, ever
    ///
    /// Everything is static, so it survives a restart of the run within one session. Saving to disk comes later.
    /// If you already have a class that awards XP for kills, do not use both.
    /// </summary>
    public class PlayerXp : MonoBehaviour
    {
        [Serializable]
        public struct KillXp
        {
            public string enemyId;
            public int xp;
        }

        [Header("Points")]
        [Tooltip("XP needed for the 1st, 2nd, 3rd... point. After the last one every point costs the same as the last.")]
        [SerializeField] private int[] pointCosts = { 100, 200, 400, 700, 1000, 1100, 1200, 1500, 2000 };

        [Header("Kills")]
        [SerializeField] private int defaultKillXp = 10;
        [Tooltip("Per enemy type, by the EnemyStats id. Types not listed give the default.")]
        [SerializeField] private List<KillXp> killXp = new() { new KillXp { enemyId = "Crawler", xp = 20 } };

        [Header("Discovery")]
        [Tooltip("XP for the first photo of a new enemy type.")]
        [SerializeField] private int discoveryXp = 25;
        [Tooltip("How much of the enemy must be in the photo for it to count as discovered.")]
        [SerializeField, Range(0f, 1f)] private float discoveryCoverage = 0.5f;

        private static readonly int[] DefaultCosts = { 100, 200, 400, 700, 1000, 1100, 1200, 1500, 2000 };
        private static int[] _costs = DefaultCosts;
        private static int _points;
        private static int _progress;
        private static int _pointsEarned;
        private static readonly HashSet<string> Discovered = new();

        private EventBus _bus;

        public static int Points => _points;
        public static int Progress => _progress;
        public static int Needed => _costs[Mathf.Min(_pointsEarned, _costs.Length - 1)];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _costs = DefaultCosts;
            _points = _progress = _pointsEarned = 0;
            Discovered.Clear();
        }

        /// <summary>For saving: everything that PlayerXp keeps between runs.</summary>
        public static void Export(out int progress, out int points, out int pointsEarned, out string[] discovered)
        {
            progress = _progress;
            points = _points;
            pointsEarned = _pointsEarned;
            discovered = new string[Discovered.Count];
            Discovered.CopyTo(discovered);
        }

        /// <summary>For loading a save. discovered may be null.</summary>
        public static void Import(int progress, int points, int pointsEarned, IEnumerable<string> discovered)
        {
            _progress = Mathf.Max(0, progress);
            _points = Mathf.Max(0, points);
            _pointsEarned = Mathf.Max(0, pointsEarned);

            Discovered.Clear();
            if (discovered != null)
            {
                foreach (var id in discovered) Discovered.Add(id);
            }

            Publish(0, false);
        }

        public static void Add(int xp)
        {
            if (xp <= 0) return;

            _progress += xp;
            var earned = false;

            while (_progress >= Needed)
            {
                _progress -= Needed;
                _pointsEarned++;
                _points++;
                earned = true;
            }

            Publish(xp, earned);

            if (earned && GameServices.TryGet(out EventBus bus))
            {
                bus.Publish(new NoticeEvent($"Camera point earned  ({_points} to spend)"));
            }
        }

        /// <summary>Spends camera points. False (and nothing taken) if there are not enough.</summary>
        public static bool TrySpend(int points)
        {
            if (points < 0 || _points < points) return false;

            _points -= points;
            Publish(0, false);
            return true;
        }

        private static void Publish(int delta, bool pointEarned)
        {
            if (GameServices.TryGet(out EventBus bus))
            {
                bus.Publish(new XpChangedEvent(_points, _progress, Needed, delta, pointEarned));
            }
        }

        private void Awake()
        {
            if (pointCosts != null && pointCosts.Length > 0) _costs = pointCosts;
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
            Publish(0, false); // so the suitcase shows the real bar straight away
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