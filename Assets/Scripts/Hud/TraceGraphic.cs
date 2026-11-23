using UnityEngine;
using UnityEngine.UI;

namespace Hud
{
    /// <summary>
    /// A scrolling monitor trace for the pulse. Push() adds a value (-1..1) on the right, everything else moves left.
    /// It is a RawImage showing a small texture that is redrawn when new values arrive, so it needs no custom mesh.
    /// White pixels, tinted by the RawImage colour (set .color to colour the whole trace). Layers, back to front:
    /// a faint graticule that scrolls with the line, a dark under-stroke (readable on bright walls), a soft glow, the
    /// line itself, and a bright head dot at the newest sample that swells with HeadPulse (0..1). The oldest 20% fades in.
    /// </summary>
    public class TraceGraphic : RawImage
    {
        [SerializeField, Min(1)] private int thickness = 3;
        [SerializeField, Min(8)] private int gridSpacing = 32;

        /// <summary>0..1. Swells the head dot, set it to 1 on every heartbeat and let it fall.</summary>
        public float HeadPulse
        {
            get => _headPulse;
            set
            {
                if (Mathf.Approximately(value, _headPulse)) return;

                _headPulse = value;
                _dirty = true;
            }
        }

        private Texture2D _texture;
        private Color32[] _pixels;
        private float[] _samples;
        private int _head;
        private int _width;
        private int _height;
        private int _scroll;
        private float _headPulse;
        private bool _dirty;

        public void Init(int width, int height)
        {
            _width = Mathf.Max(16, width);
            _height = Mathf.Max(16, height);
            _samples = new float[_width];
            _pixels = new Color32[_width * _height];
            _head = 0;
            _scroll = 0;

            if (_texture) Destroy(_texture);
            _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            texture = _texture;
            _dirty = true;
        }

        public void Push(float value)
        {
            if (_samples == null) return;

            _samples[_head] = Mathf.Clamp(value, -1f, 1f);
            _head = (_head + 1) % _width;
            _scroll++;
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (!_dirty || _texture == null) return;

            _dirty = false;
            System.Array.Clear(_pixels, 0, _pixels.Length);

            DrawGrid();
            DrawLine(1, -1, 0, 0.8f, thickness + 1);   // dark under-stroke
            DrawLine(0, 0, 255, 0.22f, thickness + 5); // glow
            DrawLine(0, 0, 255, 1f, thickness);        // the line
            DrawHead();

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        private float Y(int column)
        {
            var half = (_height - 1) * 0.5f - thickness - 3f;
            return (_height - 1) * 0.5f + _samples[(_head + column) % _width] * half;
        }

        private void DrawGrid()
        {
            for (var x = 0; x < _width; x++)
            {
                var fade = Mathf.Clamp01(x / (_width * 0.2f));
                var vertical = (x + _scroll) % gridSpacing == 0;

                for (var y = 0; y < _height; y++)
                {
                    var centreLine = y == _height / 2;
                    var quarter = y == _height / 4 || y == _height * 3 / 4;
                    if (!vertical && !centreLine && !quarter) continue;

                    var strength = vertical ? 0.13f : centreLine ? 0.2f : 0.08f;
                    Plot(x, y, 255, (byte)(255f * fade * strength));
                }
            }
        }

        private void DrawLine(int offsetX, int offsetY, byte shade, float strength, int width)
        {
            var previousY = -1f;

            for (var x = 0; x < _width; x++)
            {
                var y = Y(x);
                var fade = Mathf.Clamp01(x / (_width * 0.2f));
                var alpha = (byte)(255f * fade * strength);

                var from = Mathf.RoundToInt(previousY < 0f ? y : Mathf.Min(previousY, y));
                var to = Mathf.RoundToInt(previousY < 0f ? y : Mathf.Max(previousY, y));
                previousY = y;

                for (var py = from - width / 2; py <= to + width / 2; py++)
                {
                    for (var px = x; px < x + 2; px++) Plot(px + offsetX, py + offsetY, shade, alpha);
                }
            }
        }

        private void DrawHead()
        {
            var x = _width - 2;
            var y = Mathf.RoundToInt(Y(_width - 1));
            var core = 2 + Mathf.RoundToInt(_headPulse * 3f);
            var halo = core + 4;

            for (var dy = -halo; dy <= halo; dy++)
            {
                for (var dx = -halo; dx <= halo; dx++)
                {
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distance <= core) Plot(x + dx, y + dy, 255, 255);
                    else if (distance <= halo) Plot(x + dx, y + dy, 255, (byte)(255f * 0.35f * (1f - (distance - core) / (halo - core))));
                }
            }
        }

        /// <summary>The strongest pixel wins, so the dark outline stays under the glow and the line stays over both.</summary>
        private void Plot(int x, int y, byte shade, byte alpha)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return;

            var index = y * _width + x;
            if (alpha <= _pixels[index].a) return;

            _pixels[index] = new Color32(shade, shade, shade, alpha);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_texture) Destroy(_texture);
        }
    }
}