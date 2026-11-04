using UnityEngine;

namespace Generation
{
    /// <summary>
    /// Authored data for a room holding which prefab it is and how the generator may use it.
    /// </summary>
    [CreateAssetMenu(fileName = "RoomDefinition", menuName = "Kadr/Room Definition")]
    public class RoomDefinition : ScriptableObject
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private EncounterType encounterType;
        [SerializeField] private SizeClass sizeClass;
        [SerializeField] private DepthMask allowedDepths = DepthMask.All;
        [SerializeField, Min(0f)] private float weight = 1f;

        public GameObject Prefab => prefab;
        public EncounterType EncounterType => encounterType;
        public SizeClass SizeClass => sizeClass;
        public DepthMask AllowedDepths => allowedDepths;
        public float Weight => weight;

        /// <summary>
        /// The RoomInstance on the prefab, or null if the prefab is missing or has none.
        /// </summary>
        public RoomInstance RoomPrefab => prefab ? prefab.GetComponent<RoomInstance>() : null;

        /// <summary>
        /// Read from the prefab, never typed in by hand, so the two cannot disagree.
        /// </summary>
        public int SocketCount => RoomPrefab ? RoomPrefab.SocketCount : 0;

        /// <summary>
        /// True if this room may appear on the given elevator depth (1 to 3).
        /// </summary>
        public bool AllowedOnDepth(int depth)
        {
            if (depth is < 1 or > 3)
            {
                return false;
            }

            var mask = (DepthMask)(1 << (depth - 1));
            return (allowedDepths & mask) != 0;
        }

        private void OnValidate()
        {
            if (prefab && !prefab.GetComponent<RoomInstance>())
            {
                Debug.LogWarning($"{name}: the prefab has no RoomInstance component on its root.", this);
            }
        }
    }
}