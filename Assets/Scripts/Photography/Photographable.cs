using System.Collections.Generic;
using Enemies;
using Gameplay;
using UnityEngine;

namespace Photography
{
    /// <summary>
    /// Put this on anything the camera can score: the zombie and crawler prefabs, later the bosses.
    /// The type id is what the repeat penalty counts ("zombie", "crawler"), so every zombie variant shares one id.
    /// Leave it empty on an enemy and it uses the EnemyStats id.
    /// </summary>
    public class Photographable : MonoBehaviour
    {
        public static readonly List<Photographable> All = new();

        [Tooltip("What the repeat penalty counts. Empty = the EnemyStats id.")]
        [SerializeField] private string subjectId;
        [Tooltip("Shown in the photo breakdown. Empty = the id.")]
        [SerializeField] private string displayName;
        [Tooltip("1 = common. Higher = rarer and worth more.")]
        [SerializeField, Min(0.1f)] private float rarity = 1f;
        [Tooltip("1 = harmless. Higher = more dangerous and worth more.")]
        [SerializeField, Min(0.1f)] private float danger = 1f;
        [Tooltip("Optional. Points that must be visible for a photo to count (head, chest...). " +
                 "Empty = three points are worked out from the model's bounds.")]
        [SerializeField] private Transform[] anchors;

        private Renderer[] _renderers;
        private Health _health;
        private EnemyController _enemy;

        public string SubjectId => !string.IsNullOrEmpty(subjectId) ? subjectId
            : _enemy && _enemy.Stats ? _enemy.Stats.enemyId : name;

        public string DisplayName => !string.IsNullOrEmpty(displayName) ? displayName : SubjectId;
        public float Rarity => rarity;
        public float Danger => danger;

        /// <summary>A dead enemy is not worth a photo.</summary>
        public bool CanBePhotographed => isActiveAndEnabled && (_health == null || !_health.IsDead);

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _health = GetComponent<Health>();
            _enemy = GetComponent<EnemyController>();
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public EnemyController Enemy => _enemy;

        /// <summary>On fire right now (Fireshow).</summary>
        public bool IsBurning => _enemy && _enemy.TryGetComponent<EnemyStatus>(out var status) && status.IsBurning;

        /// <summary>Turned against the other enemies right now (Disorient).</summary>
        public bool IsDisoriented => _enemy && _enemy.TryGetComponent<EnemyStatus>(out var status) && status.IsDisoriented;

        /// <summary>The attack pose is worth more.</summary>
        public bool IsAttacking => _enemy && _enemy.Fsm.Current is EnemyAttackState;

        public Bounds WorldBounds
        {
            get
            {
                var found = false;
                var bounds = new Bounds(transform.position, Vector3.zero);
                foreach (var r in _renderers)
                {
                    if (!r || !r.enabled) continue;
                    if (!found) { bounds = r.bounds; found = true; }
                    else bounds.Encapsulate(r.bounds);
                }

                return bounds;
            }
        }

        public void GetAnchorPoints(List<Vector3> into)
        {
            into.Clear();

            if (anchors != null && anchors.Length > 0)
            {
                foreach (var a in anchors)
                {
                    if (a) into.Add(a.position);
                }

                if (into.Count > 0) return;
            }

            var b = WorldBounds;
            var c = b.center;
            into.Add(c);
            into.Add(new Vector3(c.x, b.min.y + b.size.y * 0.8f, c.z)); // head height
            into.Add(new Vector3(c.x, b.min.y + b.size.y * 0.25f, c.z)); // legs
        }
    }
}