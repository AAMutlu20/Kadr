using UnityEngine;

namespace Items
{
    /// <summary>
    /// One grid area inside the suitcase: the Consumables area on the floor of the bottom half, or the Permanent
    /// area on the inside of the lid. Put it on an empty object at the TOP LEFT corner of the area; the object's own
    /// axes define the grid:
    ///
    ///   +X (red)    next column, towards the screen's right
    ///   -Z (blue)   next row, so +Z points UP the area, towards the top of the screen
    ///   +Y (green)  the surface normal, pointing out of the surface towards the camera
    ///
    /// For the lid, make this object a CHILD of the lid mesh, so the zone (and the items on it) swings with the lid.
    /// The grid is drawn as lines in the Scene view (orange for consumables, green for permanent items).
    ///
    /// It is geometry only. The view finds cells with a ray against the zone's plane, not with colliders.
    /// </summary>
    public class InventoryZone : MonoBehaviour
    {
        [SerializeField] private ItemCategory category = ItemCategory.Consumable;
        [Tooltip("Must match the Inventory's width for this category.")]
        [SerializeField, Min(1)] private int columns = 8;
        [Tooltip("Must match the Inventory's height for this category.")]
        [SerializeField, Min(1)] private int rows = 4;
        [Tooltip("Size of one cell, in this object's local units.")]
        [SerializeField, Min(0.001f)] private float cellSize = 0.06f;

        [Header("Visible grid (in the game)")]
        [SerializeField] private bool showGrid = true;
        [Tooltip("Colour of the slot area (alpha = how much of the suitcase shows through).")]
        [SerializeField] private Color gridBackColor = new(0.10f, 0.10f, 0.10f, 0.6f);
        [Tooltip("Colour of the lines between cells. Darker than the area.")]
        [SerializeField] private Color gridLineColor = new(0f, 0f, 0f, 0.9f);
        [Tooltip("Line thickness as a fraction of one cell.")]
        [SerializeField, Range(0.01f, 0.15f)] private float gridLineThickness = 0.05f;

        public ItemCategory Category => category;
        public int Columns => columns;
        public int Rows => rows;
        public float CellSize => cellSize;

        /// <summary>The grid this area draws. Bound once by the inventory screen.</summary>
        public InventoryGrid Grid { get; private set; }

        public bool Bind(InventoryGrid grid)
        {
            Grid = grid;
            return grid.Width == columns && grid.Height == rows;
        }

        /// <summary>
        /// Builds the grid as ONE flat quad just above the surface. Its material is a small generated cell texture
        /// (dark background, darker border) that repeats once per cell, with mipmaps and anisotropic filtering so the
        /// lines stay clean at a slant. A child object, so it swings with the lid. Called once by the inventory screen.
        /// </summary>
        public void BuildGridVisual(int layer)
        {
            if (!showGrid) return;

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name + " cell",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };

            // The line is centred on the cell border, so each cell paints half of it on every side and neighbours join up.
            var edge = Mathf.Max(1, Mathf.RoundToInt(size * gridLineThickness * 0.5f));
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var onLine = x < edge || x >= size - edge || y < edge || y >= size - edge;
                    pixels[y * size + x] = onLine ? gridLineColor : gridBackColor;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true);

            var width = columns * cellSize;
            var depth = rows * cellSize;

            var mesh = new Mesh { name = name + " grid" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(width, 0f, 0f),
                new Vector3(width, 0f, -depth), new Vector3(0f, 0f, -depth)
            };
            // One texture repeat per cell.
            mesh.uv = new[]
            {
                new Vector2(0f, rows), new Vector2(columns, rows), new Vector2(columns, 0f), new Vector2(0f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();

            var go = new GameObject("GridPlane");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.001f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture }; // unlit, alpha, no culling
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = material;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            go.layer = layer;
        }

        /// <summary>Centre of a footprint (in cells) in this object's local space, 'lift' above the surface.</summary>
        public Vector3 FootprintCenter(int x, int y, int w, int h, float lift)
        {
            return new Vector3((x + w * 0.5f) * cellSize, lift, -(y + h * 0.5f) * cellSize);
        }

        /// <summary>
        /// Where a ray meets the plane 'lift' above this zone's surface, in local space. This replaces trigger
        /// colliders: a ray and a bit of maths find the cell without physics.
        /// </summary>
        public bool TryRaycast(Ray ray, float lift, out Vector3 local)
        {
            var plane = new Plane(transform.up, transform.TransformPoint(new Vector3(0f, lift, 0f)));

            if (!plane.Raycast(ray, out var distance))
            {
                local = default;
                return false;
            }

            local = transform.InverseTransformPoint(ray.GetPoint(distance));
            local.y = lift;
            return true;
        }

        /// <summary>True when a local point lies over this zone's rectangle.</summary>
        public bool Contains(Vector3 local)
        {
            return local.x >= 0f && local.x <= columns * cellSize && -local.z >= 0f && -local.z <= rows * cellSize;
        }

        /// <summary>The cell under a local point. False when it is outside the grid.</summary>
        public bool TryGetCell(Vector3 local, out int x, out int y)
        {
            x = Mathf.FloorToInt(local.x / cellSize);
            y = Mathf.FloorToInt(-local.z / cellSize);
            return Grid != null && Grid.InBounds(x, y);
        }

        /// <summary>Top left cell for a w x h item whose centre is at the local point, rounded to the nearest cell.</summary>
        public void AnchorFromPoint(Vector3 local, int w, int h, out int ax, out int ay)
        {
            ax = Mathf.RoundToInt(local.x / cellSize - w * 0.5f);
            ay = Mathf.RoundToInt(-local.z / cellSize - h * 0.5f);
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = category == ItemCategory.Permanent
                ? new Color(0.3f, 1f, 0.4f, 0.9f)
                : new Color(1f, 0.5f, 0.15f, 0.9f);

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