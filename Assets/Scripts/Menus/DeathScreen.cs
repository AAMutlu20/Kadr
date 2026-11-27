using System;
using System.Collections;
using System.Collections.Generic;
using Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Menus
{
    /// <summary>
    /// The debrief after a death, in the look of the main menu. The viewfinder menu supplies the chrome and the two lines
    /// (CONTINUE, MAIN MENU); this adds what is on the desk:
    ///
    ///  - left: the last frame before the black, as a Polaroid that develops slowly,
    ///  - right: the incident report on a sheet of photo paper: cause, time, rooms, kills, frames, what was filed and
    ///    what was forfeited, what was lost in the dark, and the route through the floor with the place of death.
    ///
    /// DeathFlow creates it. Everything is drawn in code; everything runs on unscaled time (the game is frozen).
    /// </summary>
    [RequireComponent(typeof(ViewfinderMenu))]
    public class DeathScreen : MonoBehaviour
    {
        private static readonly Color Ink = new(0.12f, 0.11f, 0.10f, 1f);
        private static readonly Color InkSoft = new(0.12f, 0.11f, 0.10f, 0.55f);
        private static readonly Color InkRule = new(0.12f, 0.11f, 0.10f, 0.16f);
        private static readonly Color AccentInk = new(0.78f, 0.34f, 0.08f, 1f);
        private static readonly Color RedInk = new(0.74f, 0.10f, 0.08f, 1f);

        private const float CardWidth = 700f;
        private const float CardHeight = 840f;
        private const float CardPad = 48f;
        private const float RowHeight = 34f;

        private ViewfinderMenu _menu;
        private RunSummary _summary;
        private Action _onContinue;
        private Action _onMainMenu;
        private bool _chosen;

        private Canvas _canvas;
        private CanvasGroup _root;
        private CanvasGroup _polaroid;
        private CanvasGroup _card;
        private CanvasGroup _stamp;
        private RectTransform _stampRect;
        private RawImage _photo;
        private RectTransform _pulse;
        private Texture2D _lastFrame;

        private float _time;

        // ---------- public ----------

        public void Open(RunSummary summary, Action onContinue, Action onMainMenu)
        {
            _summary = summary;
            _onContinue = onContinue;
            _onMainMenu = onMainMenu;
            _lastFrame = summary.LastFrame;

            _menu = GetComponent<ViewfinderMenu>();
            Build();

            var page = new MenuPage
            {
                Heading = "DEBRIEF   ·   FLOOR " + summary.Floor,
                Compact = true,
                Hint = "UP / DOWN  SELECT      ENTER  CONFIRM"
            };
            page.Entries.Add(new MenuEntry { Label = "CONTINUE", Select = Continue });
            page.Entries.Add(new MenuEntry { Label = "MAIN MENU", Select = MainMenu });
            _menu.Show(page);
        }

        /// <summary>The film winds back: a timecode counts down from the time of the run to zero, then black.</summary>
        public IEnumerator Rewind(float seconds)
        {
            var canvas = MenuUi.MakeCanvas("RewindCanvas", transform, 12);
            var black = MenuUi.Box("Black", canvas.transform, new Color(0f, 0f, 0f, 0f));
            MenuUi.Stretch(black.rectTransform);

            var noise = MenuUi.Picture("Noise", canvas.transform, MenuTextures.Noise, new Color(1f, 1f, 1f, 0.22f));
            MenuUi.Stretch(noise.rectTransform);
            noise.color = new Color(1f, 1f, 1f, 0f);

            var title = MenuUi.Label("Title", canvas.transform, "<<  REWIND", 52, MenuUi.Accent, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            MenuUi.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(900f, 80f));
            var clock = MenuUi.Label("Clock", canvas.transform, _summary.TimeText, 150, MenuUi.Paper, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            MenuUi.Place(clock.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(900f, 200f));
            title.color = new Color(MenuUi.Accent.r, MenuUi.Accent.g, MenuUi.Accent.b, 0f);
            clock.color = new Color(MenuUi.Paper.r, MenuUi.Paper.g, MenuUi.Paper.b, 0f);

            const float fadeIn = 0.35f;
            const float fadeOut = 0.35f;
            var total = fadeIn + seconds + fadeOut;
            var t = 0f;

            while (t < total)
            {
                t += Time.unscaledDeltaTime;

                var cover = Mathf.Clamp01(t / fadeIn);                    // the report goes under the black
                var end = Mathf.Clamp01((total - t) / fadeOut);           // the numbers go out, the black stays
                var run = Mathf.Clamp01((t - fadeIn) / seconds);
                var eased = run * run * (3f - 2f * run);

                black.color = new Color(0f, 0f, 0f, cover);
                noise.color = new Color(1f, 1f, 1f, 0.22f * cover * (0.5f + 0.5f * UnityEngine.Random.value));
                noise.uvRect = new Rect(UnityEngine.Random.value, UnityEngine.Random.value, 1f, 1f);

                var seconds01 = Mathf.RoundToInt(Mathf.Lerp(_summary.Seconds, 0f, eased));
                clock.text = (seconds01 / 60).ToString("00") + ":" + (seconds01 % 60).ToString("00");

                var show = cover * end;
                title.color = new Color(MenuUi.Accent.r, MenuUi.Accent.g, MenuUi.Accent.b, show);
                clock.color = new Color(MenuUi.Paper.r, MenuUi.Paper.g, MenuUi.Paper.b, show);

                yield return null;
            }

            black.color = Color.black;
            noise.color = new Color(1f, 1f, 1f, 0f);
            title.text = "";
            clock.text = "";
        }

        /// <summary>Everything fades to black. Used when leaving for the main menu.</summary>
        public IEnumerator Leave(float seconds)
        {
            _menu.Interactable = false;
            StartCoroutine(_menu.FadeOut(seconds));

            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                _root.alpha = 1f - Mathf.Clamp01(t / seconds);
                yield return null;
            }

            _root.alpha = 0f;
        }

        public void Close()
        {
            _menu.Interactable = false;
        }

        private void OnDestroy()
        {
            if (_lastFrame) Destroy(_lastFrame);
        }

        // ---------- choices ----------

        private void Continue()
        {
            if (_chosen) return;
            _chosen = true;
            _menu.Interactable = false;
            _onContinue?.Invoke();
        }

        private void MainMenu()
        {
            if (_chosen) return;
            _chosen = true;
            _menu.Interactable = false;
            _onMainMenu?.Invoke();
        }

        // ---------- animation ----------

        private void Update()
        {
            if (!_canvas) return;

            _time += Time.unscaledDeltaTime;

            _polaroid.alpha = Ramp(_time, 0.3f, 0.8f);
            _card.alpha = Ramp(_time, 0.7f, 0.8f);

            // The picture develops: black, then up to its normal brightness over a few seconds.
            if (_photo)
            {
                var develop = Mathf.SmoothStep(0.04f, 0.9f, Ramp(_time, 0.6f, 4.5f));
                _photo.color = new Color(develop, develop, develop, 1f);
            }

            // The rubber stamp lands with a little punch.
            var landed = Ramp(_time, 2.0f, 0.12f);
            _stamp.alpha = landed;
            var scale = Mathf.Lerp(1.7f, 1f, landed);
            _stampRect.localScale = new Vector3(scale, scale, 1f);

            if (_pulse)
            {
                var beat = 1f + 0.35f * (0.5f + 0.5f * Mathf.Sin(_time * 4f));
                _pulse.localScale = new Vector3(beat, beat, 1f);
            }
        }

        private static float Ramp(float time, float delay, float duration) => Mathf.Clamp01((time - delay) / duration);

        // ---------- building ----------

        private void Build()
        {
            // Above the viewfinder (10), under the rewind (12).
            _canvas = MenuUi.MakeCanvas("DeathScreenCanvas", transform, 11);
            var rootRect = (RectTransform)_canvas.transform;
            var group = MenuUi.Rect("Root", rootRect);
            MenuUi.Stretch(group);
            _root = group.gameObject.AddComponent<CanvasGroup>();
            _root.interactable = false;
            _root.blocksRaycasts = false;

            BuildPolaroid(group);
            BuildCard(group);
        }

        private void BuildPolaroid(Transform parent)
        {
            // Under the two menu lines, left aligned with them. Reference size 1920 x 1080, from the top left.
            var rect = MenuUi.Rect("Polaroid", parent);
            MenuUi.Place(rect, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(150f + 180f, -(520f + 215f)), new Vector2(360f, 430f));
            rect.localRotation = Quaternion.Euler(0f, 0f, -4f);
            _polaroid = rect.gameObject.AddComponent<CanvasGroup>();

            var shadow = MenuUi.Box("Shadow", rect, new Color(0f, 0f, 0f, 0.4f)).rectTransform;
            MenuUi.Place(shadow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(10f, -12f), new Vector2(360f, 430f));

            var print = MenuUi.Box("Print", rect, new Color(0.95f, 0.94f, 0.89f, 1f));
            MenuUi.Stretch(print.rectTransform);

            if (_lastFrame)
            {
                _photo = MenuUi.Picture("Photo", rect, _lastFrame, new Color(0.04f, 0.04f, 0.04f, 1f));
                // The frame is 16:9. Show the middle square of it.
                var aspect = _lastFrame.width / (float)_lastFrame.height;
                var width = 1f / aspect;
                _photo.uvRect = new Rect((1f - width) * 0.5f, 0f, width, 1f);
            }
            else
            {
                var blank = MenuUi.Box("Photo", rect, new Color(0.15f, 0.15f, 0.15f, 1f));
                MenuUi.Place(blank.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(320f, 320f));
            }

            if (_photo) MenuUi.Place(_photo.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(320f, 320f));

            var caption = MenuUi.Label("Caption", rect, "FRAME " + _summary.FrameNumber.ToString("000"), 28, Ink, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            MenuUi.Place(caption.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 58f), new Vector2(320f, 34f));
            var small = MenuUi.Label("Small", rect, "LAST EXPOSURE", 20, InkSoft, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            MenuUi.Place(small.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(320f, 26f));

            // A strip of tape holding it down.
            var tape = MenuUi.Box("Tape", rect, new Color(0.93f, 0.91f, 0.84f, 0.55f)).rectTransform;
            MenuUi.Place(tape, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(20f, 2f), new Vector2(120f, 36f));
            tape.localRotation = Quaternion.Euler(0f, 0f, 7f);
        }

        private void BuildCard(Transform parent)
        {
            var rect = MenuUi.Rect("Report", parent);
            MenuUi.Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(480f, 0f), new Vector2(CardWidth, CardHeight));
            rect.localRotation = Quaternion.Euler(0f, 0f, -1.1f);
            _card = rect.gameObject.AddComponent<CanvasGroup>();

            var shadow = MenuUi.Box("Shadow", rect, new Color(0f, 0f, 0f, 0.4f)).rectTransform;
            MenuUi.Place(shadow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(12f, -14f), new Vector2(CardWidth, CardHeight));

            var sheet = MenuUi.Box("Sheet", rect, MenuUi.Paper);
            MenuUi.Stretch(sheet.rectTransform);
            var grain = MenuUi.Picture("Grain", rect, MenuTextures.Noise, new Color(0f, 0f, 0f, 0.07f));
            MenuUi.Stretch(grain.rectTransform);

            var s = _summary;

            // Title
            var title = MenuUi.Label("Title", rect, "INCIDENT REPORT", 40, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            MenuUi.Place(title.rectTransform, Top, Top, new Vector2(CardPad, -34f), new Vector2(420f, 52f));
            var roll = MenuUi.Label("Roll", rect, "ROLL 01  ·  FLOOR " + s.Floor, 22, InkSoft, TextAnchor.MiddleRight, FontStyle.Normal, true);
            MenuUi.Place(roll.rectTransform, Top, Top, new Vector2(CardWidth - CardPad - 260f, -40f), new Vector2(260f, 40f));
            Rule(rect, 94f, Ink, 2f);

            // The facts
            var y = 108f;
            Row(rect, ref y, "CAUSE", s.Cause, Ink);
            Row(rect, ref y, "TIME ON FLOOR", s.TimeText, Ink);
            Row(rect, ref y, "ROOMS ENTERED", s.RoomsVisited + " / " + s.RoomsTotal, Ink);
            Row(rect, ref y, "ENEMIES DOWN", s.Kills.ToString(), Ink);
            Row(rect, ref y, "FRAMES SHOT", s.PhotosTaken + "  (" + s.PhotosKept + " kept)", Ink);
            Row(rect, ref y, "XP EARNED", "+" + s.XpGained + "  (kept)", Ink);

            // The money
            y += 10f;
            var assignmentTop = y;
            Header(rect, ref y, "ASSIGNMENT");
            Row(rect, ref y, "PAID PHOTOS", s.PayoutTotal + " Руб.", Ink);
            Row(rect, ref y, "FILED  (" + Mathf.RoundToInt(s.PayoutRate * 100f) + "%)", "+" + s.Filed + " Руб.", Ink);
            Row(rect, ref y, "FORFEITED", s.Forfeited + " Руб.", RedInk);
            Row(rect, ref y, "BALANCE", s.Balance + " Руб.", Ink);

            // The loss
            y += 10f;
            Header(rect, ref y, "LOST IN THE DARK");
            Row(rect, ref y, "CONSUMABLES", "x" + s.ConsumablesLost, s.ConsumablesLost > 0 ? RedInk : Ink);
            Row(rect, ref y, "KEY ITEMS", s.KeyItemsLost.Count.ToString(), s.KeyItemsLost.Count > 0 ? RedInk : Ink);
            if (s.KeyItemsLost.Count > 0)
            {
                var names = MenuUi.Label("Names", rect, string.Join(", ", s.KeyItemsLost).ToUpperInvariant(), 18, InkSoft, TextAnchor.UpperLeft, FontStyle.Normal, true);
                names.horizontalOverflow = HorizontalWrapMode.Wrap;
                MenuUi.Place(names.rectTransform, Top, Top, new Vector2(CardPad, -y), new Vector2(CardWidth - CardPad * 2f, 40f));
                y += 40f;
            }

            // The route
            y += 8f;
            var routeLabel = MenuUi.Label("RouteLabel", rect, "ROUTE", 22, AccentInk, TextAnchor.LowerLeft, FontStyle.Normal, true);
            MenuUi.Place(routeLabel.rectTransform, Top, Top, new Vector2(CardPad, -y), new Vector2(200f, 28f));
            var legend = MenuUi.Label("Legend", rect, "FILLED = ENTERED     RED = DIED HERE", 16, InkSoft, TextAnchor.LowerRight, FontStyle.Normal, true);
            MenuUi.Place(legend.rectTransform, Top, Top, new Vector2(CardWidth - CardPad - 380f, -y), new Vector2(380f, 28f));
            y += 34f;
            BuildRoute(rect, y, CardHeight - 28f - y);

            BuildStamp(rect, assignmentTop + 20f);
        }

        private static readonly Vector2 Top = new(0f, 1f);

        private static void Rule(Transform parent, float y, Color color, float thickness)
        {
            var rule = MenuUi.Box("Rule", parent, color).rectTransform;
            MenuUi.Place(rule, Top, Top, new Vector2(CardPad, -y), new Vector2(CardWidth - CardPad * 2f, thickness));
        }

        private static void Header(Transform parent, ref float y, string text)
        {
            var label = MenuUi.Label("Header " + text, parent, text, 22, AccentInk, TextAnchor.LowerLeft, FontStyle.Normal, true);
            MenuUi.Place(label.rectTransform, Top, Top, new Vector2(CardPad, -y), new Vector2(CardWidth - CardPad * 2f, 30f));
            Rule(parent, y + 32f, AccentInk, 2f);
            y += 40f;
        }

        private static void Row(Transform parent, ref float y, string label, string value, Color valueColor)
        {
            var width = CardWidth - CardPad * 2f;

            var left = MenuUi.Label("Label " + label, parent, label, 22, InkSoft, TextAnchor.MiddleLeft);
            MenuUi.Place(left.rectTransform, Top, Top, new Vector2(CardPad, -y), new Vector2(width, RowHeight));

            var right = MenuUi.Label("Value " + label, parent, value, 24, valueColor, TextAnchor.MiddleRight, FontStyle.Normal, true);
            MenuUi.Place(right.rectTransform, Top, Top, new Vector2(CardPad, -y), new Vector2(width, RowHeight));

            Rule(parent, y + RowHeight - 1f, InkRule, 1f);
            y += RowHeight;
        }

        private void BuildStamp(Transform parent, float y)
        {
            var rect = MenuUi.Rect("Stamp", parent);
            MenuUi.Place(rect, Top, new Vector2(0.5f, 0.5f), new Vector2(CardWidth - 250f, -(y + 70f)), new Vector2(400f, 74f));
            rect.localRotation = Quaternion.Euler(0f, 0f, 9f);
            _stampRect = rect;
            _stamp = rect.gameObject.AddComponent<CanvasGroup>();

            var color = new Color(RedInk.r, RedInk.g, RedInk.b, 0.88f);
            var frame = MenuUi.Frame(rect, "StampFrame", color, 5f);
            MenuUi.Stretch(frame);
            var text = MenuUi.Label("StampText", rect, "INCOMPLETE ASSIGNMENT", 28, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUi.Stretch(text.rectTransform);
        }

        private void BuildRoute(Transform parent, float top, float height)
        {
            var width = CardWidth - CardPad * 2f;
            var area = MenuUi.Rect("Route", parent);
            MenuUi.Place(area, Top, Top, new Vector2(CardPad, -top), new Vector2(width, height));

            var frame = MenuUi.Frame(area, "RouteFrame", InkRule, 2f);
            MenuUi.Stretch(frame);

            var route = _summary.Route;
            if (route.Count == 0) return;

            const float padX = 40f;
            const float padY = 28f;
            var byId = new Dictionary<int, RouteNode>();
            foreach (var node in route) byId[node.Id] = node;

            Vector2 PointOf(RouteNode node) => new(padX + node.X * (width - padX * 2f), padY + node.Y * (height - padY * 2f));

            // Lines first, so the squares sit on top of them.
            foreach (var node in route)
            {
                if (node.Parent < 0 || !byId.TryGetValue(node.Parent, out var parentNode)) continue;

                var main = node.OnCriticalPath && parentNode.OnCriticalPath;
                Line(area, PointOf(parentNode), PointOf(node), main ? 3f : 2f, main ? new Color(Ink.r, Ink.g, Ink.b, 0.75f) : InkSoft);
            }

            foreach (var node in route)
            {
                var p = PointOf(node);
                var size = node.IsBoss || node.Id == 0 ? 22f : 16f;

                var rect = MenuUi.Rect("Node " + node.Id, area);
                MenuUi.Place(rect, Top, new Vector2(0.5f, 0.5f), new Vector2(p.x, -p.y), new Vector2(size, size));
                if (node.IsBoss) rect.localRotation = Quaternion.Euler(0f, 0f, 45f);

                var fill = node.DeathRoom ? RedInk : node.IsBoss ? AccentInk : Ink;
                if (node.Visited || node.DeathRoom)
                {
                    var box = MenuUi.Box("Fill", rect, fill);
                    MenuUi.Stretch(box.rectTransform);
                }
                else
                {
                    var hollow = MenuUi.Frame(rect, "Hollow", node.IsBoss ? AccentInk : InkSoft, 2f);
                    MenuUi.Stretch(hollow);
                }

                if (node.DeathRoom)
                {
                    var ring = MenuUi.Rect("Pulse", rect);
                    MenuUi.Place(ring, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size + 14f, size + 14f));
                    var ringFrame = MenuUi.Frame(ring, "Ring", new Color(RedInk.r, RedInk.g, RedInk.b, 0.7f), 2f);
                    MenuUi.Stretch(ringFrame);
                    _pulse = ring;
                }

                var tag = node.Id == 0 ? "LOBBY" : node.IsBoss ? "BOSS" : null;
                if (tag == null) continue;

                var text = MenuUi.Label("Tag", area, tag, 16, node.IsBoss ? AccentInk : InkSoft, TextAnchor.UpperCenter, FontStyle.Normal, true);
                MenuUi.Place(text.rectTransform, Top, new Vector2(0.5f, 1f), new Vector2(p.x, -(p.y + size * 0.5f + 8f)), new Vector2(90f, 22f));
            }
        }

        private static void Line(Transform parent, Vector2 from, Vector2 to, float thickness, Color color)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length < 0.5f) return;

            var rect = MenuUi.Box("Line", parent, color).rectTransform;
            MenuUi.Place(rect, Top, new Vector2(0f, 0.5f), new Vector2(from.x, -from.y), new Vector2(length, thickness));
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg);
        }
    }
}