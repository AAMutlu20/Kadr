using System.Collections;
using System.Collections.Generic;
using System.Text;
using Items;
using Menus;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Photography
{
    /// <summary>
    /// The photo album window. Opened by clicking the photos on the lid (an InventoryHotspot), it covers the screen
    /// while the suitcase stays paused behind it.
    ///
    ///   left:   the paid 12 (pinned first, then the best by score), empty slots as dim outlines
    ///   middle: the extra photos, marked "not paid"
    ///   right:  the photo under the mouse (or the selected one) with its score breakdown, Pin and Discard
    ///
    /// Hovering lifts and straightens a photo. Click selects. Pin keeps a photo in the paid 12. Discard asks twice.
    /// Esc or right click closes the window and returns to the suitcase; Tab closes both.
    ///
    /// It does not use Unity's EventSystem: the mouse is read directly, like the rest of the inventory, so it works
    /// with the new Input System and needs nothing in the scene except this component.
    /// </summary>
    public class PhotoAlbumOverlay : MonoBehaviour
    {
        private const int Cols = 4;
        private const int PerGrid = 12;
        private const float CellW = 160f;
        private const float CellH = 195f;
        private const float Gap = 10f;
        private const float DetailWindow = 285f;
        private const float ScrollStep = 45f;

        [Header("References")]
        [SerializeField] private CameraController photoCamera;
        [SerializeField] private Inventory inventory;
        [SerializeField] private InventoryView3D inventoryView;
        [Tooltip("The clickable area over the photos on the lid (a BoxCollider + InventoryHotspot).")]
        [SerializeField] private InventoryHotspot photosHotspot;

        [Header("Look")]
        [SerializeField] private string currencySuffix = " руб.";

        private sealed class Slot
        {
            public RectTransform Cell;     // fixed rectangle, used for the mouse test
            public RectTransform Visual;   // the part that lifts and tilts
            public Image Frame;
            public RawImage Picture;
            public Text Caption;
            public GameObject PinMark;
            public GameObject Stamp;
            public bool Extra;
            public PhotoData Photo;
            public float Hover;            // 0..1
            public float Tilt;
        }

        private sealed class PanelButton
        {
            public RectTransform Rect;
            public Image Background;
            public Text Label;
            public bool Enabled;
        }

        private readonly List<Slot> _slots = new();
        private PhotoAlbum _album;
        private GameObject _root;
        private Font _font;
        private Text _payoutText;
        private RawImage _bigPicture;
        private Text _detailText;
        private RectTransform _detailRect;   // the text, taller than its window when a photo has many subjects
        private float _scroll;
        private float _scrollMax;
        private PanelButton _pin;
        private PanelButton _discard;

        private bool _open;
        private Slot _hovered;
        private PhotoData _selected;
        private PhotoData _shownDetail;
        private bool _detailDirty = true;
        private bool _discardArmed;

        private void Start()
        {
            if (!photoCamera || !inventory || !inventoryView)
            {
                Debug.LogError("[PhotoAlbumOverlay] Photo Camera, Inventory or Inventory View is not assigned.", this);
                enabled = false;
                return;
            }

            _album = photoCamera.Album;
            _font = KadrFont.Body;
            Build();

            _album.Changed += OnAlbumChanged;
            if (photosHotspot) photosHotspot.Clicked += Open;
        }

        private void OnDestroy()
        {
            if (_album != null) _album.Changed -= OnAlbumChanged;
            if (photosHotspot) photosHotspot.Clicked -= Open;
        }

        private void OnDisable()
        {
            if (_open) CloseNow();
        }

        // ---------- open and close ----------

        public void Open()
        {
            if (_open || !inventory.IsOpen) return;

            _open = true;
            inventoryView.Blocked = true;
            _root.SetActive(true);
            _detailDirty = true;
            Refresh();
        }

        /// <summary>Back to the suitcase. The suitcase starts listening again a frame later, so the same Esc does not close it too.</summary>
        private void Close()
        {
            if (!_open) return;

            _open = false;
            _root.SetActive(false);
            StartCoroutine(UnblockNextFrame());
        }

        private IEnumerator UnblockNextFrame()
        {
            yield return null;
            inventoryView.Blocked = false;
        }

        /// <summary>The whole inventory closed (Tab): no waiting.</summary>
        private void CloseNow()
        {
            _open = false;
            if (_root) _root.SetActive(false);
            if (inventoryView) inventoryView.Blocked = false;
        }

        // ---------- every frame ----------

        private void Update()
        {
            if (!_open) return;

            if (!inventory.IsOpen)
            {
                CloseNow();
                return;
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            {
                Close();
                return;
            }

            var pointer = mouse.position.ReadValue();

            // Mouse wheel scrolls the details of the shown photo.
            var wheel = mouse.scroll.ReadValue().y;
            if (wheel != 0f && _scrollMax > 0f)
            {
                _scroll = Mathf.Clamp(_scroll - Mathf.Sign(wheel) * ScrollStep, 0f, _scrollMax);
                _detailRect.anchoredPosition = new Vector2(0f, _scroll);
            }

            // Which photo is under the mouse?
            Slot over = null;
            foreach (var slot in _slots)
            {
                if (slot.Photo != null && Contains(slot.Cell, pointer)) { over = slot; break; }
            }

            if (over != _hovered)
            {
                _hovered = over;
                if (over != null) over.Cell.SetAsLastSibling(); // the lifted photo draws over its neighbours
                _detailDirty = true;
            }

            AnimateSlots();
            UpdateButtons(pointer);

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (_pin.Enabled && Contains(_pin.Rect, pointer)) TogglePin();
                else if (_discard.Enabled && Contains(_discard.Rect, pointer)) Discard();
                else if (_hovered != null) Select(_hovered.Photo);
            }

            var target = _hovered != null ? _hovered.Photo : _selected;
            if (_detailDirty || target != _shownDetail) ShowDetail(target);
        }

        private void AnimateSlots()
        {
            var step = Time.unscaledDeltaTime * 9f;

            foreach (var slot in _slots)
            {
                slot.Hover = Mathf.MoveTowards(slot.Hover, slot == _hovered ? 1f : 0f, step);
                var t = Mathf.SmoothStep(0f, 1f, slot.Hover);
                slot.Visual.localScale = Vector3.one * Mathf.Lerp(1f, 1.12f, t);
                slot.Visual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(slot.Tilt, 0f, t));
            }
        }

        private void UpdateButtons(Vector2 pointer)
        {
            var photo = _selected;

            _pin.Enabled = photo != null && (photo.Pinned || _album.PinnedCount < PhotoAlbum.PaidSlots);
            _pin.Label.text = photo == null ? "Pin"
                : photo.Pinned ? "Unpin"
                : _album.PinnedCount >= PhotoAlbum.PaidSlots ? "12 pinned" : "Pin";

            _discard.Enabled = photo != null;
            _discard.Label.text = _discardArmed ? "Sure? Click again" : "Discard";

            Tint(_pin, pointer, new Color(0.25f, 0.45f, 0.3f));
            Tint(_discard, pointer, _discardArmed ? new Color(0.7f, 0.2f, 0.2f) : new Color(0.45f, 0.25f, 0.25f));
        }

        private static void Tint(PanelButton button, Vector2 pointer, Color color)
        {
            var hot = button.Enabled && Contains(button.Rect, pointer);
            button.Background.color = !button.Enabled ? new Color(0.25f, 0.25f, 0.25f, 0.6f)
                : hot ? Color.Lerp(color, Color.white, 0.25f) : color;
            button.Label.color = button.Enabled ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        }

        // ---------- actions ----------

        private void Select(PhotoData photo)
        {
            _selected = photo;
            _discardArmed = false;
            _detailDirty = true;
        }

        private void TogglePin()
        {
            if (_selected == null) return;

            if (_selected.Pinned) _album.Unpin(_selected);
            else _album.Pin(_selected);
        }

        private void Discard()
        {
            if (_selected == null) return;

            if (!_discardArmed)
            {
                _discardArmed = true; // the first click only asks
                return;
            }

            var photo = _selected;
            _selected = null;
            _discardArmed = false;
            _album.Discard(photo);
        }

        private void OnAlbumChanged()
        {
            if (_open) Refresh();
        }

        // ---------- showing the album ----------

        private void Refresh()
        {
            var paid = _album.GetPaid();

            var extras = new List<PhotoData>();
            foreach (var photo in _album.Photos)
            {
                if (!paid.Contains(photo)) extras.Add(photo);
            }

            extras.Sort((a, b) => b.Score.CompareTo(a.Score));

            for (var i = 0; i < PerGrid; i++)
            {
                SetSlot(_slots[i], i < paid.Count ? paid[i] : null);
                SetSlot(_slots[PerGrid + i], i < extras.Count ? extras[i] : null);
            }

            if (_selected != null && !_album.Contains(_selected)) Select(null);
            if (_hovered != null && _hovered.Photo == null) _hovered = null;

            var total = Mathf.RoundToInt(_album.EstimatedPayout());
            _payoutText.text = $"Estimated payout:  <b>{total}{currencySuffix}</b>      paid {paid.Count} / {PhotoAlbum.PaidSlots}      pinned {_album.PinnedCount}";
            _detailDirty = true;
        }

        private void SetSlot(Slot slot, PhotoData photo)
        {
            slot.Photo = photo;
            var has = photo != null;

            slot.Frame.color = has ? Color.white : new Color(1f, 1f, 1f, 0.07f);
            slot.Picture.gameObject.SetActive(has);
            slot.Picture.texture = has ? photo.Image : null;
            slot.Caption.text = has ? Mathf.RoundToInt(photo.Score) + currencySuffix : "";
            slot.PinMark.SetActive(has && photo.Pinned);
            slot.Stamp.SetActive(has && slot.Extra);
            slot.Tilt = has ? (photo.Number * 37 % 7 - 3) * 1.1f : 0f;
        }

        private void ShowDetail(PhotoData photo)
        {
            _shownDetail = photo;
            _detailDirty = false;

            if (photo == null)
            {
                _bigPicture.gameObject.SetActive(false);
                SetDetailText("<color=#999999>Click a photo to look at it.\nPinned photos always count towards the paid 12.</color>");
                return;
            }

            _bigPicture.gameObject.SetActive(true);
            _bigPicture.texture = photo.Image;

            var text = new StringBuilder();
            foreach (var s in photo.Subjects)
            {
                text.Append("<b>").Append(s.Name).Append("</b>   ").Append(Mathf.RoundToInt(s.Score)).Append(currencySuffix).Append('\n');
                text.Append("<size=20><color=#bbbbbb>rarity ").Append(s.Rarity.ToString("0.#"))
                    .Append("  danger ").Append(s.Danger.ToString("0.#"))
                    .Append("  in frame ").Append(Mathf.RoundToInt(s.Coverage * 100f)).Append('%')
                    .Append("  framing ").Append(s.Framing.ToString("0.##"));
                if (s.State > 1f) text.Append("  attacking x").Append(s.State.ToString("0.##"));
                if (s.Special > 1f) text.Append("  bonus x").Append(s.Special.ToString("0.##"));
                if (s.Repeat < 1f) text.Append("  seen before x").Append(s.Repeat.ToString("0.##"));
                text.Append("</color></size>\n");
            }

            text.Append("\n<b>Photo ").Append(photo.Number).Append(":  ").Append(Mathf.RoundToInt(photo.Score)).Append(currencySuffix).Append("</b>\n");
            text.Append(photo.Pinned ? "<color=#e0c060>Pinned</color>"
                : _album.GetPaid().Contains(photo) ? "<color=#80d090>In the paid 12</color>"
                : "<color=#e07070>Not paid</color>");

            SetDetailText(text.ToString());
        }

        /// <summary>Sets the text, makes its box as tall as the text needs, and scrolls back to the top.</summary>
        private void SetDetailText(string content)
        {
            _detailText.text = content;

            var height = Mathf.Max(DetailWindow, _detailText.preferredHeight + 8f);
            _detailRect.sizeDelta = new Vector2(310f, height);
            _scrollMax = height - DetailWindow;
            _scroll = 0f;
            _detailRect.anchoredPosition = Vector2.zero;
        }

        // ---------- building the window ----------

        private void Build()
        {
            var canvasGo = new GameObject("PhotoAlbumCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(canvasGo.transform, false);
            Stretch(_root.GetComponent<RectTransform>());

            // Dimmed backdrop over the suitcase.
            var backdrop = MakeImage(_root.transform, "Backdrop", new Color(0f, 0f, 0f, 0.8f));
            Stretch(backdrop.rectTransform);

            var panel = MakeImage(_root.transform, "Panel", new Color(0.13f, 0.12f, 0.11f, 1f));
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel.rectTransform.sizeDelta = new Vector2(1800f, 860f);
            panel.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.15f);

            var p = panel.transform;

            MakeText(p, "Title", "PHOTOS", 44, TextAnchor.MiddleLeft, Color.white, 40f, 18f, 600f, 60f);
            _payoutText = MakeText(p, "Payout", "", 28, TextAnchor.MiddleRight, new Color(0.85f, 0.85f, 0.8f), 700f, 22f, 1060f, 50f);

            MakeText(p, "PaidLabel", "PAID  (the best 12)", 24, TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 0.8f), 40f, 92f, 600f, 34f);
            MakeText(p, "ExtraLabel", "EXTRA  (not paid)", 24, TextAnchor.MiddleLeft, new Color(0.9f, 0.7f, 0.7f), 740f, 92f, 600f, 34f);

            const float gridTop = 135f;
            for (var i = 0; i < PerGrid; i++) _slots.Add(MakeSlot(p, 40f, gridTop, i, false));
            for (var i = 0; i < PerGrid; i++) _slots.Add(MakeSlot(p, 740f, gridTop, i, true));

            // The detail column.
            var detail = MakeImage(p, "Detail", new Color(0.18f, 0.17f, 0.16f, 1f));
            SetTopLeft(detail.rectTransform, 1440f, 92f, 330f, 700f);

            _bigPicture = MakeRaw(detail.transform, "BigPicture", 10f, 10f, 310f, 310f);
            // The text sits in a window that clips it. The mouse wheel scrolls it, there is no visible bar.
            var scrollGo = new GameObject("DetailScroll", typeof(RectTransform), typeof(RectMask2D));
            scrollGo.transform.SetParent(detail.transform, false);
            SetTopLeft(scrollGo.GetComponent<RectTransform>(), 10f, 330f, 310f, DetailWindow);
            _detailText = MakeText(scrollGo.transform, "DetailText", "", 26, TextAnchor.UpperLeft, Color.white, 0f, 0f, 310f, DetailWindow);
            _detailText.supportRichText = true;
            _detailRect = _detailText.rectTransform;

            _pin = MakeButton(detail.transform, "Pin", 10f, 630f, 150f, 52f);
            _discard = MakeButton(detail.transform, "Discard", 170f, 630f, 150f, 52f);

            MakeText(p, "Hint", "Click a photo to look at it     Esc or right click: back to the suitcase     Tab: close everything",
                22, TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.7f), 40f, 802f, 1720f, 40f);

            _root.SetActive(false);
        }

        private Slot MakeSlot(Transform parent, float originX, float originY, int index, bool extra)
        {
            var col = index % Cols;
            var row = index / Cols;
            var x = originX + col * (CellW + Gap);
            var y = originY + row * (CellH + Gap);

            var slot = new Slot { Extra = extra };

            var cell = new GameObject("Slot", typeof(RectTransform));
            cell.transform.SetParent(parent, false);
            slot.Cell = cell.GetComponent<RectTransform>();
            SetTopLeft(slot.Cell, x, y, CellW, CellH);

            var visual = new GameObject("Visual", typeof(RectTransform));
            visual.transform.SetParent(cell.transform, false);
            slot.Visual = visual.GetComponent<RectTransform>();
            slot.Visual.anchorMin = slot.Visual.anchorMax = new Vector2(0.5f, 0.5f);
            slot.Visual.pivot = new Vector2(0.5f, 0.5f);
            slot.Visual.sizeDelta = new Vector2(CellW, CellH);
            slot.Visual.anchoredPosition = Vector2.zero;

            slot.Frame = MakeImage(visual.transform, "Frame", new Color(1f, 1f, 1f, 0.07f));
            Stretch(slot.Frame.rectTransform);
            slot.Frame.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.25f);

            slot.Picture = MakeRaw(visual.transform, "Picture", 10f, 10f, 140f, 140f);
            slot.Caption = MakeText(visual.transform, "Caption", "", 22, TextAnchor.MiddleCenter, new Color(0.15f, 0.12f, 0.1f), 0f, 152f, CellW, 38f);

            var pin = MakeImage(visual.transform, "PinMark", new Color(0.8f, 0.15f, 0.15f, 1f));
            SetTopLeft(pin.rectTransform, CellW - 34f, 2f, 30f, 30f);
            MakeText(pin.transform, "P", "P", 20, TextAnchor.MiddleCenter, Color.white, 0f, 0f, 30f, 30f);
            slot.PinMark = pin.gameObject;

            var stamp = MakeText(visual.transform, "Stamp", "NOT PAID", 30, TextAnchor.MiddleCenter,
                new Color(0.85f, 0.2f, 0.2f, 0.9f), 10f, 60f, 140f, 40f);
            stamp.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 18f);
            slot.Stamp = stamp.gameObject;

            return slot;
        }

        private PanelButton MakeButton(Transform parent, string label, float x, float y, float w, float h)
        {
            var background = MakeImage(parent, label + "PanelButton", new Color(0.3f, 0.3f, 0.3f));
            SetTopLeft(background.rectTransform, x, y, w, h);

            var text = MakeText(background.transform, "Label", label, 24, TextAnchor.MiddleCenter, Color.white, 0f, 0f, w, h);
            return new PanelButton { Rect = background.rectTransform, Background = background, Label = text };
        }

        private Text MakeText(Transform parent, string objectName, string content, int size, TextAnchor alignment,
            Color color, float x, float y, float w, float h)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            SetTopLeft(go.GetComponent<RectTransform>(), x, y, w, h);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.text = content;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Image MakeImage(Transform parent, string objectName, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RawImage MakeRaw(Transform parent, string objectName, float x, float y, float w, float h)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            SetTopLeft(go.GetComponent<RectTransform>(), x, y, w, h);

            var raw = go.GetComponent<RawImage>();
            raw.raycastTarget = false;
            return raw;
        }

        /// <summary>Places a rectangle by its top left corner, measured from the top left of its parent.</summary>
        private static void SetTopLeft(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static bool Contains(RectTransform rect, Vector2 screenPoint)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);
        }
    }
}
