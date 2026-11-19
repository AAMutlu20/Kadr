using System.Collections.Generic;
using Core;
using Events;
using Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Items
{
    /// <summary>
    /// The 2D parts of the inventory screen. Two kinds:
    ///
    ///  - Panels ON the suitcase lid (world space canvases): the health bar with XP under it, and the "orders" note.
    ///    Each sits at an anchor object you place on the lid, as a child of the lid mesh, so it swings with the lid.
    ///  - Text on the screen (overlay): the name and description of the item under the cursor, and a controls hint.
    ///
    /// Health and XP come from the event bus, so this class never asks Health or an XP object for anything while it
    /// runs. The canvases are built in code. If an anchor is left empty, that panel falls back to a corner of the
    /// screen, so the screen works before the anchors exist.
    ///
    /// ANCHOR ORIENTATION: a world canvas is read when the viewer looks along the anchor's +Z. So the anchor's blue
    /// +Z arrow must point INTO the lid, away from the camera, and its green +Y arrow must point up the lid. The
    /// rectangle drawn in the Scene view shows the panel's size and place.
    ///
    /// The health bar is a RawImage that scrolls sideways inside a mask whose width follows the health. That clips
    /// the pattern instead of squashing it, and it turns yellow and then red as health drops, like Fine, Caution and
    /// Danger in Resident Evil.
    /// </summary>
    public class InventoryHud : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Inventory inventory;
        [SerializeField] private SuitcaseRig rig;
        [Tooltip("The player's Health. Empty = searched for on the Inventory's object, its parents and children, then on the PlayerController.")]
        [SerializeField] private Health playerHealth;

        [Header("Panels on the lid (children of the lid mesh)")]
        [Tooltip("Where the health bar and XP go. Empty = top left of the screen.")]
        [SerializeField] private Transform healthAnchor;
        [Tooltip("Panel width in world units. Its height follows.")]
        [SerializeField] private float healthWidth = 0.3f;
        [Tooltip("Extra turn of the health panel, in degrees. Text upside down: Z 180. Mirrored: Y 180. Set it while NOT playing.")]
        [SerializeField] private Vector3 healthRotation = Vector3.zero;
        [Tooltip("Extra shift of the health panel, in the anchor's own axes (X right, Y up the lid).")]
        [SerializeField] private Vector3 healthOffset = Vector3.zero;
        [Tooltip("Where the orders note goes (the Comisar's current task). Empty = top right of the screen.")]
        [SerializeField] private Transform ordersAnchor;
        [SerializeField] private float ordersWidth = 0.25f;
        [Tooltip("Extra turn of the orders panel, in degrees. Text upside down: Z 180. Mirrored: Y 180. Set it while NOT playing.")]
        [SerializeField] private Vector3 ordersRotation = Vector3.zero;
        [SerializeField] private Vector3 ordersOffset = Vector3.zero;

        [Header("Orders")]
        [SerializeField] private string ordersTitle = "ORDERS";
        [SerializeField, TextArea] private string ordersBody = "Find the documents.";

        private static readonly Vector2 HealthPanelSize = new(420f, 110f);
        private static readonly Vector2 OrdersPanelSize = new(400f, 300f);

        private static readonly Color HpFine = new(0.25f, 0.95f, 0.4f);
        private static readonly Color HpCaution = new(0.95f, 0.8f, 0.2f);
        private static readonly Color HpDanger = new(0.95f, 0.2f, 0.15f);
        private const float BarHeight = 44f;
        private const float BarBorder = 3f;

        private readonly List<GameObject> _worldPanels = new();
        private GameObject _overlayRoot;

        private RectTransform _hpMask;
        private RawImage _hpFill;
        private Text _hpText;
        private Texture2D _hpTexture;
        private Text _xpText;
        private Text _ordersTitleText;
        private Text _ordersBodyText;
        private Text _nameText;
        private Text _descText;
        private Font _font;
        private EventBus _bus;
        private bool _visible;

        private float _hpCurrent;
        private float _hpMax = 1f;
        private float _hpShown = 1f; // eases towards the real value so the bar drains smoothly
        private float _hpScroll;

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            FindHealth();
            ReadHealth();
            _hpShown = _hpMax > 0f ? _hpCurrent / _hpMax : 0f;

            Build();

            if (GameServices.TryGet(out _bus))
            {
                _bus.Subscribe<XpChangedEvent>(OnXpChanged);
            }

            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.Unsubscribe<XpChangedEvent>(OnXpChanged);
            }

            if (_hpTexture) Destroy(_hpTexture);
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;

            if (_overlayRoot) _overlayRoot.SetActive(visible);
            foreach (var panel in _worldPanels)
            {
                if (panel) panel.SetActive(visible);
            }

            if (visible) ReadHealth();
            if (visible) _hpShown = _hpMax > 0f ? _hpCurrent / _hpMax : 0f; // no drain animation from a stale value
        }

        public void ShowInfo(string title, string body)
        {
            if (!_nameText) return;

            _nameText.text = title;
            _descText.text = body;
        }

        /// <summary>The current order from the Comisar. The Quest GUI will call this when the quest changes.</summary>
        public void SetOrders(string title, string body)
        {
            if (_ordersTitleText) _ordersTitleText.text = title;
            if (_ordersBodyText) _ordersBodyText.text = body;
        }

        /// <summary>
        /// Health is read straight from the player's Health component instead of from events. Events were filtered by
        /// "is this the Inventory's own GameObject", which silently shows 0 whenever Health sits on another object.
        /// </summary>
        private void FindHealth()
        {
            if (!playerHealth && inventory)
            {
                playerHealth = inventory.GetComponentInParent<Health>();
                if (!playerHealth) playerHealth = inventory.GetComponentInChildren<Health>();
            }

            if (!playerHealth)
            {
                var player = FindAnyObjectByType<PlayerController>();
                if (player) playerHealth = player.GetComponentInChildren<Health>();
            }

            if (!playerHealth) Debug.LogWarning("[InventoryHud] No player Health found. Assign it in the inspector.", this);
        }

        private void ReadHealth()
        {
            if (!playerHealth) return;

            _hpCurrent = playerHealth.Current;
            _hpMax = playerHealth.Max;
        }

        private void OnXpChanged(XpChangedEvent e)
        {
            if (_xpText) _xpText.text = "XP  " + e.Total;
        }

        private void Update()
        {
            if (!_visible || !_hpFill) return;

            ReadHealth();
            var target = _hpMax > 0f ? Mathf.Clamp01(_hpCurrent / _hpMax) : 0f;
            _hpShown = Mathf.MoveTowards(_hpShown, target, Time.unscaledDeltaTime * 0.6f);

            // The mask clips the fill instead of squashing it, so the texture does not stretch as health drops.
            var inner = HealthPanelSize.x - BarBorder * 2f;
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

        // ---------- building the canvases in code ----------

        private void Build()
        {
            // The overlay: item text and the hint, always on the screen. It also hosts any panel without an anchor.
            var canvasGo = new GameObject("InventoryHudCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // an overlay canvas draws on top of every camera, the inventory camera included

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _overlayRoot = new GameObject("Root", typeof(RectTransform));
            _overlayRoot.transform.SetParent(canvasGo.transform, false);
            Stretch(_overlayRoot.GetComponent<RectTransform>());

            BuildHealthPanel(MakePanel("HealthPanel", healthAnchor, HealthPanelSize, healthWidth, healthRotation, healthOffset,
                new Vector2(0f, 1f), new Vector2(80f, -70f)));
            BuildOrdersPanel(MakePanel("OrdersPanel", ordersAnchor, OrdersPanelSize, ordersWidth, ordersRotation, ordersOffset,
                new Vector2(1f, 1f), new Vector2(-80f, -70f)));

            _nameText = MakeText(_overlayRoot.transform, "", 34, TextAnchor.MiddleCenter);
            Anchor(_nameText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 150f),
                new Vector2(1000f, 46f), new Vector2(0.5f, 0f));

            _descText = MakeText(_overlayRoot.transform, "", 26, TextAnchor.UpperCenter);
            _descText.supportRichText = true;
            Anchor(_descText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 70f),
                new Vector2(1000f, 80f), new Vector2(0.5f, 0f));

            var hint = MakeText(_overlayRoot.transform,
                "LMB pick up / place     MMB split     R rotate     RMB use     TAB close", 24, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.55f);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 24f),
                new Vector2(1400f, 40f), new Vector2(0.5f, 0f));
        }

        /// <summary>
        /// A panel of pixelSize pixels. With an anchor it is a world canvas on the lid, worldWidth wide; without one it
        /// is a rectangle in a corner of the screen overlay.
        /// </summary>
        private RectTransform MakePanel(string panelName, Transform anchor, Vector2 pixelSize, float worldWidth,
            Vector3 euler, Vector3 offset, Vector2 overlayCorner, Vector2 overlayOffset)
        {
            if (!anchor)
            {
                var go = new GameObject(panelName, typeof(RectTransform));
                go.transform.SetParent(_overlayRoot.transform, false);
                var rect = go.GetComponent<RectTransform>();
                Anchor(rect, overlayCorner, overlayOffset, pixelSize, overlayCorner);
                return rect;
            }

            var worldGo = new GameObject(panelName, typeof(Canvas));
            worldGo.transform.SetParent(anchor, false);

            var worldCanvas = worldGo.GetComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;
            worldCanvas.worldCamera = rig ? rig.Camera : null;

            var worldRect = worldGo.GetComponent<RectTransform>();
            worldRect.pivot = new Vector2(0.5f, 0.5f);
            worldRect.sizeDelta = pixelSize;
            worldRect.localPosition = offset;
            worldRect.localRotation = Quaternion.Euler(euler);
            worldRect.localScale = Vector3.one * (worldWidth / pixelSize.x);

            _worldPanels.Add(worldGo);
            return worldRect;
        }

        private void BuildHealthPanel(RectTransform panel)
        {
            var frame = new GameObject("HealthBar", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(panel, false);
            var frameImage = frame.GetComponent<Image>();
            frameImage.color = new Color(0.35f, 0.4f, 0.35f, 1f);
            frameImage.raycastTarget = false;
            Anchor(frameImage.rectTransform, new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(HealthPanelSize.x, BarHeight), new Vector2(0.5f, 1f));

            var back = new GameObject("Back", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(frame.transform, false);
            var backImage = back.GetComponent<Image>();
            backImage.color = new Color(0.03f, 0.05f, 0.03f, 1f);
            backImage.raycastTarget = false;
            Stretch(backImage.rectTransform, BarBorder);

            // The mask is as wide as the filled part. Its child keeps full width, so it is clipped, not squashed.
            var innerWidth = HealthPanelSize.x - BarBorder * 2f;
            var maskGo = new GameObject("Mask", typeof(RectTransform), typeof(RectMask2D));
            maskGo.transform.SetParent(back.transform, false);
            _hpMask = maskGo.GetComponent<RectTransform>();
            _hpMask.anchorMin = new Vector2(0f, 0f);
            _hpMask.anchorMax = new Vector2(0f, 1f);
            _hpMask.pivot = new Vector2(0f, 0.5f);
            _hpMask.anchoredPosition = Vector2.zero;
            _hpMask.sizeDelta = new Vector2(innerWidth, 0f);

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
            fillRect.sizeDelta = new Vector2(innerWidth, 0f);

            _hpText = MakeText(frame.transform, "", 24, TextAnchor.MiddleLeft);
            Stretch(_hpText.rectTransform, 12f);
            _hpText.color = new Color(1f, 1f, 1f, 0.9f);

            _xpText = MakeText(panel, "XP  0", 34, TextAnchor.MiddleLeft);
            Anchor(_xpText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -BarHeight - 8f),
                new Vector2(HealthPanelSize.x, 44f), new Vector2(0.5f, 1f));

            FinishWorldPanel(panel);
        }

        private void BuildOrdersPanel(RectTransform panel)
        {
            // A sheet of paper pinned to the lid.
            var paper = new GameObject("Paper", typeof(RectTransform), typeof(Image));
            paper.transform.SetParent(panel, false);
            var paperImage = paper.GetComponent<Image>();
            paperImage.color = new Color(0.82f, 0.78f, 0.64f, 0.95f);
            paperImage.raycastTarget = false;
            Stretch(paperImage.rectTransform);

            var ink = new Color(0.12f, 0.1f, 0.08f, 1f);

            _ordersTitleText = MakeText(paper.transform, ordersTitle, 36, TextAnchor.MiddleCenter);
            _ordersTitleText.color = ink;
            Anchor(_ordersTitleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -16f),
                new Vector2(OrdersPanelSize.x - 40f, 48f), new Vector2(0.5f, 1f));

            _ordersBodyText = MakeText(paper.transform, ordersBody, 30, TextAnchor.UpperLeft);
            _ordersBodyText.color = ink;
            Anchor(_ordersBodyText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -76f),
                new Vector2(OrdersPanelSize.x - 50f, OrdersPanelSize.y - 100f), new Vector2(0.5f, 1f));

            FinishWorldPanel(panel);
        }

        /// <summary>A world panel is built after the rig set its layers, so move the new objects onto the inventory layer.</summary>
        private void FinishWorldPanel(RectTransform panel)
        {
            if (rig && panel.GetComponent<Canvas>() != null)
            {
                SuitcaseRig.SetLayerRecursive(panel.gameObject, rig.Layer);
            }
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

        // ---------- editor help ----------

        private void OnDrawGizmos()
        {
            DrawAnchorGizmo(healthAnchor, healthWidth, HealthPanelSize);
            DrawAnchorGizmo(ordersAnchor, ordersWidth, OrdersPanelSize);
        }

        /// <summary>A rectangle the size of the panel, plus a blue line along +Z (which must point into the lid).</summary>
        private static void DrawAnchorGizmo(Transform anchor, float worldWidth, Vector2 pixelSize)
        {
            if (!anchor) return;

            var height = worldWidth * pixelSize.y / pixelSize.x;
            Gizmos.matrix = Matrix4x4.TRS(anchor.position, anchor.rotation, Vector3.one);

            Gizmos.color = new Color(1f, 1f, 1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(worldWidth, height, 0.001f));

            Gizmos.color = new Color(0.3f, 0.5f, 1f, 0.9f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, 0f, worldWidth * 0.3f));
        }
    }
}