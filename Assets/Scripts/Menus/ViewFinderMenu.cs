using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Menus
{
    /// <summary>One line in a menu page.</summary>
    public class MenuEntry
    {
        public string Label;
        /// <summary>Small text on the right (the Russian word). Replaced by the value when there is one.</summary>
        public string Sub;
        /// <summary>Click / Enter. Plays the shutter.</summary>
        public Action Select;
        /// <summary>Left / right keys, scroll wheel, and a click when there is no Select. -1 or +1.</summary>
        public Action<int> Adjust;
        /// <summary>Shown as "&lt;  value  &gt;" on the right.</summary>
        public Func<string> Value;
        /// <summary>False: the value is shown as it is, without the arrows (key names).</summary>
        public bool Arrows = true;
        /// <summary>A section title: small, not selectable.</summary>
        public bool Header;
        /// <summary>0 to 1. Draws a row of ticks before the value (volume, sensitivity...).</summary>
        public Func<float> Meter;

        public RectTransform Rect;
        public Text LabelText;
        public Text RightText;
        public Image[] MeterTicks;
        public int MeterFilled;

        public void Refresh()
        {
            if (!RightText) return;

            string text;
            if (Value != null)
            {
                var value = Value();
                text = Arrows ? "<  " + value + "  >" : value;
            }
            else
            {
                text = Sub ?? "";
            }

            KadrFont.Fit(RightText, KadrFont.Mono, text);

            if (Meter != null && MeterTicks != null)
            {
                MeterFilled = Mathf.RoundToInt(Mathf.Clamp01(Meter()) * MeterTicks.Length);
            }
        }
    }

    /// <summary>One tab of a page: a name and its own lines.</summary>
    public class MenuTab
    {
        public string Name = "";
        public readonly List<MenuEntry> Entries = new();
    }

    public class MenuPage
    {
        public string Heading = "";
        public float Spacing = 104f;
        /// <summary>Esc, or a right click on a line that cannot be adjusted.</summary>
        public Action Back;
        /// <summary>The line of key hints at the bottom. Empty = the standard one.</summary>
        public string Hint;
        /// <summary>Hides the big title and starts the page higher, so a long page has room. Lists then scroll.</summary>
        public bool Compact;
        /// <summary>0 = the menu's normal width.</summary>
        public float Width;
        /// <summary>With tabs, the page shows a tab strip and Entries is the lines of the current tab.</summary>
        public readonly List<MenuTab> Tabs = new();
        public int Tab;

        private readonly List<MenuEntry> _own = new();

        public List<MenuEntry> Entries => Tabs.Count > 0 ? Tabs[Mathf.Clamp(Tab, 0, Tabs.Count - 1)].Entries : _own;
    }

    /// <summary>
    /// The menu engine: a camera viewfinder drawn over whatever is behind it (the corner marks, REC dot, timecode, film
    /// grain, a title) and pages of lines. A focus box in the shape of an autofocus frame hunts onto the line under the
    /// mouse and locks; clicking a line fires the shutter (a white flash and a click). Everything is built in code.
    /// MainMenu uses it for the main menu and the settings; the pause menu can use it the same way.
    ///
    /// Needs the Input System package (it is the new Input System project).
    /// </summary>
    public class ViewfinderMenu : MonoBehaviour
    {
        [Header("Sounds (all optional)")]
        [SerializeField] private AudioClip hoverClip;
        [Tooltip("Played when the focus box locks onto a line.")]
        [SerializeField] private AudioClip lockClip;
        [SerializeField] private AudioClip shutterClip;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;

        [Header("Layout (1920 x 1080 reference, from the top left)")]
        [SerializeField] private Vector2 titlePosition = new(150f, -130f);
        [SerializeField] private Vector2 pagePosition = new(150f, -430f);
        [Tooltip("Where pages without the big title (Compact) start.")]
        [SerializeField] private Vector2 compactPosition = new(150f, -150f);
        [SerializeField] private float entryWidth = 780f;
        [SerializeField] private string title = "K A D R";
        [SerializeField] private string subtitle = "КАДР   ·   1974";

        // Parts that MainMenu fades in during the intro.
        public CanvasGroup ChromeGroup { get { EnsureBuilt(); return _chromeGroup; } }
        public CanvasGroup TitleGroup { get { EnsureBuilt(); return _titleGroup; } }
        public CanvasGroup PageGroup { get { EnsureBuilt(); return _pageGroup; } }

        /// <summary>Off during the intro, and while something is loading.</summary>
        public bool Interactable { get; set; } = true;

        private bool _built;
        private AudioSource _audio;
        private RectTransform _root;
        private CanvasGroup _chromeGroup;
        private CanvasGroup _titleGroup;
        private CanvasGroup _pageGroup;
        private CanvasGroup _containerGroup;
        private RectTransform _container;

        private RawImage _grain;
        private RawImage _recDot;
        private Text _timecode;
        private Text _frameText;
        private Text _message;
        private Text _hints;
        private Text _titleMain;
        private RectTransform _titleRed;
        private Text _afText;
        private BracketBox _focus;
        private Image _flash;
        private Image _fade;

        private const string DefaultHint = "CLICK  SELECT        RIGHT CLICK / ESC  BACK        WHEEL  ADJUST";
        private const string TabHint = "Q / E  TABS        CLICK  SELECT        ESC  BACK        WHEEL  SCROLL";
        private const float CompactListHeight = 650f;

        private readonly List<RectTransform> _tabRects = new();
        private RectTransform _list;
        private RectTransform _content;
        private RectTransform _scrollTrack;
        private RectTransform _scrollThumb;
        private float _scroll;
        private float _scrollTarget;
        private float _listHeight = 700f;
        private float _contentHeight;

        private MenuPage _page;
        private int _index = -1;
        private float _pageT = 1f;
        private float _clock;
        private int _frameNumber = 1;
        private float _grainTimer;
        private float _messageLeft;
        private float _flashAlpha;
        private float _flickerTimer = 3f;
        private float _flickerLeft;
        private Vector2 _lastMouse;

        // Focus box spring (screen pixels for the position, canvas units for the size)
        private Vector2 _focusPos;
        private Vector2 _focusPosVel;
        private Vector2 _focusSize;
        private Vector2 _focusSizeVel;
        private float _settledFor;
        private bool _locked;
        private bool _focusPlaced;

        private void Awake() => EnsureBuilt();

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            Build();
        }

        // ---------- public ----------

        /// <summary>Shows a page. The first line gets the focus.</summary>
        public void Show(MenuPage page, bool animate = true)
        {
            EnsureBuilt();

            _page = page;
            _titleGroup.gameObject.SetActive(!page.Compact);
            _hints.text = !string.IsNullOrEmpty(page.Hint) ? page.Hint : page.Tabs.Count > 1 ? TabHint : DefaultHint;

            BuildEntries();
            _pageT = animate ? 0f : 1f;
            _index = -1; // so the first line is treated as a new focus target even if the old page had one at the same spot
            SetIndex(FirstSelectable(), false);
        }

        /// <summary>Re-reads the values of the lines (after changing a setting from outside).</summary>
        public void RefreshValues()
        {
            if (_page == null) return;
            foreach (var entry in _page.Entries) entry.Refresh();
        }

        /// <summary>The white flash of a camera. 0.6 is a click, 1 is a full flash.</summary>
        public void Flash(float strength = 0.6f)
        {
            EnsureBuilt();
            _flashAlpha = Mathf.Max(_flashAlpha, strength);
        }

        /// <summary>Flash, shutter sound and the frame counter going up.</summary>
        public void Shutter()
        {
            Flash(0.55f);
            Play(shutterClip);
            _frameNumber++;
            if (_frameText) _frameText.text = "FRAME " + _frameNumber.ToString("000");
        }

        /// <summary>A short orange line at the bottom, gone after a few seconds.</summary>
        public void Message(string text)
        {
            EnsureBuilt();
            _message.text = text;
            _messageLeft = 3.5f;
        }

        public IEnumerator FadeOut(float seconds)
        {
            EnsureBuilt();

            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                _fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(t / seconds));
                yield return null;
            }

            _fade.color = Color.black;
        }

        // ---------- building ----------

        private void Build()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.ignoreListenerPause = true;

            var canvas = MenuUi.MakeCanvas("ViewfinderCanvas", transform, 10);
            _root = (RectTransform)canvas.transform;

            // Darken the scene a little and the edges a lot, so the text reads over anything.
            MenuUi.Stretch(MenuUi.Box("Dim", _root, new Color(0f, 0f, 0f, 0.3f)).rectTransform);
            MenuUi.Stretch(MenuUi.Picture("Vignette", _root, MenuTextures.Vignette, Color.white).rectTransform);

            BuildChrome();
            BuildTitle();
            BuildPageHost();

            _grain = MenuUi.Picture("Grain", _root, MenuTextures.Noise, new Color(1f, 1f, 1f, 0.07f));
            MenuUi.Stretch(_grain.rectTransform);

            _flash = MenuUi.Box("Flash", _root, new Color(1f, 1f, 1f, 0f));
            MenuUi.Stretch(_flash.rectTransform);
            _fade = MenuUi.Box("Fade", _root, new Color(0f, 0f, 0f, 0f));
            MenuUi.Stretch(_fade.rectTransform);
        }

        private void BuildChrome()
        {
            var chrome = MenuUi.Rect("Chrome", _root);
            MenuUi.Stretch(chrome);
            _chromeGroup = chrome.gameObject.AddComponent<CanvasGroup>();

            // Rule of thirds, very faint.
            for (var i = 1; i <= 2; i++)
            {
                var f = i / 3f;

                var vertical = MenuUi.Box("Third V", chrome, MenuUi.Faint).rectTransform;
                vertical.anchorMin = new Vector2(f, 0f);
                vertical.anchorMax = new Vector2(f, 1f);
                vertical.pivot = new Vector2(0.5f, 0.5f);
                vertical.sizeDelta = new Vector2(2f, 0f);
                vertical.anchoredPosition = Vector2.zero;

                var horizontal = MenuUi.Box("Third H", chrome, MenuUi.Faint).rectTransform;
                horizontal.anchorMin = new Vector2(0f, f);
                horizontal.anchorMax = new Vector2(1f, f);
                horizontal.pivot = new Vector2(0.5f, 0.5f);
                horizontal.sizeDelta = new Vector2(0f, 2f);
                horizontal.anchoredPosition = Vector2.zero;
            }

            // Corner marks of the viewfinder.
            var corners = new BracketBox(chrome, "Corners", 120f, 5f, new Color(0.93f, 0.91f, 0.84f, 0.75f));
            corners.Root.anchorMin = Vector2.zero;
            corners.Root.anchorMax = Vector2.one;
            corners.Root.offsetMin = new Vector2(64f, 64f);
            corners.Root.offsetMax = new Vector2(-64f, -64f);

            // Centre cross.
            var cross = MenuUi.Box("Cross H", chrome, MenuUi.Dim).rectTransform;
            MenuUi.Place(cross, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 3f));
            cross = MenuUi.Box("Cross V", chrome, MenuUi.Dim).rectTransform;
            MenuUi.Place(cross, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, 30f));

            // REC and timecode, top left.
            _recDot = MenuUi.Picture("RecDot", chrome, MenuTextures.Disc, MenuUi.Rec);
            MenuUi.Place(_recDot.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(112f, -102f), new Vector2(24f, 24f));

            var rec = MenuUi.Label("Rec", chrome, "REC", 30, MenuUi.Paper, TextAnchor.UpperLeft, FontStyle.Bold, true);
            MenuUi.Place(rec.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(148f, -97f), new Vector2(120f, 40f));

            _timecode = MenuUi.Label("Timecode", chrome, "00:00:00:00", 30, MenuUi.Dim, TextAnchor.UpperLeft, FontStyle.Normal, true);
            MenuUi.Place(_timecode.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(250f, -97f), new Vector2(300f, 40f));

            // Battery, top right.
            var battery = MenuUi.Frame(chrome, "Battery", MenuUi.Paper, 3f);
            MenuUi.Place(battery, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-112f, -100f), new Vector2(66f, 28f));
            for (var i = 0; i < 3; i++)
            {
                var cell = MenuUi.Box("Cell", battery, MenuUi.Paper).rectTransform;
                MenuUi.Place(cell, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(7f + i * 19f, 0f), new Vector2(15f, 14f));
            }

            // Exposure values, bottom left. Frame number, bottom right.
            var info = MenuUi.Label("Exposure", chrome, "ISO 400    F2.8    1/60", 26, MenuUi.Dim, TextAnchor.LowerLeft, FontStyle.Normal, true);
            MenuUi.Place(info.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(112f, 96f), new Vector2(600f, 36f));

            _frameText = MenuUi.Label("Frame", chrome, "FRAME 001", 26, MenuUi.Paper, TextAnchor.LowerRight, FontStyle.Normal, true);
            MenuUi.Place(_frameText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-112f, 96f), new Vector2(400f, 36f));

            _hints = MenuUi.Label("Hints", chrome, DefaultHint, 22, MenuUi.Dim, TextAnchor.LowerCenter, FontStyle.Normal, true);
            MenuUi.Place(_hints.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(1100f, 30f));

            _message = MenuUi.Label("Message", chrome, "", 28, MenuUi.Accent, TextAnchor.LowerCenter, FontStyle.Normal, true);
            MenuUi.Place(_message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1400f, 40f));
        }

        private void BuildTitle()
        {
            var group = MenuUi.Rect("Title", _root);
            MenuUi.Stretch(group);
            _titleGroup = group.gameObject.AddComponent<CanvasGroup>();

            // A red copy a few pixels off, like a misregistered print.
            var red = MenuUi.Label("TitleRed", group, title, 200, new Color(0.9f, 0.14f, 0.12f, 0.35f), TextAnchor.UpperLeft, FontStyle.Bold);
            _titleRed = red.rectTransform;
            MenuUi.Place(_titleRed, new Vector2(0f, 1f), new Vector2(0f, 1f), titlePosition + new Vector2(4f, -3f), new Vector2(1100f, 240f));

            _titleMain = MenuUi.Label("TitleMain", group, title, 200, MenuUi.Paper, TextAnchor.UpperLeft, FontStyle.Bold);
            MenuUi.Place(_titleMain.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), titlePosition, new Vector2(1100f, 240f));

            var sub = MenuUi.Label("Subtitle", group, subtitle, 30, MenuUi.Accent, TextAnchor.UpperLeft);
            MenuUi.Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), titlePosition + new Vector2(10f, -235f), new Vector2(800f, 40f));

            // The rubber stamp across the corner of the title.
            var stamp = MenuUi.Rect("Stamp", group);
            MenuUi.Place(stamp, new Vector2(0f, 1f), new Vector2(0f, 1f), titlePosition + new Vector2(640f, -20f), new Vector2(300f, 64f));
            stamp.localRotation = Quaternion.Euler(0f, 0f, -7f);

            var frame = MenuUi.Frame(stamp, "StampFrame", new Color(0.9f, 0.14f, 0.12f, 0.8f), 4f);
            MenuUi.Stretch(frame);
            var stampText = MenuUi.Label("StampText", stamp, "СЕКРЕТНО", 38, new Color(0.9f, 0.14f, 0.12f, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUi.Stretch(stampText.rectTransform);
        }

        private void BuildPageHost()
        {
            var outer = MenuUi.Rect("Page", _root);
            MenuUi.Stretch(outer);
            _pageGroup = outer.gameObject.AddComponent<CanvasGroup>();

            _container = MenuUi.Rect("Container", outer);
            MenuUi.Place(_container, new Vector2(0f, 1f), new Vector2(0f, 1f), pagePosition, new Vector2(entryWidth, 700f));
            _containerGroup = _container.gameObject.AddComponent<CanvasGroup>();

            // The focus box goes last, so it draws over the lines.
            _focus = new BracketBox(outer, "Focus", 22f, 4f, MenuUi.Paper);
            _focus.Root.gameObject.SetActive(false);

            _afText = MenuUi.Label("AF", _focus.Root, "AF", 22, MenuUi.Paper, TextAnchor.LowerLeft, FontStyle.Bold, true);
            MenuUi.Place(_afText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 6f), new Vector2(160f, 28f));
        }

        private void BuildEntries()
        {
            for (var i = _container.childCount - 1; i >= 0; i--) Destroy(_container.GetChild(i).gameObject);
            _tabRects.Clear();
            _scrollTrack = null;
            _scrollThumb = null;

            var width = _page.Width > 0f ? _page.Width : entryWidth;
            var topLeft = new Vector2(0f, 1f);
            _container.anchoredPosition = BasePosition;
            _container.sizeDelta = new Vector2(width, 700f);

            if (_page.Tabs.Count > 0) BuildTabStrip(width);
            else
            {
                var heading = MenuUi.Label("Heading", _container, _page.Heading, 26, MenuUi.Accent, TextAnchor.MiddleLeft, FontStyle.Normal, true);
                MenuUi.Place(heading.rectTransform, topLeft, topLeft, new Vector2(28f, 0f), new Vector2(width, 34f));
            }

            // The lines live in a clipped window, so a long page can scroll.
            _listHeight = _page.Compact ? CompactListHeight : 700f;
            _list = MenuUi.Rect("List", _container);
            MenuUi.Place(_list, topLeft, topLeft, new Vector2(0f, -56f), new Vector2(width, _listHeight));
            _list.gameObject.AddComponent<RectMask2D>();

            var entries = _page.Entries;
            _contentHeight = entries.Count * _page.Spacing;
            _content = MenuUi.Rect("Content", _list);
            MenuUi.Place(_content, topLeft, topLeft, Vector2.zero, new Vector2(width, _contentHeight));
            _scroll = 0f;
            _scrollTarget = 0f;

            var fontSize = Mathf.RoundToInt(_page.Spacing * 0.5f);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                var rect = MenuUi.Rect("Entry " + entry.Label, _content);
                MenuUi.Place(rect, topLeft, topLeft, new Vector2(0f, -i * _page.Spacing), new Vector2(width, _page.Spacing - 12f));
                entry.Rect = rect;

                if (entry.Header)
                {
                    entry.LabelText = MenuUi.Label("Label", rect, entry.Label, 22, MenuUi.Accent, TextAnchor.LowerLeft, FontStyle.Normal, true);
                    MenuUi.Stretch(entry.LabelText.rectTransform, 28f, 6f, 28f, 0f);
                    entry.RightText = null;

                    var rule = MenuUi.Box("Rule", rect, new Color(MenuUi.Accent.r, MenuUi.Accent.g, MenuUi.Accent.b, 0.35f)).rectTransform;
                    rule.anchorMin = new Vector2(0f, 0f);
                    rule.anchorMax = new Vector2(1f, 0f);
                    rule.pivot = new Vector2(0.5f, 0f);
                    rule.sizeDelta = new Vector2(-56f, 2f);
                    rule.anchoredPosition = Vector2.zero;
                    continue;
                }

                entry.LabelText = MenuUi.Label("Label", rect, entry.Label, fontSize, MenuUi.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
                MenuUi.Stretch(entry.LabelText.rectTransform, 28f, 0f, 28f, 0f);

                entry.RightText = MenuUi.Label("Right", rect, "", 26, MenuUi.Dim, TextAnchor.MiddleRight, FontStyle.Normal, true);
                MenuUi.Stretch(entry.RightText.rectTransform, 28f, 0f, 28f, 0f);

                entry.MeterTicks = null;
                if (entry.Meter != null) BuildMeter(entry, rect);

                entry.Refresh();
            }

            if (_contentHeight > _listHeight + 1f) BuildScrollbar(width);
        }

        private void BuildTabStrip(float width)
        {
            var topLeft = new Vector2(0f, 1f);
            const float tabWidth = 200f;

            for (var i = 0; i < _page.Tabs.Count; i++)
            {
                var rect = MenuUi.Rect("Tab " + _page.Tabs[i].Name, _container);
                MenuUi.Place(rect, topLeft, topLeft, new Vector2(i * tabWidth, 0f), new Vector2(tabWidth - 12f, 46f));
                _tabRects.Add(rect);

                var active = i == _page.Tab;
                var label = MenuUi.Label("Label", rect, _page.Tabs[i].Name, 28, active ? MenuUi.Paper : MenuUi.Dim, TextAnchor.MiddleLeft);
                MenuUi.Stretch(label.rectTransform, 14f, 6f, 0f, 0f);

                var line = MenuUi.Box("Line", rect, active ? MenuUi.Accent : MenuUi.Faint).rectTransform;
                line.anchorMin = new Vector2(0f, 0f);
                line.anchorMax = new Vector2(1f, 0f);
                line.pivot = new Vector2(0.5f, 0f);
                line.sizeDelta = new Vector2(0f, active ? 4f : 2f);
                line.anchoredPosition = Vector2.zero;
            }

            var heading = MenuUi.Label("Heading", _container, _page.Heading, 22, MenuUi.Dim, TextAnchor.LowerRight, FontStyle.Normal, true);
            MenuUi.Place(heading.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -4f), new Vector2(width * 0.35f, 40f));
        }

        private void BuildMeter(MenuEntry entry, RectTransform row)
        {
            const int ticks = 20;
            const float tickWidth = 10f;
            const float gap = 4f;

            var bar = MenuUi.Rect("Meter", row);
            MenuUi.Place(bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-300f, 0f),
                new Vector2(ticks * tickWidth + (ticks - 1) * gap, 20f));

            entry.MeterTicks = new Image[ticks];
            for (var t = 0; t < ticks; t++)
            {
                var tick = MenuUi.Box("Tick", bar, MenuUi.Faint);
                MenuUi.Place(tick.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(t * (tickWidth + gap), 0f),
                    new Vector2(tickWidth, t % 5 == 4 ? 20f : 14f));
                entry.MeterTicks[t] = tick;
            }
        }

        private void BuildScrollbar(float width)
        {
            _scrollTrack = MenuUi.Box("ScrollTrack", _container, MenuUi.Faint).rectTransform;
            MenuUi.Place(_scrollTrack, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(width + 22f, -56f), new Vector2(4f, _listHeight));

            _scrollThumb = MenuUi.Box("ScrollThumb", _scrollTrack, MenuUi.Dim).rectTransform;
            MenuUi.Place(_scrollThumb, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(4f, 60f));
        }

        private Vector2 BasePosition => _page != null && _page.Compact ? compactPosition : pagePosition;

        private int FirstSelectable()
        {
            var entries = _page.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                if (!entries[i].Header) return i;
            }

            return -1;
        }

        // ---------- every frame ----------

        private void Update()
        {
            if (!_built) return;

            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _clock += dt;

            UpdateChrome(dt);
            UpdatePage(dt);
            UpdateFocus(dt);
            if (Interactable) HandleInput();
            UpdateFlash(dt);
        }

        private void UpdateChrome(float dt)
        {
            // REC dot blinks once a second.
            var on = Mathf.FloorToInt(_clock) % 2 == 0;
            _recDot.color = on ? MenuUi.Rec : new Color(MenuUi.Rec.r, MenuUi.Rec.g, MenuUi.Rec.b, 0.15f);

            var total = Mathf.FloorToInt(_clock);
            _timecode.text = $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}:{(int)((_clock % 1f) * 25f):00}";

            // Film grain, jumping to a new spot 20 times a second.
            _grainTimer += dt;
            if (_grainTimer >= 0.05f)
            {
                _grainTimer = 0f;
                _grain.uvRect = new Rect(UnityEngine.Random.value, UnityEngine.Random.value, 1920f / 128f, 1080f / 128f);
            }

            // Now and then the title misregisters for a moment.
            _flickerTimer -= dt;
            if (_flickerTimer <= 0f)
            {
                _flickerTimer = UnityEngine.Random.Range(3.5f, 7f);
                _flickerLeft = 0.15f;
            }

            var jitter = Vector2.zero;
            var titleColor = MenuUi.Paper;
            if (_flickerLeft > 0f)
            {
                _flickerLeft -= dt;
                jitter = new Vector2(UnityEngine.Random.Range(-8f, 8f), UnityEngine.Random.Range(-3f, 3f));
                titleColor.a = 0.7f;
            }

            _titleRed.anchoredPosition = titlePosition + new Vector2(4f, -3f) + jitter;
            _titleMain.color = titleColor;

            if (_messageLeft > 0f)
            {
                _messageLeft -= dt;
                if (_messageLeft <= 0f) _message.text = "";
            }
        }

        private void UpdatePage(float dt)
        {
            UpdateScroll(dt);

            if (_pageT >= 1f && _containerGroup.alpha >= 1f) return;

            _pageT = Mathf.Min(1f, _pageT + dt / 0.3f);
            var eased = Mathf.SmoothStep(0f, 1f, _pageT);
            _containerGroup.alpha = eased;
            _container.anchoredPosition = BasePosition + new Vector2((1f - eased) * -50f, 0f);
        }

        private void UpdateScroll(float dt)
        {
            if (!_content) return;

            var max = Mathf.Max(0f, _contentHeight - _listHeight);
            _scrollTarget = Mathf.Clamp(_scrollTarget, 0f, max);
            _scroll = Mathf.Lerp(_scroll, _scrollTarget, 1f - Mathf.Exp(-16f * dt));
            if (Mathf.Abs(_scroll - _scrollTarget) < 0.1f) _scroll = _scrollTarget;

            _content.anchoredPosition = new Vector2(0f, _scroll);

            if (!_scrollThumb || max <= 0f) return;

            var track = _scrollTrack.rect.height;
            var thumb = Mathf.Max(40f, track * _listHeight / _contentHeight);
            _scrollThumb.sizeDelta = new Vector2(4f, thumb);
            _scrollThumb.anchoredPosition = new Vector2(0f, -(track - thumb) * (_scroll / max));
        }

        /// <summary>Scrolls just far enough that the line (and the section title above it) is inside the window.</summary>
        private void EnsureVisible(int index)
        {
            if (_page == null || index < 0) return;

            var entries = _page.Entries;
            var first = index > 0 && entries[index - 1].Header ? index - 1 : index;
            var top = first * _page.Spacing;
            var bottom = index * _page.Spacing + _page.Spacing - 12f;

            if (top < _scrollTarget) _scrollTarget = top;
            else if (bottom > _scrollTarget + _listHeight) _scrollTarget = bottom - _listHeight;
        }

        private void UpdateFocus(float dt)
        {
            if (_page == null || _index < 0 || _index >= _page.Entries.Count)
            {
                if (_focus.Root.gameObject.activeSelf) _focus.Root.gameObject.SetActive(false);
                return;
            }

            var entry = _page.Entries[_index];
            if (!entry.Rect) return;

            var targetPos = (Vector2)entry.Rect.TransformPoint(entry.Rect.rect.center);
            var targetSize = entry.Rect.rect.size + new Vector2(26f, 4f);

            if (!_focusPlaced)
            {
                // The first time, the box starts wide and off to one side and hunts in.
                _focusPlaced = true;
                _focusPos = targetPos + new Vector2(90f, 60f);
                _focusSize = targetSize * 1.7f;
                _focusPosVel = Vector2.zero;
                _focusSizeVel = Vector2.zero;
            }

            const float stiffness = 260f;
            const float damping = 20f;

            _focusPosVel += (targetPos - _focusPos) * (stiffness * dt);
            _focusPosVel *= Mathf.Exp(-damping * dt);
            _focusPos += _focusPosVel * dt;

            _focusSizeVel += (targetSize - _focusSize) * (stiffness * dt);
            _focusSizeVel *= Mathf.Exp(-damping * dt);
            _focusSize += _focusSizeVel * dt;

            // Locked once it has been still and on target for a moment.
            var settled = (targetPos - _focusPos).sqrMagnitude < 4f
                          && (targetSize - _focusSize).sqrMagnitude < 4f
                          && _focusPosVel.sqrMagnitude < 400f;
            _settledFor = settled ? _settledFor + dt : 0f;
            if (!_locked && _settledFor > 0.1f)
            {
                _locked = true;
                Play(lockClip);
            }

            _focus.Root.gameObject.SetActive(true);
            _focus.Root.position = new Vector3(_focusPos.x, _focusPos.y, 0f);
            _focus.Root.sizeDelta = _focusSize;

            if (_locked)
            {
                _focus.SetColor(MenuUi.Ok);
                _afText.color = MenuUi.Ok;
                _afText.text = "AF  OK";
            }
            else
            {
                var blink = 0.55f + 0.45f * Mathf.Sin(_clock * 32f);
                var c = MenuUi.Paper;
                c.a = blink;
                _focus.SetColor(c);
                _afText.color = c;
                _afText.text = "AF  ...";
            }

            // The line the focus is on lights up.
            for (var i = 0; i < _page.Entries.Count; i++)
            {
                var e = _page.Entries[i];
                if (!e.LabelText || e.Header || !e.RightText) continue;

                var selected = i == _index;
                e.LabelText.color = selected ? MenuUi.Paper : MenuUi.Dim;
                e.RightText.color = selected ? MenuUi.Accent : MenuUi.Dim;

                if (e.MeterTicks == null) continue;

                var on = selected ? MenuUi.Accent : MenuUi.Dim;
                for (var t = 0; t < e.MeterTicks.Length; t++) e.MeterTicks[t].color = t < e.MeterFilled ? on : MenuUi.Faint;
            }
        }

        private void UpdateFlash(float dt)
        {
            _flashAlpha = Mathf.MoveTowards(_flashAlpha, 0f, dt * 5f);
            _flash.color = new Color(1f, 1f, 1f, _flashAlpha);
        }

        // ---------- input ----------

        private void HandleInput()
        {
            if (_page == null || _page.Entries.Count == 0) return;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var scrollable = _contentHeight > _listHeight + 1f;

            if (mouse != null)
            {
                var pointer = mouse.position.ReadValue();

                if (mouse.leftButton.wasPressedThisFrame)
                {
                    for (var i = 0; i < _tabRects.Count; i++)
                    {
                        if (!RectTransformUtility.RectangleContainsScreenPoint(_tabRects[i], pointer, null)) continue;

                        SwitchTab(i);
                        return;
                    }
                }

                // The mouse only takes the focus when it actually moves, so the keys are not fought over.
                if ((pointer - _lastMouse).sqrMagnitude > 0.25f)
                {
                    _lastMouse = pointer;
                    for (var i = 0; i < _page.Entries.Count; i++)
                    {
                        if (Over(i, pointer) && i != _index) SetIndex(i, true);
                    }
                }

                var entry = _page.Entries[Mathf.Clamp(_index, 0, _page.Entries.Count - 1)];
                var over = _index >= 0 && Over(_index, pointer);

                if (mouse.leftButton.wasPressedThisFrame && over) Activate();

                if (mouse.rightButton.wasPressedThisFrame)
                {
                    if (over && entry.Adjust != null && !scrollable) Adjust(-1);
                    else Back();
                }

                var scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.1f)
                {
                    // A long page scrolls with the wheel; a short one adjusts the line under the mouse.
                    if (scrollable) { if (InList(pointer)) Move(scroll > 0f ? -1 : 1, false); }
                    else if (over && entry.Adjust != null) Adjust(scroll > 0f ? 1 : -1);
                }
            }

            if (keyboard == null) return;

            if (_page.Tabs.Count > 1)
            {
                if (keyboard.eKey.wasPressedThisFrame || keyboard.pageDownKey.wasPressedThisFrame) SwitchTab((_page.Tab + 1) % _page.Tabs.Count);
                if (keyboard.qKey.wasPressedThisFrame || keyboard.pageUpKey.wasPressedThisFrame) SwitchTab((_page.Tab + _page.Tabs.Count - 1) % _page.Tabs.Count);
            }

            if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) Move(1);
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) Move(-1);
            if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) Adjust(1);
            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) Adjust(-1);
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame) Activate();
            if (keyboard.escapeKey.wasPressedThisFrame) Back();
        }

        private bool InList(Vector2 pointer) => _list && RectTransformUtility.RectangleContainsScreenPoint(_list, pointer, null);

        private bool Over(int index, Vector2 pointer)
        {
            var entry = _page.Entries[index];
            return entry.Rect && !entry.Header && InList(pointer)
                   && RectTransformUtility.RectangleContainsScreenPoint(entry.Rect, pointer, null);
        }

        private void SwitchTab(int tab)
        {
            if (_page == null || tab == _page.Tab || tab < 0 || tab >= _page.Tabs.Count) return;

            _page.Tab = tab;
            Play(hoverClip);
            Flash(0.12f);

            BuildEntries();
            _pageT = 0.5f; // a short fade-in of the new tab
            _index = -1;
            SetIndex(FirstSelectable(), false);
        }

        /// <summary>Next line in that direction, skipping section titles. The keys wrap round; the wheel stops at the ends.</summary>
        private void Move(int direction, bool wrap = true)
        {
            var entries = _page.Entries;
            var count = entries.Count;
            var next = _index;

            for (var n = 0; n < count; n++)
            {
                next += direction;
                if (next < 0 || next >= count)
                {
                    if (!wrap) return;
                    next = (next + count) % count;
                }

                if (entries[next].Header) continue;

                SetIndex(next, true);
                return;
            }
        }

        private void SetIndex(int index, bool sound)
        {
            if (index == _index && _focusPlaced) return;

            _index = index;
            _locked = false;
            _settledFor = 0f;
            _focusSize *= 1.3f; // the lens pulls out of focus for a moment
            EnsureVisible(index);
            if (sound) Play(hoverClip);
        }

        private void Activate()
        {
            if (_page == null || _index < 0) return;

            var entry = _page.Entries[_index];
            if (entry.Select != null)
            {
                Shutter();
                entry.Select();
            }
            else if (entry.Adjust != null)
            {
                Adjust(1);
            }
        }

        private void Adjust(int direction)
        {
            if (_page == null || _index < 0) return;

            var entry = _page.Entries[_index];
            if (entry.Adjust == null) return;

            entry.Adjust(direction);
            entry.Refresh();
            Play(hoverClip);
        }

        private void Back()
        {
            if (_page?.Back == null) return;

            Play(hoverClip);
            _page.Back();
        }

        private void Play(AudioClip clip)
        {
            if (clip && _audio) _audio.PlayOneShot(clip, sfxVolume);
        }
    }
}
