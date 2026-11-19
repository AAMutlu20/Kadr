using Items;
using UnityEngine;

namespace Photography
{
    /// <summary>
    /// The 12 polaroids on the lid next to the red book: the paid photos, in order (best first), and a dim outline for
    /// every empty slot. Put this on an empty child of the lid mesh, so it swings with the lid.
    ///
    /// Orientation is the same as an InventoryZone: put the object at the TOP LEFT corner of the polaroid area,
    ///   +X (red) = next column, to the right     -Z (blue) = next row, so +Z points up the lid
    ///   +Y (green) = out of the lid, towards the camera.
    /// Easiest: copy the Rotation of your PermanentZone. The grid shows in the Scene view while the object is selected.
    ///
    /// If your lid model already has polaroids baked into its texture, these quads cover them. Line the grid up with
    /// them, or remove the baked ones.
    /// </summary>
    public class LidPhotoGrid : MonoBehaviour
    {
        private const int Columns = 4;
        private const int Rows = 3;

        [SerializeField] private CameraController photoCamera;
        [SerializeField] private SuitcaseRig rig;
        [Tooltip("Distance between two slots (centre to centre), in this object's local units.")]
        [SerializeField, Min(0.001f)] private float cellSize = 0.05f;
        [Tooltip("How far above the lid surface the photos sit.")]
        [SerializeField] private float lift = 0.0012f;

        private PhotoAlbum _album;
        private Texture2D _outline;
        private readonly Material[] _frameMaterials = new Material[Columns * Rows];
        private readonly Material[] _photoMaterials = new Material[Columns * Rows];
        private readonly GameObject[] _photoQuads = new GameObject[Columns * Rows];

        private void Start()
        {
            if (!photoCamera || !rig)
            {
                Debug.LogError("[LidPhotoGrid] Photo Camera or Rig is not assigned.", this);
                enabled = false;
                return;
            }

            _album = photoCamera.Album;
            _outline = MakeOutlineTexture();

            for (var i = 0; i < Columns * Rows; i++) BuildSlot(i);

            _album.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_album != null) _album.Changed -= Refresh;
            if (_outline) Destroy(_outline);
        }

        private void BuildSlot(int index)
        {
            var column = index % Columns;
            var row = index / Columns;
            var centre = new Vector3((column + 0.5f) * cellSize, lift, -(row + 0.5f) * cellSize);
            var tilt = ((index * 53) % 7 - 3) * 1.2f; // a few degrees, so they do not look printed

            MakeQuad("Frame " + index, centre, tilt, new Vector2(cellSize * 0.9f, cellSize * 1.0f), out _frameMaterials[index]);

            var photoCentre = centre + new Vector3(0f, 0.0005f, cellSize * 0.08f);
            _photoQuads[index] = MakeQuad("Photo " + index, photoCentre, tilt, new Vector2(cellSize * 0.76f, cellSize * 0.76f), out _photoMaterials[index]);
        }

        private GameObject MakeQuad(string quadName, Vector3 localCentre, float tilt, Vector2 size, out Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = quadName;

            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = localCentre;
            // A quad faces -Z: turning it 90 degrees about X lays it face up on the surface (its top towards +Z).
            quad.transform.localRotation = Quaternion.Euler(0f, tilt, 0f) * Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);

            material = new Material(Shader.Find("Sprites/Default"));
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            quad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SuitcaseRig.SetLayerRecursive(quad, rig.Layer);
            return quad;
        }

        private void Refresh()
        {
            var paid = _album.GetPaid();

            for (var i = 0; i < Columns * Rows; i++)
            {
                var photo = i < paid.Count ? paid[i] : null;

                if (photo != null && photo.Image)
                {
                    _frameMaterials[i].mainTexture = null;
                    _frameMaterials[i].color = Color.white;
                    _photoMaterials[i].mainTexture = photo.Image;
                    _photoQuads[i].SetActive(true);
                }
                else
                {
                    _frameMaterials[i].mainTexture = _outline;
                    _frameMaterials[i].color = new Color(1f, 1f, 1f, 0.35f);
                    _photoQuads[i].SetActive(false);
                }
            }
        }

        private static Texture2D MakeOutlineTexture()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var edge = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                    pixels[y * size + x] = edge ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.05f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);

            for (var c = 0; c <= Columns; c++)
            {
                Gizmos.DrawLine(new Vector3(c * cellSize, 0f, 0f), new Vector3(c * cellSize, 0f, -Rows * cellSize));
            }

            for (var r = 0; r <= Rows; r++)
            {
                Gizmos.DrawLine(new Vector3(0f, 0f, -r * cellSize), new Vector3(Columns * cellSize, 0f, -r * cellSize));
            }
        }
    }
}