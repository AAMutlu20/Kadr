using System.Collections;
using System.Collections.Generic;
using Core;
using Events;
using Items;
using Menus;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Photography
{
    /// <summary>
    /// The camera upgrades screen. Opened by clicking the red book on the suitcase lid (an InventoryHotspot), like the
    /// photo album window.
    ///
    ///   top two rows:  the two branches, REPORTER (safe) and ARTIST (valuable), four tiers each, left to right
    ///   bottom row:    the four flash abilities, each with levels, a price and an EQUIP button (one is active at a time)
    ///
    /// The first branch tier you buy locks the other branch for good, so that first click only asks, and a second click
    /// commits. Everything else is one click. Esc or right click goes back to the suitcase, Tab closes everything.
    /// The mouse is read directly (no EventSystem needed), same as the album window.
    /// </summary>
    public class CameraUpgradesOverlay : MonoBehaviour
    {
        private enum CardKind { BranchTier, Ability }

        private sealed class Card
        {
            public CardKind Kind;
            public CameraBranch Branch;
            public int Tier;
            public CameraAbility Ability;
            public RectTransform Rect;
            public Image Frame;
            public Text Title, Body, Level, Cost;
            public Image Lock;        // shown instead of the description while the upgrade is hidden
            public Text LockText;     // fallback when there is no icon texture
            public RectTransform EquipRect;
            public Image EquipBg;
            public Text EquipText;
        }

        private const float CardW = 410f;
        private const float CardGap = 20f;

        [Header("References")]
        [SerializeField] private Inventory inventory;
        [SerializeField] private InventoryView3D inventoryView;
        [Tooltip("The clickable red book (a BoxCollider + InventoryHotspot).")]
        [SerializeField] private InventoryHotspot bookHotspot;
        [Tooltip("The upgrade tree asset. Empty = the one the camera uses, or a default made in memory.")]
        [SerializeField] private CameraUpgradeTree upgradeTree;

        [Header("Look")]
        [Tooltip("The lock picture (a Sprite, Texture Type: Sprite 2D and UI). Shown instead of the description on upgrades you cannot see yet. Empty = the word LOCKED.")]
        [SerializeField] private Sprite lockIcon;
        [Tooltip("The lock shown when you can afford the upgrade and it is the next one: a different sprite, drawn yellow. Empty = the normal lock, tinted yellow.")]
        [SerializeField] private Sprite affordableIcon;

        private readonly List<Card> _cards = new();
        private CameraUpgradeTree _tree;
        private CameraProgress _progress;
        private EventBus _bus;
        private Font _font;
        private GameObject _root;
        private Text _xpText;
        private Text _message;

        private bool _open;
        private bool _dirty;
        private Card _hovered;
        private Card _armed;
        private float _messageUntil;

        private static readonly Color Dim = new(0.16f, 0.16f, 0.17f);
        private static readonly Color VeryDim = new(0.09f, 0.09f, 0.1f);
        private static readonly Color Owned = new(0.17f, 0.34f, 0.2f);
        private static readonly Color Next = new(0.27f, 0.27f, 0.31f);
        private static readonly Color Armed = new(0.55f, 0.4f, 0.1f);

        private void Start()
        {
            if (!inventory || !inventoryView)
            {
                Debug.LogError("[CameraUpgradesOverlay] Inventory or Inventory View is not assigned.", this);
                enabled = false;
                return;
            }

            _tree = CameraUpgradeTree.Resolve(upgradeTree);
            _progress = CameraProgress.Current;
            _font = KadrFont.Body;
            Build();

            _progress.Changed += MarkDirty;
            if (GameServices.TryGet(out _bus)) _bus.Subscribe<XpChangedEvent>(OnXp);
            if (bookHotspot) bookHotspot.Clicked += Open;
        }

        private void OnDestroy()
        {
            if (_progress != null) _progress.Changed -= MarkDirty;
            if (_bus != null) _bus.Unsubscribe<XpChangedEvent>(OnXp);
            if (bookHotspot) bookHotspot.Clicked -= Open;
        }

        private void OnDisable()
        {
            if (_open) CloseNow();
        }

        private void OnXp(XpChangedEvent e) => MarkDirty();
        private void MarkDirty() => _dirty = true;

        // ---------- open and close ----------

        public void Open()
        {
            if (_open || !inventory.IsOpen) return;

            _open = true;
            inventoryView.Blocked = true;
            _root.SetActive(true);
            _armed = null;
            _message.text = "";
            Refresh();
        }

        private void Close()
        {
            if (!_open) return;

            _open = false;
            _root.SetActive(false);
            _armed = null;
            StartCoroutine(UnblockNextFrame());
        }

        private IEnumerator UnblockNextFrame()
        {
            yield return null; // so the same Esc does not also close the suitcase
            inventoryView.Blocked = false;
        }

        private void CloseNow()
        {
            _open = false;
            _armed = null;
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

            var previous = _hovered;
            _hovered = null;
            foreach (var card in _cards)
            {
                if (Contains(card.Rect, pointer)) { _hovered = card; break; }
            }

            if (_hovered != previous) _dirty = true;

            if (mouse.leftButton.wasPressedThisFrame) Click(pointer);

            if (_message.text.Length > 0 && Time.unscaledTime >= _messageUntil) _message.text = "";
            if (_dirty) Refresh();
        }

        private void Click(Vector2 pointer)
        {
            var card = _hovered;

            // Anywhere else cancels a pending "click again".
            if (card != _armed && _armed != null)
            {
                _armed = null;
                _dirty = true;
            }

            if (card == null) return;

            if (card.Kind == CardKind.Ability && card.EquipRect && Contains(card.EquipRect, pointer))
            {
                if (_progress.Equip(card.Ability)) Say(_tree.Ability(card.Ability).title + " equipped");
                else Say("Buy the first level to unlock it");
                return;
            }

            if (card.Kind == CardKind.BranchTier) ClickBranchTier(card);
            else ClickAbility(card);
        }

        private void ClickBranchTier(Card card)
        {
            if (_progress.IsBranchLocked(card.Branch)) { Say("That branch is locked out for good"); return; }

            var owned = _progress.Branch == card.Branch ? _progress.BranchTier : 0;
            if (card.Tier <= owned) return;
            if (card.Tier != owned + 1) { Say("Buy the earlier tiers first"); return; }

            var cost = _progress.NextBranchTierCost(_tree, card.Branch);
            if (PlayerXp.Points < cost) { Say(NeedMore(cost)); return; }

            // The first purchase picks the branch for good: ask once.
            if (_progress.Branch == CameraBranch.None && _armed != card)
            {
                _armed = card;
                _dirty = true;
                return;
            }

            _armed = null;
            if (_progress.TryBuyBranchTier(_tree, card.Branch)) Say("Upgraded");
        }

        private void ClickAbility(Card card)
        {
            var cost = _progress.NextAbilityLevelCost(_tree, card.Ability);
            if (cost < 0) { Say("Already at the top level"); return; }
            if (PlayerXp.Points < cost) { Say(NeedMore(cost)); return; }

            if (_progress.TryBuyAbilityLevel(_tree, card.Ability)) Say("Upgraded");
        }

        private static string NeedMore(int cost)
        {
            var missing = cost - PlayerXp.Points;
            return $"Not enough points: {missing} more {(missing == 1 ? "point" : "points")} needed";
        }

        private static string Points(int n) => n == 1 ? "1 point" : n + " points";

        private void Say(string text)
        {
            _message.text = text;
            _messageUntil = Time.unscaledTime + 2.5f;
        }

        // ---------- showing the state ----------

        private void Refresh()
        {
            _dirty = false;
            _xpText.text = $"POINTS  <b>{PlayerXp.Points}</b>";

            foreach (var card in _cards)
            {
                if (card.Kind == CardKind.BranchTier) RefreshBranchCard(card);
                else RefreshAbilityCard(card);
            }

            // A light tint on the card under the mouse.
            if (_hovered != null) _hovered.Frame.color = Color.Lerp(_hovered.Frame.color, Color.white, 0.12f);
        }

        /// <summary>
        /// How much a branch card shows depends on how far it is from the next thing you can buy:
        ///   owned or next to buy:  everything (the next one also has a small lock, yellow when you can afford it)
        ///   one further:           the name and a lock
        ///   two or more further, or in the branch you did not pick:  dark, a big lock, nothing else
        /// </summary>
        private void RefreshBranchCard(Card card)
        {
            var data = _tree.Branch(card.Branch);
            var tier = data.tiers[card.Tier - 1];

            var lockedOut = _progress.IsBranchLocked(card.Branch);
            var ownedCount = _progress.Branch == card.Branch ? _progress.BranchTier : 0;
            var owned = !lockedOut && ownedCount >= card.Tier;
            var distance = card.Tier - (ownedCount + 1); // 0 = the one you can buy now
            var cost = tier.costPoints;

            var showAll = owned || (!lockedOut && distance == 0);
            var showName = showAll || (!lockedOut && distance == 1);
            var affordable = !lockedOut && distance == 0 && PlayerXp.Points >= cost;
            var showLock = !owned;

            card.Title.gameObject.SetActive(showName);
            card.Title.text = $"{card.Tier}.  {tier.title}";
            card.Body.gameObject.SetActive(showAll);
            card.Body.text = tier.description;
            card.Cost.gameObject.SetActive(showAll);

            // The lock: small in the corner of the card you can buy, big in the middle of the ones that are hidden.
            var big = !showAll;
            var size = !big ? 44f : showName ? 64f : 96f;
            var rect = card.Lock.rectTransform;
            if (!big) SetTopLeft(rect, CardW - size - 14f, 8f, size, size);
            else SetTopLeft(rect, (CardW - size) * 0.5f, showName ? 62f : 36f, size, size);

            card.Lock.sprite = affordable && affordableIcon ? affordableIcon : lockIcon;
            card.Lock.color = affordable ? new Color(1f, 0.85f, 0.25f, 1f)
                : showName ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.3f);
            card.Lock.gameObject.SetActive(showLock && lockIcon);
            card.LockText.gameObject.SetActive(showLock && big && !lockIcon);

            if (owned)
            {
                card.Frame.color = Owned;
                card.Cost.text = "<color=#80e090>OWNED</color>";
            }
            else if (!showName)
            {
                card.Frame.color = VeryDim;
            }
            else if (!showAll)
            {
                card.Frame.color = Dim;
            }
            else if (_armed == card)
            {
                card.Frame.color = Armed;
                card.Cost.text = "<color=#ffd060>Click again to commit. The other branch is locked for good.</color>";
            }
            else
            {
                card.Frame.color = Next;
                card.Cost.text = affordable ? $"<b>{Points(cost)}</b>" : $"<color=#e07070>{Points(cost)}</color>";
            }
        }

        private void RefreshAbilityCard(Card card)
        {
            var data = _tree.Ability(card.Ability);
            var level = _progress.AbilityLevel(card.Ability);
            var max = data.levels.Count;

            card.Title.text = data.title;
            card.Body.text = data.description;

            var pips = new System.Text.StringBuilder();
            for (var i = 1; i <= max; i++) pips.Append(i <= level ? "[#]" : "[ ]");
            var current = level > 0 ? data.levels[level - 1] : null;
            card.Level.text = level == 0
                ? $"{pips}  not unlocked"
                : $"{pips}  {current.description}\n<color=#aaaaaa>cooldown {current.cooldown:0} s</color>";

            var cost = _progress.NextAbilityLevelCost(_tree, card.Ability);
            if (cost < 0)
            {
                card.Cost.text = "<color=#80e090>MAX LEVEL</color>";
            }
            else
            {
                var enough = PlayerXp.Points >= cost;
                var verb = level == 0 ? "Unlock" : "Upgrade";
                card.Cost.text = enough ? $"{verb}:  <b>{Points(cost)}</b>" : $"{verb}:  <color=#e07070>{Points(cost)}</color>";
            }

            var equipped = _progress.Equipped == card.Ability;
            card.Frame.color = equipped ? Owned : level > 0 ? Next : Dim;
            card.EquipBg.color = equipped ? new Color(0.25f, 0.5f, 0.3f) : level > 0 ? new Color(0.32f, 0.32f, 0.36f) : new Color(0.2f, 0.2f, 0.2f);
            card.EquipText.text = equipped ? "EQUIPPED" : "EQUIP";
            card.EquipText.color = level > 0 ? Color.white : new Color(1f, 1f, 1f, 0.35f);
        }

        // ---------- building the window ----------

        private void Build()
        {
            var canvasGo = new GameObject("CameraUpgradesCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 61;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(canvasGo.transform, false);
            Stretch(_root.GetComponent<RectTransform>());

            var backdrop = MakeImage(_root.transform, "Backdrop", new Color(0f, 0f, 0f, 0.8f));
            Stretch(backdrop.rectTransform);

            var panel = MakeImage(_root.transform, "Panel", new Color(0.13f, 0.12f, 0.11f, 1f));
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel.rectTransform.sizeDelta = new Vector2(1800f, 1000f);
            panel.gameObject.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.15f);
            var p = panel.transform;

            MakeText(p, "CAMERA UPGRADES", 42, TextAnchor.MiddleLeft, Color.white, 40f, 14f, 800f, 62f);
            _xpText = MakeText(p, "", 36, TextAnchor.MiddleRight, new Color(0.9f, 0.85f, 0.6f), 1000f, 18f, 760f, 56f);
            MakeLine(p, 40f, 82f, 1720f);

            var safe = _tree.Branch(CameraBranch.Safe);
            var valuable = _tree.Branch(CameraBranch.Valuable);
            var blue = new Color(0.55f, 0.78f, 1f);
            var orange = new Color(1f, 0.72f, 0.45f);
            var green = new Color(0.6f, 0.9f, 0.65f);

            MakeSectionHeader(p, safe.title, safe.tagline, blue, 96f);
            for (var i = 0; i < safe.tiers.Count; i++) _cards.Add(MakeBranchCard(p, CameraBranch.Safe, i + 1, 160f, blue));

            MakeSectionHeader(p, valuable.title, valuable.tagline, orange, 346f);
            for (var i = 0; i < valuable.tiers.Count; i++) _cards.Add(MakeBranchCard(p, CameraBranch.Valuable, i + 1, 410f, orange));

            MakeSectionHeader(p, "FLASH ABILITIES", "One is active at a time. Unlock it, then equip it.", green, 596f);
            var abilityOrder = new[] { CameraAbility.Stun, CameraAbility.Disorient, CameraAbility.Fireshow, CameraAbility.Reveal };
            for (var i = 0; i < abilityOrder.Length; i++) _cards.Add(MakeAbilityCard(p, abilityOrder[i], i, 660f, green));

            _message = MakeText(p, "", 28, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f), 40f, 922f, 1720f, 40f);
            MakeText(p, "Click a card to buy it     Esc or right click: back to the suitcase     Tab: close everything",
                22, TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.7f), 40f, 960f, 1720f, 34f);

            _root.SetActive(false);
        }

        /// <summary>A section title in its colour, with its description beside it (it wraps to two lines if it must).</summary>
        private void MakeSectionHeader(Transform parent, string title, string tagline, Color colour, float y)
        {
            MakeText(parent, title, 30, TextAnchor.MiddleLeft, colour, 40f, y, 330f, 56f).fontStyle = FontStyle.Bold;

            var tag = MakeText(parent, tagline, 22, TextAnchor.MiddleLeft, new Color(0.78f, 0.78f, 0.78f), 380f, y, 1380f, 56f, true, 15);
            tag.fontStyle = FontStyle.Italic;
        }

        private Card MakeBranchCard(Transform parent, CameraBranch branch, int tier, float y, Color accent)
        {
            var card = new Card { Kind = CardKind.BranchTier, Branch = branch, Tier = tier };
            var x = 40f + (tier - 1) * (CardW + CardGap);

            card.Frame = MakeImage(parent, "Card", Dim);
            card.Rect = card.Frame.rectTransform;
            SetTopLeft(card.Rect, x, y, CardW, 168f);
            MakeAccent(card.Frame.transform, accent, 168f);

            card.Title = MakeText(card.Frame.transform, "", 29, TextAnchor.MiddleLeft, Color.white, 20f, 8f, CardW - 32f, 36f);
            card.Body = MakeText(card.Frame.transform, "", 21, TextAnchor.UpperLeft, new Color(0.85f, 0.85f, 0.85f), 20f, 48f, CardW - 32f, 80f, true, 14);
            card.Cost = MakeText(card.Frame.transform, "", 24, TextAnchor.MiddleLeft, Color.white, 20f, 132f, CardW - 32f, 32f);

            // A lock in the middle of the body, while the upgrade is hidden.
            var lockGo = new GameObject("Lock", typeof(RectTransform), typeof(Image));
            lockGo.transform.SetParent(card.Frame.transform, false);
            SetTopLeft(lockGo.GetComponent<RectTransform>(), (CardW - 64f) * 0.5f, 62f, 64f, 64f);
            card.Lock = lockGo.GetComponent<Image>();
            card.Lock.sprite = lockIcon;
            card.Lock.preserveAspect = true;
            card.Lock.color = new Color(1f, 1f, 1f, 0.55f);
            card.Lock.raycastTarget = false;

            card.LockText = MakeText(card.Frame.transform, "LOCKED", 26, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.35f), 20f, 62f, CardW - 32f, 64f);
            return card;
        }

        private Card MakeAbilityCard(Transform parent, CameraAbility ability, int index, float y, Color accent)
        {
            var card = new Card { Kind = CardKind.Ability, Ability = ability };
            var x = 40f + index * (CardW + CardGap);

            card.Frame = MakeImage(parent, "AbilityCard", Dim);
            card.Rect = card.Frame.rectTransform;
            SetTopLeft(card.Rect, x, y, CardW, 252f);
            MakeAccent(card.Frame.transform, accent, 252f);

            card.Title = MakeText(card.Frame.transform, "", 31, TextAnchor.MiddleLeft, Color.white, 20f, 8f, CardW - 32f, 38f);
            card.Body = MakeText(card.Frame.transform, "", 21, TextAnchor.UpperLeft, new Color(0.85f, 0.85f, 0.85f), 20f, 50f, CardW - 32f, 70f, true, 14);
            card.Level = MakeText(card.Frame.transform, "", 22, TextAnchor.UpperLeft, Color.white, 20f, 124f, CardW - 32f, 62f, true, 14);
            card.Cost = MakeText(card.Frame.transform, "", 24, TextAnchor.MiddleLeft, Color.white, 20f, 186f, CardW - 32f, 32f);

            card.EquipBg = MakeImage(card.Frame.transform, "Equip", new Color(0.3f, 0.3f, 0.3f));
            card.EquipRect = card.EquipBg.rectTransform;
            SetTopLeft(card.EquipRect, 20f, 214f, CardW - 32f, 30f);
            card.EquipText = MakeText(card.EquipBg.transform, "EQUIP", 22, TextAnchor.MiddleCenter, Color.white, 0f, 0f, CardW - 32f, 30f);
            return card;
        }

        /// <summary>A thin coloured stripe down the left edge of a card.</summary>
        private static void MakeAccent(Transform card, Color colour, float height)
        {
            var stripe = MakeImage(card, "Accent", colour);
            SetTopLeft(stripe.rectTransform, 0f, 0f, 6f, height);
        }

        private static void MakeLine(Transform parent, float x, float y, float width)
        {
            var line = MakeImage(parent, "Line", new Color(1f, 1f, 1f, 0.12f));
            SetTopLeft(line.rectTransform, x, y, width, 2f);
        }

        private Text MakeText(Transform parent, string content, int size, TextAnchor alignment, Color color,
            float x, float y, float w, float h, bool fit = false, int minSize = 14)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
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
            text.verticalOverflow = fit ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            // Text that must stay inside its box shrinks instead of spilling out, so longer descriptions still fit.
            if (fit)
            {
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = minSize;
                text.resizeTextMaxSize = size;
            }

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
