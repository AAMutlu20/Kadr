using Core;
using Events;
using UnityEngine;
using UnityEngine.UI;

namespace Items
{
    /// <summary>
    /// The 2D part of the inventory screen, drawn over the 3D suitcase: the health bar (top left), XP (top right),
    /// the name and description of the item under the cursor (bottom) and a controls hint.
    ///
    /// Both numbers come from the event bus, so this class never asks Health or an XP object for anything while it
    /// runs. The canvas is built in code, so the only setup is this component on an empty GameObject and the
    /// player's Inventory (used to find the player's Health and to filter the health events).
    ///
    /// The bar is a RawImage that scrolls sideways inside a mask whose width follows the health. That clips the
    /// pattern instead of squashing it, and it turns yellow and then red as health drops, like Fine, Caution and
    /// Danger in Resident Evil.
    /// </summary>
    public class InventoryHud : MonoBehaviour
    {
        [SerializeField] private Inventory inventory;
        [Tooltip("Distance from the screen corners, in reference pixels (1920 x 1080).")]
        [SerializeField] private Vector2 margin = new(80f, 70f);
        [SerializeField] private float barWidth = 360f;
        [SerializeField] private float barHeight = 40f;

        private static readonly Color HpFine = new(0.25f, 0.95f, 0.4f);
        private static readonly Color HpCaution = new(0.95f, 0.8f, 0.2f);
        private static readonly Color HpDanger = new(0.95f, 0.2f, 0.15f);
        private const float BarBorder = 3f;

        private GameObject _root;
        private RectTransform _hpMask;
        private RawImage _hpFill;
        private Text _hpText;
        private Texture2D _hpTexture;
        private Text _xpText;
        private Text _nameText;
        private Text _descText;
        private Font _font;
        private EventBus _bus;

        private float _hpCurrent;
        private float _hpMax = 1f;
        private float _hpShown = 1f; // eases towards the real value so the bar drains smoothly
        private float _hpScroll;

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Health published its first event in its own Start, possibly before this existed. Read it once.
            if (inventory && inventory.TryGetComponent<Gameplay.Health>(out var health))
            {
                _hpCurrent = health.Current;
                _hpMax = health.Max;
                _hpShown = _hpMax > 0f ? _hpCurrent / _hpMax : 0f;
            }

            Build();

            if (GameServices.TryGet(out _bus))
            {
                _bus.Subscribe<HealthChangedEvent>(OnHealthChanged);
                _bus.Subscribe<XpChangedEvent>(OnXpChanged);
            }

            _root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.Unsubscribe<HealthChangedEvent>(OnHealthChanged);
                _bus.Unsubscribe<XpChangedEvent>(OnXpChanged);
            }

