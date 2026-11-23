using System.Collections.Generic;
using Gameplay;
using Menus;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Items
{
    /// <summary>
    /// The window of an opened ItemCrate. Creates itself the first time a crate is opened, so the scene needs nothing.
    ///
    ///   left:  the crate's grid        right: the suitcase's consumables grid
    ///   Click an item to move the whole stack to the other side. Shift + click moves one.
    ///   TAKE ALL moves everything that fits into the suitcase.
    ///   Esc, E, Tab or right click closes it.
    ///
    /// The game is paused while it is open. The suitcase screen is switched off meanwhile, so Tab cannot open it
    /// underneath. Like the other windows it reads the mouse directly, so no EventSystem is needed.
    /// </summary>
    public class CrateOverlay : MonoBehaviour
    {
        private const float PanelW = 1800f;
        private const float PanelH = 860f;
        private const float Margin = 40f;
        private const float MaxCell = 96f;
        private const float GridTop = 150f;
        private const float MessageSeconds = 2.2f;

        private sealed class StackView
        {
            public ItemStack Stack;
            public bool FromCrate;
            public RectTransform Rect;
            public Image Background;
            public Color BaseColor;
        }

        private static CrateOverlay _instance;

        private readonly List<StackView> _views = new();
        private ItemCrate _crate;
        private Inventory _inventory;
        private InventoryView3D _suitcase;
        private PlayerController _player;
        private Health _health;

        private GameObject _root;
        private Font _font;
        private RectTransform _crateLayer;
        private RectTransform _playerLayer;
        private Text _crateLabel;
        private Text _playerLabel;
        private Text _message;
        private RectTransform _takeAllRect;
        private Image _takeAllImage;
        private Text _takeAllLabel;

        private bool _open;
        private bool _dirty;
        private int _openedFrame;
        private float _savedTimeScale = 1f;
        private float _cell = MaxCell;
        private float _messageUntil;
        private StackView _hovered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>Opens the window for this crate. Called by the crate itself.</summary>
        public static void Show(ItemCrate crate, Inventory inventory)
        {
            if (!_instance) _instance = FindAnyObjectByType<CrateOverlay>();
            if (!_instance) _instance = new GameObject("CrateOverlay").AddComponent<CrateOverlay>();

            _instance.Open(crate, inventory);
        }

        private void Awake()
        {
            _font = KadrFont.Body;
            Build();
            _root.SetActive(false);
        }

        private void OnDisable()
        {
            if (_open) Close();
        }

        // ---------- open and close ----------

        private void Open(ItemCrate crate, Inventory inventory)
        {
            if (_open || !crate || !inventory || crate.Grid == null) return;
            if (Time.timeScale == 0f || inventory.IsOpen) return;

            _crate = crate;
            _inventory = inventory;
            _suitcase = FindAnyObjectByType<InventoryView3D>();
            _player = inventory.GetComponent<PlayerController>();
            _health = inventory.GetComponentInParent<Health>();

            _open = true;
            _openedFrame = Time.frameCount;
            _messageUntil = 0f;
            _message.text = "";

            // Freeze the world and the player, and keep Tab from opening the suitcase underneath.
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            if (_player) _player.InputEnabled = false;
            if (_suitcase) _suitcase.enabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _crate.Grid.Changed += MarkDirty;
            _inventory.Consumables.Changed += MarkDirty;

            _root.SetActive(true);
            Rebuild();
            _crate.OnOpened();
        }

        private void Close()
        {
            if (!_open) return;

            _open = false;
            _root.SetActive(false);

            if (_crate && _crate.Grid != null) _crate.Grid.Changed -= MarkDirty;
            if (_inventory) _inventory.Consumables.Changed -= MarkDirty;

            Time.timeScale = _savedTimeScale;
            if (_suitcase) _suitcase.enabled = true;

            // A dead player keeps input off, so closing must not switch anything back on.
            var dead = _health && _health.IsDead;
            if (_player && !dead) _player.InputEnabled = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            var crate = _crate;
            _crate = null;
            _hovered = null;
            if (crate) crate.OnClosed();
        }

        private void MarkDirty() => _dirty = true;

        // ---------- every frame ----------

        private void Update()
        {
            if (!_open) return;

            if (_health && _health.IsDead)
            {
                Close();
                return;
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // The E that opened the window is still "pressed this frame" on the first frame.
            if (Time.frameCount != _openedFrame &&
                (kb.escapeKey.wasPressedThisFrame || KadrControls.Pressed(GameAction.Interact) || KadrControls.Pressed(GameAction.Inventory) ||
                 mouse.rightButton.wasPressedThisFrame))
            {
                Close();
                return;
            }

            if (_dirty) Rebuild();

            var pointer = mouse.position.ReadValue();

            _hovered = null;
            foreach (var view in _views)
            {
                if (Contains(view.Rect, pointer)) { _hovered = view; break; }
            }

            foreach (var view in _views)
            {
                view.Background.color = view == _hovered ? Color.Lerp(view.BaseColor, Color.white, 0.25f) : view.BaseColor;
            }

            var overTakeAll = Contains(_takeAllRect, pointer);
            _takeAllImage.color = overTakeAll ? new Color(0.35f, 0.55f, 0.4f) : new Color(0.25f, 0.42f, 0.3f);

            if (_message.text.Length > 0 && Time.unscaledTime > _messageUntil) _message.text = "";

            if (!mouse.leftButton.wasPressedThisFrame || Time.frameCount == _openedFrame) return;

            if (overTakeAll) TakeAll();
            else if (_hovered != null) Move(_hovered, kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        }

        // ---------- moving items ----------

        private void Move(StackView view, bool onlyOne)
        {
            var def = view.Stack.Definition;
            var amount = onlyOne ? 1 : view.Stack.Count;

            if (view.FromCrate)
            {
                var left = _inventory.TryAdd(def, amount);
                var moved = amount - left;
                if (moved <= 0)
                {
                    Say("The suitcase is full");
                    return;
                }

                _crate.Grid.RemoveCount(view.Stack, moved);
            }
            else
            {
                if (!_crate.AllowStoring)
                {
                    Say("You cannot store anything here");
                    return;
                }

                var left = _crate.Grid.Add(def, amount);
                var moved = amount - left;
                if (moved <= 0)
                {
                    Say(_crate.DisplayName + " is full");
                    return;
                }

                _inventory.Consumables.RemoveCount(view.Stack, moved);
            }
        }

        private void TakeAll()
        {
            var stacks = new List<ItemStack>(_crate.Grid.Stacks);
            var leftBehind = false;

            foreach (var stack in stacks)
            {
                var left = _inventory.TryAdd(stack.Definition, stack.Count);
                var moved = stack.Count - left;
                if (moved > 0) _crate.Grid.RemoveCount(stack, moved);
                if (left > 0) leftBehind = true;
            }

            if (leftBehind) Say("The suitcase is full");
            else if (stacks.Count == 0) Say("Nothing left to take");
        }

        private void Say(string text)
        {
            _message.text = text;
            _messageUntil = Time.unscaledTime + MessageSeconds;
        }

        // ---------- drawing the two grids ----------

        private void Rebuild()
        {
            _dirty = false;
            _hovered = null;

            Clear(_crateLayer);
            Clear(_playerLayer);
            _views.Clear();

            var crateGrid = _crate.Grid;
            var playerGrid = _inventory.Consumables;

            _cell = Mathf.Min(MaxCell, (PanelW - Margin * 3f) / (crateGrid.Width + playerGrid.Width));

            var playerX = PanelW - Margin - playerGrid.Width * _cell;
            Place(_crateLayer, Margin, GridTop, crateGrid.Width * _cell, crateGrid.Height * _cell);
            Place(_playerLayer, playerX, GridTop, playerGrid.Width * _cell, playerGrid.Height * _cell);

            Place(_crateLabel.rectTransform, Margin, 92f, 700f, 44f);
            _crateLabel.text = _crate.DisplayName.ToUpperInvariant();

            Place(_playerLabel.rectTransform, playerX, 92f, playerGrid.Width * _cell, 44f);
            _playerLabel.text = _crate.AllowStoring ? "SUITCASE  (click to store)" : "SUITCASE  (storing not possible here)";

            BuildGrid(_crateLayer, crateGrid, true);
            BuildGrid(_playerLayer, playerGrid, false);
        }

        private void BuildGrid(RectTransform layer, InventoryGrid grid, bool fromCrate)
        {
            for (var y = 0; y < grid.Height; y++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var cell = MakeImage(layer, "Cell", new Color(0.07f, 0.07f, 0.07f, 1f));
                    Place(cell.rectTransform, x * _cell + 1.5f, y * _cell + 1.5f, _cell - 3f, _cell - 3f);
                }
            }

            foreach (var stack in grid.Stacks)
            {
                var def = stack.Definition;

                var hash = Mathf.Abs(def.Id.GetHashCode());
                var color = Color.HSVToRGB((hash % 100) / 100f, 0.4f, 0.5f);

                var image = MakeImage(layer, def.DisplayName, color);
                Place(image.rectTransform, stack.X * _cell + 1.5f, stack.Y * _cell + 1.5f,
                    stack.Width * _cell - 3f, stack.Height * _cell - 3f);

                if (def.Icon)
                {
                    var icon = MakeImage(image.transform, "Icon", Color.white);
                    icon.sprite = def.Icon;
                    icon.preserveAspect = true;
                    Stretch(icon.rectTransform, 6f);
                }

                var name = MakeText(image.transform, "Name", def.DisplayName, 22, TextAnchor.UpperCenter, Color.white);
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 10;
                name.resizeTextMaxSize = 22;
                name.horizontalOverflow = HorizontalWrapMode.Wrap;
                name.verticalOverflow = VerticalWrapMode.Truncate;
                Stretch(name.rectTransform, 5f);

                if (stack.Count > 1 || def.MaxStack > 1)
                {
                    var count = MakeText(image.transform, "Count", "x" + stack.Count, 26, TextAnchor.LowerRight, Color.white);
                    count.fontStyle = FontStyle.Bold;
                    count.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
                    Stretch(count.rectTransform, 6f);
                }

                _views.Add(new StackView { Stack = stack, FromCrate = fromCrate, Rect = image.rectTransform, Background = image, BaseColor = color });
            }
        }

        private static void Clear(RectTransform layer)
        {
            for (var i = layer.childCount - 1; i >= 0; i--) Destroy(layer.GetChild(i).gameObject);
        }

        // ---------- building the window ----------

        private void Build()
        {
            var canvasGo = new GameObject("CrateCanvas", typeof(Canvas), typeof(CanvasScaler));
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
            Stretch(_root.GetComponent<RectTransform>(), 0f);

            var backdrop = MakeImage(_root.transform, "Backdrop", new Color(0f, 0f, 0f, 0.75f));
            Stretch(backdrop.rectTransform, 0f);

            var panel = MakeImage(_root.transform, "Panel", new Color(0.13f, 0.12f, 0.11f, 1f));
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel.rectTransform.sizeDelta = new Vector2(PanelW, PanelH);
            panel.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.15f);
            var p = panel.transform;

            var title = MakeText(p, "Title", "CONTAINER", 40, TextAnchor.MiddleLeft, Color.white);
            Place(title.rectTransform, Margin, 20f, 600f, 60f);

            _crateLabel = MakeText(p, "CrateLabel", "", 28, TextAnchor.MiddleLeft, new Color(0.9f, 0.8f, 0.6f));
            _playerLabel = MakeText(p, "PlayerLabel", "", 28, TextAnchor.MiddleRight, new Color(0.8f, 0.9f, 0.8f));

            _crateLayer = MakeLayer(p, "CrateGrid");
            _playerLayer = MakeLayer(p, "PlayerGrid");

            var takeAll = MakeImage(p, "TakeAll", new Color(0.25f, 0.42f, 0.3f));
            _takeAllImage = takeAll;
            _takeAllRect = takeAll.rectTransform;
            Place(_takeAllRect, Margin, PanelH - 90f, 280f, 56f);
            _takeAllLabel = MakeText(takeAll.transform, "Label", "TAKE ALL", 28, TextAnchor.MiddleCenter, Color.white);
            Stretch(_takeAllLabel.rectTransform, 0f);

            _message = MakeText(p, "Message", "", 28, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f));
            Place(_message.rectTransform, 360f, PanelH - 90f, 700f, 56f);

            var hint = MakeText(p, "Hint", "Click: move stack    Shift + click: move one    Esc: close", 22,
                TextAnchor.MiddleRight, new Color(0.7f, 0.7f, 0.7f));
            Place(hint.rectTransform, PanelW - Margin - 700f, PanelH - 90f, 700f, 56f);
        }

        private RectTransform MakeLayer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private Text MakeText(Transform parent, string name, string content, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.text = content;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static Image MakeImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Top left anchored: x to the right, y DOWN from the parent's top edge.</summary>
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static bool Contains(RectTransform rect, Vector2 screenPoint)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);
        }
    }
}
