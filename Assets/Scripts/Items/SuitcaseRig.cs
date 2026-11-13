using System.Collections;
using UnityEngine;

namespace Items
{
    /// <summary>
    /// The 3D suitcase scene: the geometry of the grid, the inventory camera and the lid animation.
    /// It is pure presentation and geometry. It knows nothing about items or input.
    ///
    /// Set it up once as a prefab, placed far away from the level (for example at 2000, 0, 2000) on its own
    /// layer, so the world never lights it and the main camera never sees it:
    ///
    ///   InventoryRig            (this component)
    ///     Suitcase              the model, with an Animator for the lid (optional)
    ///     GridOrigin            empty, at the TOP LEFT corner of the cell area, on the surface items lie on
    ///     InventoryCamera       a camera that looks at the opened suitcase from above
    ///     Light                 one light over the suitcase
    ///
    /// GridOrigin's axes define the grid: its +X is "next column", its -Z is "next row", its +Y is "up".
    /// Select the rig in the Scene view and the grid is drawn as green lines (the gizmo), so you can move GridOrigin
    /// until the lines sit on the suitcase's slots. For the camera, make screen right match GridOrigin's +X and screen
    /// up match its +Z. A camera looking straight down with rotation (90, 0, 0) does that; tilt it a little for depth.
    /// </summary>
    public class SuitcaseRig : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform gridOrigin;
        [SerializeField] private Camera inventoryCamera;
        [Tooltip("Optional. Needs a bool parameter (see below) that opens and closes the lid.")]
        [SerializeField] private Animator lidAnimator;
        [SerializeField] private string openBool = "Open";

        [Header("Grid (must match the Inventory component)")]
        [SerializeField, Min(1)] private int columns = 8;
        [SerializeField, Min(1)] private int rows = 6;
        [Tooltip("Size of one cell in the GridOrigin's local units.")]
        [SerializeField, Min(0.001f)] private float cellSize = 0.1f;

        [Header("Layer")]
        [Tooltip("The layer the whole rig is moved to. The inventory camera only draws this layer. " +
                 "Create it in Project Settings > Tags and Layers, and untick it on the main camera.")]
        [SerializeField] private string layerName = "Inventory";

        private int _openHash;
        private Coroutine _cameraOff;

        public Transform GridOrigin => gridOrigin;
        public Camera Camera => inventoryCamera;
        public float CellSize => cellSize;
        public int Layer { get; private set; }

        private void Awake()
        {
            if (!gridOrigin || !inventoryCamera)
            {
                Debug.LogError("[SuitcaseRig] Grid Origin or Inventory Camera is not assigned.", this);
                enabled = false;
                return;
            }

            Layer = LayerMask.NameToLayer(layerName);
            if (Layer < 0)
            {
                Debug.LogError($"[SuitcaseRig] Layer '{layerName}' does not exist. Create it in Tags and Layers.", this);
                Layer = gameObject.layer;
            }

            SetLayerRecursive(gameObject, Layer);
            inventoryCamera.cullingMask = 1 << Layer;
            inventoryCamera.enabled = false; // only on while the inventory is open

            _openHash = Animator.StringToHash(openBool);
        }

        /// <summary>Opens or closes the suitcase. The camera goes off after a delay so the closing animation is seen.</summary>
        public void SetOpen(bool open, float cameraOffDelay = 0f)
        {
            if (_cameraOff != null)
            {
                StopCoroutine(_cameraOff);
                _cameraOff = null;
            }

            if (open)
            {
                inventoryCamera.enabled = true;
            }
            else if (cameraOffDelay > 0f && isActiveAndEnabled)
            {
                _cameraOff = StartCoroutine(CameraOffAfter(cameraOffDelay));
            }
            else
            {
                inventoryCamera.enabled = false;
            }

            if (lidAnimator && !string.IsNullOrEmpty(openBool)) lidAnimator.SetBool(_openHash, open);
        }

        private IEnumerator CameraOffAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            inventoryCamera.enabled = false;
            _cameraOff = null;
        }

        /// <summary>True if the rig's grid size matches the inventory's. Call once at startup.</summary>
        public bool Matches(InventoryGrid grid) => grid.Width == columns && grid.Height == rows;

        /// <summary>Centre of a footprint (in cells) in GridOrigin's local space, 'lift' above the surface.</summary>
        public Vector3 FootprintCenter(int x, int y, int w, int h, float lift)
        {
            return new Vector3((x + w * 0.5f) * cellSize, lift, -(y + h * 0.5f) * cellSize);
        }

        /// <summary>
        /// Where the mouse points on the plane 'lift' above the grid surface, in GridOrigin's local space.
        /// This replaces the video's trigger colliders: a ray and a bit of maths find the cell without physics.
        /// </summary>
        public bool TryRaycast(Vector2 screenPoint, float lift, out Vector3 local)
        {
            var ray = inventoryCamera.ScreenPointToRay(screenPoint);
            var plane = new Plane(gridOrigin.up, gridOrigin.TransformPoint(new Vector3(0f, lift, 0f)));

            if (!plane.Raycast(ray, out var distance))
            {
                local = default;
                return false;
            }

            local = gridOrigin.InverseTransformPoint(ray.GetPoint(distance));
            local.y = lift;
            return true;
        }

        /// <summary>The cell under a local point. False when it is outside the grid.</summary>
        public bool TryGetCell(Vector3 local, InventoryGrid grid, out int x, out int y)
        {
            x = Mathf.FloorToInt(local.x / cellSize);
            y = Mathf.FloorToInt(-local.z / cellSize);
            return grid.InBounds(x, y);
        }

        /// <summary>
        /// Top left cell for a w x h item whose centre is at the local point, rounded to the nearest cell.
        /// </summary>
        public void AnchorFromPoint(Vector3 local, int w, int h, out int ax, out int ay)
        {
            ax = Mathf.RoundToInt(local.x / cellSize - w * 0.5f);
            ay = Mathf.RoundToInt(-local.z / cellSize - h * 0.5f);
        }

        public static void SetLayerRecursive(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!gridOrigin) return;

            Gizmos.matrix = gridOrigin.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);

            var width = columns * cellSize;
            var depth = rows * cellSize;

            for (var c = 0; c <= columns; c++)
            {
                Gizmos.DrawLine(new Vector3(c * cellSize, 0f, 0f), new Vector3(c * cellSize, 0f, -depth));
            }

            for (var r = 0; r <= rows; r++)
            {
                Gizmos.DrawLine(new Vector3(0f, 0f, -r * cellSize), new Vector3(width, 0f, -r * cellSize));
            }
        }
    }
}