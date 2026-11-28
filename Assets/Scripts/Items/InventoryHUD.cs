using UnityEngine.SceneManagement;
using Hud;
using System.Text;
using System.Collections.Generic;
using Core;
using Events;
using Gameplay;
using Photography;
using Menus;
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
        private static readonly Color XpGold = new(0.95f, 0.72f, 0.2f);
        private const float BarHeight = 44f;
        private const float XpBarHeight = 26f;
        private const float BarBorder = 3f;

        private readonly List<GameObject> _worldPanels = new();
        private GameObject _overlayRoot;

        private RectTransform _hpMask;
        private RectTransform _hpPreview; // mask of the grey bar: where health will end up once the healing over time is done
        private RawImage _hpPreviewFill;
        private HealOverTime _healer;
        private float _previewShown;
        private RawImage _hpFill;
        private Text _hpText;
        private Texture2D _hpTexture;
        private Text _xpText;     // "POINTS 2"
        private Text _xpBarText;  // "120 / 400"
        private RectTransform _xpMask;
        private RectTransform _xpEdge;
        private Image _xpEdgeImage;
        private RawImage _xpFill;
        private Texture2D _xpTexture;
        private int _xpProgress;
        private int _xpNeeded = 100;
        private int _xpPoints;
        private float _xpShown;        // 0..1, what the bar shows
        private bool _xpFilling;       // a point was earned: fill to the top first, flash, then restart
        private float _xpFlash;
        private float _xpScroll;
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

        // The real orders: filled from the first task and from ObjectiveEvents, in the order they appeared.
        private readonly List<string> _objectiveOrder = new();
        private readonly Dictionary<string, (string text, bool done)> _objectives = new();

        private void Start()
        {
            _font = KadrFont.Body;

            _xpProgress = PlayerXp.Progress;
            _xpNeeded = Mathf.Max(1, PlayerXp.Needed);
            _xpPoints = PlayerXp.Points;
            _xpShown = Mathf.Clamp01(_xpProgress / (float)_xpNeeded);

            FindHealth();
            ReadHealth();
            _hpShown = _hpMax > 0f ? _hpCurrent / _hpMax : 0f;

            Build();

            if (GameServices.TryGet(out _bus))
            {
                _bus.Subscribe<XpChangedEvent>(OnXpChanged);
                _bus.Subscribe<ObjectiveEvent>(OnObjective);
                _bus.Subscribe<RunRestartedEvent>(OnRunRestarted);
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            ResetObjectives();

            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.Unsubscribe<XpChangedEvent>(OnXpChanged);
                _bus.Unsubscribe<ObjectiveEvent>(OnObjective);
                _bus.Unsubscribe<RunRestartedEvent>(OnRunRestarted);
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (_hpTexture) Destroy(_hpTexture);
            if (_xpTexture) Destroy(_xpTexture);
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;

            if (_overlayRoot) _overlayRoot.SetActive(visible);
            foreach (var panel in _worldPanels)
            {
                if (panel) panel.SetActive(visible);
            }

            if (visible)
            {
                _xpFilling = false;
                _xpShown = Mathf.Clamp01(_xpProgress / (float)_xpNeeded); // no animation from a stale value
                ReadHealth();
            }
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

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ResetObjectives();

        private void OnRunRestarted(RunRestartedEvent e) => ResetObjectives();

        // The first task comes from the HUD's start text (it never goes over the bus), the rest from ObjectiveEvents.
        private void ResetObjectives()
        {
            _objectiveOrder.Clear();
            _objectives.Clear();

            var kadr = FindAnyObjectByType<KadrHud>();
            if (kadr && !string.IsNullOrEmpty(kadr.StartText)) OnObjective(new ObjectiveEvent(kadr.StartTitle, kadr.StartText));
            else RefreshOrders();
        }

        private void OnObjective(ObjectiveEvent e)
        {
            if (string.IsNullOrEmpty(e.Title)) return;

            if (!_objectives.ContainsKey(e.Title)) _objectiveOrder.Add(e.Title);
            _objectives[e.Title] = (e.Text, e.Completed);
            RefreshOrders();
        }

        private void RefreshOrders()
        {
            if (!_ordersBodyText) return;

            var sb = new StringBuilder();
            foreach (var title in _objectiveOrder)
            {
                var (text, done) = _objectives[title];
                sb.Append(done ? "<color=#7d766a>[x] " : "[ ] ").Append(text).Append(done ? "</color>" : "").Append('\n');
            }

            _ordersBodyText.text = sb.Length > 0 ? sb.ToString().TrimEnd() : ordersBody;
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

            if (!_healer || _healer.gameObject != playerHealth.gameObject) _healer = playerHealth.GetComponent<HealOverTime>();
            _hpCurrent = playerHealth.Current;
            _hpMax = playerHealth.Max;
        }

        private void OnXpChanged(XpChangedEvent e)
        {
            _xpProgress = e.Progress;
            _xpNeeded = Mathf.Max(1, e.Needed);
            _xpPoints = e.Points;

            // Earning a point: the bar fills to the top, flashes, and starts again from what is left over.
            if (e.PointEarned) _xpFilling = true;
        }

        private float XpTarget => _xpFilling ? 1f : Mathf.Clamp01(_xpProgress / (float)_xpNeeded);

        /// <summary>
        /// The XP bar looks different from the health bar on purpose: gold diagonal stripes that slide the other way,
        /// a bright pulsing tip at the edge of the fill, and a white flash when a point is earned.
        /// </summary>
        private void UpdateXpBar()
        {
            if (!_xpFill) return;

            var dt = Time.unscaledDeltaTime;
            _xpShown = Mathf.MoveTowards(_xpShown, XpTarget, dt * (_xpFilling ? 0.9f : 0.5f));

            if (_xpFilling && _xpShown >= 0.999f)
            {
                _xpFilling = false;
                _xpShown = 0f;
                _xpFlash = 1f;
            }

            _xpFlash = Mathf.MoveTowards(_xpFlash, 0f, dt * 1.6f);

            var inner = HealthPanelSize.x - BarBorder * 2f;
            _xpMask.sizeDelta = new Vector2(inner * _xpShown, 0f);

            _xpScroll = Mathf.Repeat(_xpScroll - dt * 0.3f, 1f);
            _xpFill.uvRect = new Rect(_xpScroll, 0f, 4f, 1f);

            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            var colour = Color.Lerp(XpGold, Color.white, _xpFlash);
            colour.a = 1f;
            _xpFill.color = colour;

            _xpEdge.anchoredPosition = new Vector2(inner * _xpShown, 0f);
            _xpEdgeImage.color = new Color(1f, 0.95f, 0.7f, _xpShown > 0.01f ? Mathf.Lerp(0.35f, 1f, pulse) : 0f);

            _xpBarText.text = _xpFilling ? "" : $"{_xpProgress} / {_xpNeeded}";
            _xpText.text = $"POINTS  <b>{_xpPoints}</b>";
        }

        private void Update()
        {
            if (!_visible || !_hpFill) return;

            UpdateXpBar();
            ReadHealth();
            var target = _hpMax > 0f ? Mathf.Clamp01(_hpCurrent / _hpMax) : 0f;
            _hpShown = Mathf.MoveTowards(_hpShown, target, Time.unscaledDeltaTime * 0.6f);

            // The mask clips the fill instead of squashing it, so the texture does not stretch as health drops.
            var inner = HealthPanelSize.x - BarBorder * 2f;
            _hpMask.sizeDelta = new Vector2(inner * _hpShown, 0f);

            // Grey preview: current health plus everything still to be healed. The real bar fills it up.
            var pending = _healer ? _healer.Pending : 0f;
            var previewTarget = _hpMax > 0f ? Mathf.Clamp01((_hpCurrent + pending) / _hpMax) : 0f;
            _previewShown = Mathf.MoveTowards(_previewShown, Mathf.Max(previewTarget, _hpShown), Time.unscaledDeltaTime * 0.6f);
            _hpPreview.sizeDelta = new Vector2(inner * _previewShown, 0f);

            var low = 1f - target;
            _hpScroll = Mathf.Repeat(_hpScroll + Time.unscaledDeltaTime * Mathf.Lerp(0.12f, 0.45f, low), 1f);
            _hpFill.uvRect = new Rect(_hpScroll, 0f, 1f, 1f);
            _hpPreviewFill.uvRect = new Rect(_hpScroll, 0f, 1f, 1f);

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
            _hpText.text = "HP  " + Mathf.CeilToInt(_hpCurrent) + (pending >= 0.5f ? "  +" + Mathf.CeilToInt(pending) : "");
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

            // Grey preview bar, drawn under the real fill. Same masked, scrolling texture as the real bar, in grey.
            var innerWidth = HealthPanelSize.x - BarBorder * 2f;
            _hpTexture = MakeBarTexture();
            var previewMaskGo = new GameObject("HealPreviewMask", typeof(RectTransform), typeof(RectMask2D));
            previewMaskGo.transform.SetParent(back.transform, false);
            _hpPreview = previewMaskGo.GetComponent<RectTransform>();
            _hpPreview.anchorMin = new Vector2(0f, 0f);
            _hpPreview.anchorMax = new Vector2(0f, 1f);
            _hpPreview.pivot = new Vector2(0f, 0.5f);
            _hpPreview.anchoredPosition = Vector2.zero;
            _hpPreview.sizeDelta = Vector2.zero;

            var previewFillGo = new GameObject("Fill", typeof(RectTransform), typeof(RawImage));
            previewFillGo.transform.SetParent(previewMaskGo.transform, false);
            _hpPreviewFill = previewFillGo.GetComponent<RawImage>();
            _hpPreviewFill.texture = _hpTexture;
            _hpPreviewFill.color = new Color(0.6f, 0.62f, 0.6f, 1f);
            _hpPreviewFill.raycastTarget = false;
            var previewFillRect = _hpPreviewFill.rectTransform;
            previewFillRect.anchorMin = new Vector2(0f, 0f);
            previewFillRect.anchorMax = new Vector2(0f, 1f);
            previewFillRect.pivot = new Vector2(0f, 0.5f);
            previewFillRect.anchoredPosition = Vector2.zero;
            previewFillRect.sizeDelta = new Vector2(innerWidth, 0f);

            // The mask is as wide as the filled part. Its child keeps full width, so it is clipped, not squashed.
            var maskGo = new GameObject("Mask", typeof(RectTransform), typeof(RectMask2D));
            maskGo.transform.SetParent(back.transform, false);
            _hpMask = maskGo.GetComponent<RectTransform>();
            _hpMask.anchorMin = new Vector2(0f, 0f);
            _hpMask.anchorMax = new Vector2(0f, 1f);
            _hpMask.pivot = new Vector2(0f, 0.5f);
            _hpMask.anchoredPosition = Vector2.zero;
            _hpMask.sizeDelta = new Vector2(innerWidth, 0f);

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
            _hpText.fontStyle = FontStyle.Bold;
            Outlined(_hpText);
            Stretch(_hpText.rectTransform, 12f);
            _hpText.color = new Color(1f, 1f, 1f, 0.9f);

            // XP: a second, slimmer bar under the health bar, then the points.
            var xpFrameGo = new GameObject("XpBar", typeof(RectTransform), typeof(Image));
            xpFrameGo.transform.SetParent(panel, false);
            var xpFrame = xpFrameGo.GetComponent<Image>();
            xpFrame.color = new Color(0.42f, 0.34f, 0.15f, 1f);
            xpFrame.raycastTarget = false;
            Anchor(xpFrame.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -BarHeight - 10f),
                new Vector2(HealthPanelSize.x, XpBarHeight), new Vector2(0.5f, 1f));

            var xpBack = new GameObject("Back", typeof(RectTransform), typeof(Image));
            xpBack.transform.SetParent(xpFrameGo.transform, false);
            var xpBackImage = xpBack.GetComponent<Image>();
            xpBackImage.color = new Color(0.05f, 0.04f, 0.02f, 1f);
            xpBackImage.raycastTarget = false;
            Stretch(xpBackImage.rectTransform, BarBorder);

            var xpMaskGo = new GameObject("Mask", typeof(RectTransform), typeof(RectMask2D));
            xpMaskGo.transform.SetParent(xpBack.transform, false);
            _xpMask = xpMaskGo.GetComponent<RectTransform>();
            _xpMask.anchorMin = new Vector2(0f, 0f);
            _xpMask.anchorMax = new Vector2(0f, 1f);
            _xpMask.pivot = new Vector2(0f, 0.5f);
            _xpMask.anchoredPosition = Vector2.zero;
            _xpMask.sizeDelta = new Vector2(0f, 0f);

            _xpTexture = MakeXpTexture();
            var xpFillGo = new GameObject("Fill", typeof(RectTransform), typeof(RawImage));
            xpFillGo.transform.SetParent(xpMaskGo.transform, false);
            _xpFill = xpFillGo.GetComponent<RawImage>();
            _xpFill.texture = _xpTexture;
            _xpFill.raycastTarget = false;
            var xpFillRect = _xpFill.rectTransform;
            xpFillRect.anchorMin = new Vector2(0f, 0f);
            xpFillRect.anchorMax = new Vector2(0f, 1f);
            xpFillRect.pivot = new Vector2(0f, 0.5f);
            xpFillRect.anchoredPosition = Vector2.zero;
            xpFillRect.sizeDelta = new Vector2(innerWidth, 0f);
            _xpFill.uvRect = new Rect(0f, 0f, 4f, 1f); // the stripes repeat along the bar

            // The bright tip at the end of the fill. Outside the mask so it can glow past the edge.
            var edgeGo = new GameObject("Tip", typeof(RectTransform), typeof(Image));
            edgeGo.transform.SetParent(xpBack.transform, false);
            _xpEdgeImage = edgeGo.GetComponent<Image>();
            _xpEdgeImage.raycastTarget = false;
            _xpEdge = _xpEdgeImage.rectTransform;
            _xpEdge.anchorMin = new Vector2(0f, 0f);
            _xpEdge.anchorMax = new Vector2(0f, 1f);
            _xpEdge.pivot = new Vector2(0.5f, 0.5f);
            _xpEdge.sizeDelta = new Vector2(6f, 0f);

            // Three texts inside the bar: XP on the left, the progress in the middle, the points on the far right.
            var xpLabel = MakeText(xpFrameGo.transform, "XP", 18, TextAnchor.MiddleLeft);
            Stretch(xpLabel.rectTransform, 10f);
            xpLabel.fontStyle = FontStyle.Bold;
            Outlined(xpLabel);

            _xpBarText = MakeText(xpFrameGo.transform, "", 17, TextAnchor.MiddleCenter);
            Stretch(_xpBarText.rectTransform);
            _xpBarText.color = new Color(1f, 1f, 1f, 0.95f);
            Outlined(_xpBarText);

            _xpText = MakeText(xpFrameGo.transform, "POINTS  0", 18, TextAnchor.MiddleRight);
            Stretch(_xpText.rectTransform, 10f);
            _xpText.supportRichText = true;
            Outlined(_xpText);

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
            _ordersBodyText.supportRichText = true;
            _ordersBodyText.resizeTextForBestFit = true;
            _ordersBodyText.resizeTextMinSize = 16;
            _ordersBodyText.resizeTextMaxSize = 30;
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

        /// <summary>Gold diagonal stripes that tile sideways. Tinted by the bar colour and scrolled through uvRect.</summary>
        private static Texture2D MakeXpTexture()
        {
            const int w = 64;
            const int h = 16;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            for (var x = 0; x < w; x++)
            {
                for (var y = 0; y < h; y++)
                {
                    // 16 pixels per stripe, so 64 divides evenly and the texture tiles without a seam.
                    var wave = 0.5f + 0.5f * Mathf.Sin((x + y) / 16f * Mathf.PI * 2f);
                    var v = Mathf.Lerp(0.68f, 1f, wave);
                    tex.SetPixel(x, y, new Color(v, v, v, 1f));
                }
            }

            tex.Apply();
            return tex;
        }

        /// <summary>A dark outline, so white text stays readable on top of a bright bar.</summary>
        private static void Outlined(Text text)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
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