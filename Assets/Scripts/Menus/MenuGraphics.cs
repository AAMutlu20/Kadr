using UnityEngine;
using UnityEngine.UI;

namespace Menus
{
    /// <summary>
    /// A frame: the area between an outer square and an inner square, each with its own size and tilt.
    /// Used for the black border around the tilted "photograph" in the intro (outer = way bigger than the screen,
    /// inner = the tilted square), and for the white photo border (both tilted the same). Centred on its pivot.
    /// </summary>
    public class FrameGraphic : MaskableGraphic
    {
        private Vector2 _outerHalf = new(4000f, 4000f);
        private float _outerAngle;
        private Vector2 _innerHalf;
        private float _innerAngle;

        public void Set(Vector2 outerHalf, float outerAngle, Vector2 innerHalf, float innerAngle)
        {
            _outerHalf = outerHalf;
            _outerAngle = outerAngle;
            _innerHalf = innerHalf;
            _innerAngle = innerAngle;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var outer = Corners(_outerHalf, _outerAngle);
            var inner = Corners(_innerHalf, _innerAngle);

            for (var i = 0; i < 4; i++)
            {
                var j = (i + 1) % 4;
                var start = vh.currentVertCount;

                vh.AddVert(outer[i], color, Vector2.zero);
                vh.AddVert(outer[j], color, Vector2.zero);
                vh.AddVert(inner[j], color, Vector2.zero);
                vh.AddVert(inner[i], color, Vector2.zero);

                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }

        private static Vector3[] Corners(Vector2 half, float angle)
        {
            var rotation = Quaternion.Euler(0f, 0f, angle);
            return new[]
            {
                rotation * new Vector3(-half.x, -half.y, 0f),
                rotation * new Vector3(half.x, -half.y, 0f),
                rotation * new Vector3(half.x, half.y, 0f),
                rotation * new Vector3(-half.x, half.y, 0f)
            };
        }

        /// <summary>Makes one, centred on its parent.</summary>
        public static FrameGraphic Create(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(FrameGraphic));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            var frame = go.GetComponent<FrameGraphic>();
            frame.color = color;
            frame.raycastTarget = false;
            return frame;
        }
    }

    /// <summary>
    /// The four corner marks of a focus box (or a viewfinder). Size the Root and the corners follow.
    /// </summary>
    public class BracketBox
    {
        public readonly RectTransform Root;
        private readonly Image[] _bars = new Image[8];

        public BracketBox(Transform parent, string name, float arm, float thickness, Color color)
        {
            Root = MenuUi.Rect(name, parent);

            for (var corner = 0; corner < 4; corner++)
            {
                // 0 = bottom left, 1 = bottom right, 2 = top left, 3 = top right
                var anchor = new Vector2(corner % 2, corner / 2);

                _bars[corner * 2] = Bar(anchor, new Vector2(arm, thickness), color);
                _bars[corner * 2 + 1] = Bar(anchor, new Vector2(thickness, arm), color);
            }
        }

        private Image Bar(Vector2 anchor, Vector2 size, Color color)
        {
            var image = MenuUi.Box("Bar", Root, color);
            var rect = image.rectTransform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor; // the bar grows inwards from the corner
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            return image;
        }

        public void SetColor(Color color)
        {
            foreach (var bar in _bars) bar.color = color;
        }
    }

    /// <summary>The small textures the menus need, made in code.</summary>
    public static class MenuTextures
    {
        private static Texture2D _noise;
        private static Texture2D _vignette;
        private static Texture2D _disc;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _noise = null;
            _vignette = null;
            _disc = null;
        }

        /// <summary>Grey film grain, tileable.</summary>
        public static Texture2D Noise
        {
            get
            {
                if (_noise) return _noise;

                const int size = 128;
                var random = new System.Random(7);
                var pixels = new Color32[size * size];
                for (var i = 0; i < pixels.Length; i++)
                {
                    var v = (byte)random.Next(256);
                    pixels[i] = new Color32(v, v, v, 255);
                }

                _noise = Make(size, TextureWrapMode.Repeat, pixels);
                return _noise;
            }
        }

        /// <summary>Clear in the middle, dark at the edges.</summary>
        public static Texture2D Vignette
        {
            get
            {
                if (_vignette) return _vignette;

                const int size = 128;
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var d = Distance(x, y, size);
                        var a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.15f, d)) * 0.85f;
                        pixels[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                    }
                }

                _vignette = Make(size, TextureWrapMode.Clamp, pixels);
                return _vignette;
            }
        }

        /// <summary>A hard white circle (the REC dot).</summary>
        public static Texture2D Disc
        {
            get
            {
                if (_disc) return _disc;

                const int size = 64;
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var a = Mathf.Clamp01((1f - Distance(x, y, size)) * 16f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }

                _disc = Make(size, TextureWrapMode.Clamp, pixels);
                return _disc;
            }
        }

        private static float Distance(int x, int y, int size)
        {
            var dx = (x + 0.5f) / size * 2f - 1f;
            var dy = (y + 0.5f) / size * 2f - 1f;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static Texture2D Make(int size, TextureWrapMode wrap, Color32[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}