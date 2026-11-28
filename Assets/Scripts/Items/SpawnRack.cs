using System;
using Generation;
using UnityEngine;

namespace Items
{
    /// <summary>
    /// A spot on a model where something useful appears: a drawer, a shelf, a couch cushion, the top of a box, the inside
    /// of an open locker. The model itself is not interactive. Put this on an empty child object and move it to the surface.
    /// When the room is created, it spawns one of its prefabs there.
    ///
    ///   Item or crate?  Just what you put in the list: an ItemPickup prefab is an item lying there, an ItemCrate
    ///                   prefab is a crate standing there. Mix them with weights if you like (a drawer: mostly items, sometimes a crate).
    ///   Spawn Chance    1 = always something, 0.5 = half of these spots stay empty.
    ///   Count           how many to place; with more than one they are spread inside Scatter Radius.
    ///   Yaw             0 = prefab keeps this object's facing. 360 = any direction (loose items). Crates usually want 0.
    ///
    /// The object's height is the surface: the bottom of whatever spawns is placed on it, whatever the prefab's pivot is.
    /// Put the object ON the surface, not inside it. The gizmo shows
    /// the facing (blue), the spread (green circle) and a yellow dot for the spawn point.
    /// Spawned things are parented to the room, so they disappear with it.
    /// </summary>
    public class SpawnRack : MonoBehaviour
    {
        [Serializable]
        public class Option
        {
            [Tooltip("An ItemPickup or ItemCrate prefab.")]
            public GameObject prefab;
            [Min(0f)] public float weight = 1f;
        }

        [SerializeField] private Option[] options = Array.Empty<Option>();

        [Header("Rolls")]
        [SerializeField, Range(0f, 1f)] private float spawnChance = 1f;
        [SerializeField, Min(1)] private int countMin = 1;
        [SerializeField, Min(1)] private int countMax = 1;

        [Header("Placement")]
        [Tooltip("Items are spread inside this radius around the object (flat). 0 = all on the exact point.")]
        [SerializeField, Min(0f)] private float scatterRadius;
        [Tooltip("Random turn left and right around the up axis, in degrees. 0 = no turn, 360 = any direction.")]
        [SerializeField, Range(0f, 360f)] private float yaw;

        private bool _spawned;

        private void Start()
        {
            Spawn();
        }

        /// <summary>Rolls and places the items. Runs once; later calls do nothing.</summary>
        public void Spawn()
        {
            if (_spawned) return;
            _spawned = true;

            var roll = WorldRandom.For(transform.position);
            if (roll.Value() > spawnChance) return;

            var total = 0f;
            foreach (var option in options)
            {
                if (option != null && option.prefab) total += option.weight;
            }

            if (total <= 0f)
            {
                Debug.LogWarning($"[SpawnRack] {name}: no prefab with weight above 0.", this);
                return;
            }

            var room = GetComponentInParent<RoomInstance>();
            var parent = room ? room.transform : transform.root;
            var count = roll.Range(countMin, Mathf.Max(countMin, countMax) + 1);

            for (var i = 0; i < count; i++)
            {
                var prefab = PickPrefab(total, roll);

                var offset = Vector3.zero;
                if (scatterRadius > 0f && count > 1)
                {
                    var circle = roll.InsideUnitCircle() * scatterRadius;
                    offset = transform.TransformVector(new Vector3(circle.x, 0f, circle.y));
                }

                var turn = yaw > 0f ? roll.Range(-yaw * 0.5f, yaw * 0.5f) : 0f;
                var rotation = transform.rotation * Quaternion.Euler(0f, turn, 0f);

                var spawned = Instantiate(prefab, transform.position + offset, rotation, parent);
                SitOnHeight(spawned, transform.position.y);
            }
        }

        /// <summary>
        /// Lifts (or lowers) an object so the lowest point of its model touches the given world height,
        /// whatever its pivot is (centre, top or bottom). Also used for the quest item.
        /// </summary>
        public static void SitOnHeight(GameObject spawned, float surfaceY)
        {
            var found = false;
            var bounds = new Bounds();

            foreach (var r in spawned.GetComponentsInChildren<Renderer>())
            {
                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!found)
            {
                foreach (var c in spawned.GetComponentsInChildren<Collider>())
                {
                    if (!found) { bounds = c.bounds; found = true; }
                    else bounds.Encapsulate(c.bounds);
                }
            }

            if (!found) return;

            spawned.transform.position += Vector3.up * (surfaceY - bounds.min.y);
        }

        private GameObject PickPrefab(float total, WorldRandom.Roller random)
        {
            var roll = random.Value() * total;
            GameObject last = null;

            foreach (var option in options)
            {
                if (option == null || !option.prefab || option.weight <= 0f) continue;

                last = option.prefab;
                roll -= option.weight;
                if (roll <= 0f) return option.prefab;
            }

            return last;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f);
            Gizmos.DrawSphere(transform.position, 0.12f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.8f);

            if (scatterRadius <= 0f) return;

            Gizmos.color = Color.green;
            var previous = transform.position + transform.right * scatterRadius;
            for (var i = 1; i <= 24; i++)
            {
                var angle = i / 24f * Mathf.PI * 2f;
                var next = transform.position
                           + transform.right * (Mathf.Cos(angle) * scatterRadius)
                           + transform.forward * (Mathf.Sin(angle) * scatterRadius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}