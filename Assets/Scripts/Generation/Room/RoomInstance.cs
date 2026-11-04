using System;
using System.Collections.Generic;
using UnityEngine;

namespace Generation
{
    /// <summary>
    /// Goes on the root of every room prefab. Collects the room's sockets and its RoomBounds box
    /// so the generator never has to search the hierarchy.
    /// Socket order = list order, sorted by name (Socket_0, Socket_1, etc).
    /// </summary>
    [DisallowMultipleComponent]
    public class RoomInstance : MonoBehaviour
    {
        [SerializeField] private List<Socket> sockets = new();
        [SerializeField] private BoxCollider bounds;

        public IReadOnlyList<Socket> Sockets => sockets;
        public int SocketCount => sockets.Count;
        public BoxCollider Bounds => bounds;

        /// <summary>
        /// A box in world space, ready to hand to Physics.OverlapBox.
        /// </summary>
        public struct WorldBox
        {
            public Vector3 Center;
            public Vector3 HalfExtents;
            public Quaternion Rotation;
        }

        /// <summary>
        /// Returns the bounds box in world space, including the room's current rotation.
        /// </summary>
        public WorldBox GetWorldBox()
        {
            var t = bounds.transform;
            var center = t.TransformPoint(bounds.center);
            var scaled = Vector3.Scale(bounds.size, t.lossyScale) * 0.5f;
            var half = new Vector3(Mathf.Abs(scaled.x), Mathf.Abs(scaled.y), Mathf.Abs(scaled.z));
            return new WorldBox { Center = center, HalfExtents = half, Rotation = t.rotation };
        }

        private void OnValidate()
        {
            Collect();
        }

        /// <summary>
        /// Finds all child sockets and the RoomBounds collider. Also.] available from the component's context menu.
        /// </summary>
        [ContextMenu("Collect Sockets And Bounds")]
        public void Collect()
        {
            sockets.Clear();
            GetComponentsInChildren(true, sockets);
            sockets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            var boundsTransform = transform.Find("RoomBounds");
            bounds = boundsTransform ? boundsTransform.GetComponent<BoxCollider>() : null;

            if (!bounds)
            {
                Debug.LogWarning($"{name}: no child called 'RoomBounds' with a BoxCollider found.", this);
            }
            else if (!bounds.isTrigger)
            {
                Debug.LogWarning($"{name}: RoomBounds collider should have 'Is Trigger' enabled.", this);
            }

            if (sockets.Count == 0)
            {
                Debug.LogWarning($"{name}: no Socket components found in children.", this);
            }

            #if UNITY_EDITOR
            // Without this, a collect run from the context menu may not be saved with the prefab.
            UnityEditor.EditorUtility.SetDirty(this);
            #endif
        }
    }
}