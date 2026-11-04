using Generation;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Navmesh
{
    /// <summary>
    /// Bakes one NavMesh over a freshly generated level. Listens to LevelGenerator.Generated,
    /// adds a NavMeshSurface to the level's root object and builds it, so the whole level
    /// (every room, with unused doorways already plugged) becomes one connected walkable surface.
    /// The surface lives on the generated root, so destroying the level also removes its NavMesh.
    /// </summary>
    public class NavMeshBaker : MonoBehaviour
    {
        [SerializeField] private LevelGenerator generator;

        [Tooltip("Which agent type to bake for. 0 is the default Humanoid. Set its radius and height in the Navigation window.")]
        [SerializeField] private int agentTypeId;

        [Tooltip("Only geometry on these layers is baked.")]
        [SerializeField] private LayerMask includedLayers = ~0;

        [Tooltip("Height of the Not Walkable block placed over each room's roof. Anything this tall above the room's top is unwalkable.")]
        [SerializeField, Min(1f)] private float roofBlockHeight = 6f;

        private const string BlockerRootName = "NavBlockers";

        private void OnEnable()
        {
            if (generator)
            {
                generator.Generated += Bake;
            }
        }

        private void OnDisable()
        {
            if (generator)
            {
                generator.Generated -= Bake;
            }
        }

        /// <summary>Re-bakes the level that is currently spawned. Useful from the component's context menu.</summary>
        [ContextMenu("Bake Current Level")]
        private void BakeCurrent()
        {
            if (generator && generator.Current != null)
            {
                Bake(generator.Current);
            }
            else
            {
                Debug.LogWarning("[NavMeshBaker] No generated level to bake.", this);
            }
        }

        private void Bake(GeneratedLevel level)
        {
            var startTime = Time.realtimeSinceStartup;

            // Reuse the surface if this level was already baked once.
            var surface = level.Root.GetComponent<NavMeshSurface>();
            if (!surface)
            {
                surface = level.Root.gameObject.AddComponent<NavMeshSurface>();
            }

            surface.agentTypeID = agentTypeId;
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.layerMask = includedLayers;

            BlockRoofs(level);
            surface.BuildNavMesh();

            var elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            Debug.Log($"[NavMeshBaker] Baked the level NavMesh in {elapsed:0} ms.");
        }

        /// <summary>
        /// Rooms are closed boxes, so their roof slabs are upward-facing and would bake as walkable.
        /// A Not Walkable modifier volume over each roof removes that surface without touching the floors.
        /// Rooms whose prefab already contains a NavMeshModifierVolume are skipped and use their own.
        /// </summary>
        private void BlockRoofs(GeneratedLevel level)
        {
            // Already blocked on an earlier bake of this level.
            if (level.Root.Find(BlockerRootName))
            {
                return;
            }

            var blockerRoot = new GameObject(BlockerRootName).transform;
            blockerRoot.SetParent(level.Root, false);

            var notWalkable = NavMesh.GetAreaFromName("Not Walkable");

            foreach (var room in level.Rooms.Values)
            {
                // A room that carries its own blocker (irregular rooms such as the elevator landing) keeps it.
                if (room.GetComponentInChildren<NavMeshModifierVolume>(true))
                {
                    continue;
                }

                if (!room.Bounds)
                {
                    continue;
                }

                var roomBox = ToBounds(room.GetWorldBox());
                var roofTop = roomBox.max.y;

                // Spans from 1.5 below the roof top (covers the slab) up to roofBlockHeight - 1.5 above it,
                // and only over this room's own footprint, so neighbouring rooms are unaffected.
                var blockerObject = new GameObject($"Roof_{room.name}");
                blockerObject.transform.SetParent(blockerRoot, false);

                var volume = blockerObject.AddComponent<NavMeshModifierVolume>();
                volume.area = notWalkable;
                volume.center = new Vector3(roomBox.center.x, roofTop - 1.5f + roofBlockHeight * 0.5f, roomBox.center.z);
                volume.size = new Vector3(roomBox.size.x, roofBlockHeight, roomBox.size.z);
            }
        }

        // The room's bounds box as an axis-aligned world box (exact, as rooms only rotate in 90 degree steps).
        private static Bounds ToBounds(RoomInstance.WorldBox box)
        {
            var result = new Bounds(box.Center, Vector3.zero);
            var h = box.HalfExtents;

            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? -h.x : h.x,
                    (i & 2) == 0 ? -h.y : h.y,
                    (i & 4) == 0 ? -h.z : h.z);
                result.Encapsulate(box.Center + box.Rotation * corner);
            }

            return result;
        }
    }
}