            if (_hpTexture) Destroy(_hpTexture);
        }

        public void SetVisible(bool visible)
        {
            if (!_root) return;

            _root.SetActive(visible);
            if (visible) _hpShown = _hpMax > 0f ? _hpCurrent / _hpMax : 0f; // no drain animation from a stale value
        }

        public void ShowInfo(string title, string body)
        {
            if (!_nameText) return;

            _nameText.text = title;
            _descText.text = body;
        }

        private void OnHealthChanged(HealthChangedEvent e)
        {
            // Every entity publishes this event, so only the player's own matters here.
            if (!inventory || e.Entity != inventory.gameObject) return;

            _hpCurrent = e.Current;
            _hpMax = e.Max;
        }

        private void OnXpChanged(XpChangedEvent e)
        {
            if (_xpText) _xpText.text = "XP  " + e.Total;
        }

        private void Update()
        {
            if (!_root || !_root.activeSelf) return;

            var target = _hpMax > 0f ? Mathf.Clamp01(_hpCurrent / _hpMax) : 0f;
            _hpShown = Mathf.MoveTowards(_hpShown, target, Time.unscaledDeltaTime * 0.6f);

            // The mask clips the fill instead of squashing it, so the texture does not stretch as health drops.
            var inner = barWidth - BarBorder * 2f;
            _hpMask.sizeDelta = new Vector2(inner * _hpShown, 0f);

            var low = 1f - target;
            _hpScroll = Mathf.Repeat(_hpScroll + Time.unscaledDeltaTime * Mathf.Lerp(0.12f, 0.45f, low), 1f);
            _hpFill.uvRect = new Rect(_hpScroll, 0f, 1f, 1f);

            var color = _hpShown > 0.5f
                ? Color.Lerp(HpCaution, HpFine, (_hpShown - 0.5f) * 2f)
                : Color.Lerp(HpDanger, HpCaution, _hpShown * 2f);

            // A heartbeat pulse when health is low.
            if (target < 0.3f)
            {
                color *= Mathf.Lerp(0.55f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f));
            }

            color.a = 1f;
            _hpFill.color = color;
            _hpText.text = Mathf.CeilToInt(_hpCurrent).ToString();
        }

        // ---------- building the canvas in code ----------

        private void Build()
        {
            var canvasGo = new GameObject("InventoryHudCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // an overlay canvas draws on top of every camera, the inventory camera included

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(canvasGo.transform, false);
            Stretch(_root.GetComponent<RectTransform>());

            BuildHealthBar();

            _xpText = MakeText(_root.transform, "XP  0", 34, TextAnchor.MiddleRight);
            Anchor(_xpText.rectTransform, new Vector2(1f, 1f), new Vector2(-margin.x, -margin.y),
                new Vector2(320f, 44f), new Vector2(1f, 1f));

            var title = MakeText(_root.transform, "ITEMS", 38, TextAnchor.MiddleCenter);
            Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -margin.y),
                new Vector2(400f, 44f), new Vector2(0.5f, 1f));

            _nameText = MakeText(_root.transform, "", 34, TextAnchor.MiddleCenter);
            Anchor(_nameText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 190f),
                new Vector2(1000f, 46f), new Vector2(0.5f, 0f));

            _descText = MakeText(_root.transform, "", 26, TextAnchor.UpperCenter);
            _descText.supportRichText = true;
            Anchor(_descText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 90f),
                new Vector2(1000f, 96f), new Vector2(0.5f, 0f));

            var hint = MakeText(_root.transform, "LMB pick up / place     R rotate     RMB use     TAB close", 24,
                TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.55f);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 30f),
                new Vector2(1400f, 40f), new Vector2(0.5f, 0f));
        }

        private void BuildHealthBar()
        {
            var frame = new GameObject("HealthBar", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(_root.transform, false);
            var frameImage = frame.GetComponent<Image>();
            frameImage.color = new Color(0.35f, 0.4f, 0.35f, 1f);
            frameImage.raycastTarget = false;
            Anchor(frameImage.rectTransform, new Vector2(0f, 1f), new Vector2(margin.x, -margin.y),
                new Vector2(barWidth, barHeight), new Vector2(0f, 1f));

            var back = new GameObject("Back", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(frame.transform, false);
            var backImage = back.GetComponent<Image>();
            backImage.color = new Color(0.03f, 0.05f, 0.03f, 1f);
            backImage.raycastTarget = false;
            Stretch(backImage.rectTransform, BarBorder);

            // The mask is as wide as the filled part. Its child keeps full width, so it is clipped, not squashed.
            var maskGo = new GameObject("Mask", typeof(RectTransform), typeof(RectMask2D));
            maskGo.transform.SetParent(back.transform, false);
            _hpMask = maskGo.GetComponent<RectTransform>();
            _hpMask.anchorMin = new Vector2(0f, 0f);
            _hpMask.anchorMax = new Vector2(0f, 1f);
            _hpMask.pivot = new Vector2(0f, 0.5f);
            _hpMask.anchoredPosition = Vector2.zero;
            _hpMask.sizeDelta = new Vector2(barWidth - BarBorder * 2f, 0f);

            _hpTexture = MakeBarTexture();
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(RawImage));
            fillGo.transform.SetParent(maskGo.transform, false);
            _hpFill = fillGo.GetComponent<RawImage>();
            _hpFill.texture = _hpTexture;
            _hpFill.raycastTarget = false;
            var fillRect = _hpFill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(barWidth - BarBorder * 2f, 0f);

            _hpText = MakeText(frame.transform, "", 22, TextAnchor.MiddleLeft);
            Stretch(_hpText.rectTransform, 12f);
            _hpText.color = new Color(1f, 1f, 1f, 0.9f);
        }

        /// <summary>
        /// A soft white pattern that tiles sideways. Tinted by the bar colour and scrolled through uvRect, it gives
        /// the "living" look without a shader. The noise is sampled on a circle so the left and right edges match.
        /// </summary>
        private static Texture2D MakeBarTexture()
        {
            const int w = 256;
            const int h = 32;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            for (var x = 0; x < w; x++)
            {
                var angle = x / (float)w * Mathf.PI * 2f;
                var wave = 0.5f + 0.5f * Mathf.Sin(angle * 2f);

                for (var y = 0; y < h; y++)
                {
                    var noise = Mathf.PerlinNoise(Mathf.Cos(angle) * 1.5f + 10f,
                        Mathf.Sin(angle) * 1.5f + 10f + y * 0.12f);

                    var v = Mathf.Lerp(0.5f, 1f, wave * 0.5f + noise * 0.5f);

                    // Brighter through the middle, darker at the top and bottom edge, so the bar looks rounded.
                    var edge = 1f - Mathf.Pow(Mathf.Abs(y / (h - 1f) * 2f - 1f), 2f) * 0.5f;
                    v *= edge;

                    tex.SetPixel(x, y, new Color(v, v, v, 1f));
                }
            }

            tex.Apply();
            return tex;
        }

        private Text MakeText(Transform parent, string content, int size, TextAnchor alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.text = content;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}