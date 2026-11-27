using System;
using System.Collections;
using Gameplay;
using Generation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Menus
{
    public enum LoadStyle
    {
        /// <summary>The whole screen: the elevator gauge, the film frames and the typed briefing.</summary>
        Full,
        /// <summary>Black with a small REC. For when the screen is already black (after a death's rewind).</summary>
        Minimal
    }

    /// <summary>
    /// The loading screen and the one place that loads scenes. It lives on its own object (DontDestroyOnLoad), draws on
    /// top of everything (canvas 300) and runs on unscaled time.
    ///
    ///  - Elevator gauge: a needle slides from the old place to the new one as the load really progresses.
    ///  - Film frames: four frames that expose one by one, each when a real stage is done:
    ///    LOADED (the scene is read), ENTERED (it is active), SET (a LevelGenerator in it has built its level, if there is
    ///    one), READY. The frame counter ticks and flashes with each one.
    ///  - A typed line from the Comisar, and a stamp when it is done.
    ///
    /// Use:
    ///   SceneLoader.Load("LevelOne", "FLOOR 1");                 // load, show the screen, fade out when ready
    ///   yield return SceneLoader.LoadAndHold("Lobby", "LOBBY", LoadStyle.Minimal);  // stays black until...
    ///   yield return SceneLoader.Release();                       // ...you are done setting up
    ///
    /// Note: LevelGenerator.Generate() runs in one frame, so the screen holds still for that frame. It is drawn before
    /// the scene is activated, so it never shows a half built level.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        private static SceneLoader _instance;

        private static SceneLoader Instance
        {
            get
            {
                if (_instance) return _instance;

                var go = new GameObject("SceneLoader");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<SceneLoader>();
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        public static bool IsBusy => _instance && _instance._busy;

        /// <summary>Loads a scene with the loading screen and fades out when it is ready. 'onLoaded' runs just before the fade.</summary>
        public static void Load(string scene, string destination = "", LoadStyle style = LoadStyle.Full, Action onLoaded = null,
            string header = "DESCENDING", string origin = "LOBBY", string brief = null)
        {
            if (IsBusy) return;
            var loader = Instance;
            loader.StartCoroutine(loader.Run(scene, destination, style, true, onLoaded, header, origin, brief));
        }

        /// <summary>Loads like Load, but stays on the black screen until Release(). For callers that must set things up first.</summary>
        public static IEnumerator LoadAndHold(string scene, string destination = "", LoadStyle style = LoadStyle.Full,
            string header = "DESCENDING", string origin = "LOBBY", string brief = null)
        {
            if (IsBusy) yield break;

            // The sequence runs on the loader, which survives every scene load. The caller only waits for it: if the
            // caller is destroyed by the load (the old Player, say), the loading screen must not die with it.
            var loader = Instance;
            loader.StartCoroutine(loader.Run(scene, destination, style, false, null, header, origin, brief));
            while (loader && loader._busy && !loader._holding) yield return null;
        }

        /// <summary>Fades the held loading screen away.</summary>
        public static IEnumerator Release(float fadeSeconds = 0.8f)
        {
            if (!_instance || !_instance._holding || _instance._releasing) yield break;
            _instance._releasing = true;
            yield return _instance.FadeOutAndClose(fadeSeconds);
        }

        // ---------- the words ----------

        private static readonly string[] FirstDescent =
        {
            "PHOTOGRAPH WHAT LIVES BELOW. FILE THE FILM. ASK NOTHING.",
            "THE FACILITY PAYS BY THE FRAME. IT DOES NOT PAY FOR EXCUSES.",
            "DO NOT FIRE UNLESS THE FRAME IS WORTH MORE THAN THE BULLET."
        };

        private static readonly string[] LaterDescent =
        {
            "YOU HAVE BEEN HERE BEFORE. THE COMISAR WILL NOT DISCUSS IT.",
            "THE ROOMS ARE NOT THE SAME. THE ASSIGNMENT IS.",
            "INCOMPLETE ASSIGNMENTS ARE PAID IN HALVES. THE OTHER HALF IS NOT OWED.",
            "THE FILM REMEMBERS WHAT YOU WOULD RATHER NOT."
        };

        private static readonly string[] LobbyLines =
        {
            "THE ELEVATOR IS WAITING. IT HAS ALWAYS BEEN WAITING.",
            "REPORT TO THE COMISAR. DO NOT ASK WHY THE CLOCK IS WRONG.",
            "THE LOBBY IS THE SAME AS YOU LEFT IT. THAT IS NOT A COMFORT.",
            "REWIND COMPLETE. PLEASE DO NOT DO THAT AGAIN."
        };

        private static string PickBrief(string destination)
        {
            var deaths = RunProgress.Deaths;
            if (string.Equals(destination, "LOBBY", StringComparison.OrdinalIgnoreCase)) return LobbyLines[deaths % LobbyLines.Length];
            if (deaths == 0) return FirstDescent[RunProgress.FloorsCompleted % FirstDescent.Length];
            return LaterDescent[(deaths - 1) % LaterDescent.Length];
        }

        // ---------- state ----------

        private static readonly string[] FrameNames = { "LOADED", "ENTERED", "SET", "READY" };
        private const float GaugeWidth = 920f;
        private const int Ticks = 21;

        private bool _busy;
        private bool _holding;
        private bool _releasing;
        private float _holdStart;

        /// <summary>If nobody calls Release() this long after a held load is ready, the loader lets go by itself.</summary>
        private const float HoldTimeout = 4f;

        private Canvas _canvas;
        private Image _black;
        private CanvasGroup _content;
        private Image _flash;
        private RectTransform _needle;
        private Text _frameCounter;
        private Text _brief;
        private Text _rec;
        private CanvasGroup _stamp;
        private RectTransform _stampRect;
        private readonly Image[] _frameFill = new Image[4];
        private readonly Text[] _frameLabel = new Text[4];
        private readonly RectTransform[] _frameBox = new RectTransform[4];

        private string _briefText = "";
        private float _typed;
        private float _needleShown;
        private float _needleTarget;
        private float _stampT = -1f;
        private int _stage;
        private float _flashAlpha;
        private float _time;

        // ---------- the sequence ----------

        private IEnumerator Run(string scene, string destination, LoadStyle style, bool autoRelease, Action onLoaded,
            string header, string origin, string brief)
        {
            if (_busy) yield break;
            _busy = true;
            _holding = false;
            _releasing = false;
            _stage = 0;
            _needleShown = _needleTarget = 0f;
            _stampT = -1f;
            _typed = 0f;
            _briefText = string.IsNullOrEmpty(brief) ? PickBrief(destination) : brief.ToUpperInvariant();

            Build(style, destination, header, origin);

            // Into the black. Minimal is already black behind it, so it appears at once.
            if (style == LoadStyle.Full)
            {
                yield return Fade(_black, 0f, 1f, 0.45f);
                yield return FadeGroup(_content, 0f, 1f, 0.35f);
            }
            else
            {
                _black.color = Color.black;
                _content.alpha = 1f;
            }

            var started = Time.unscaledTime;

            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Debug.LogError($"[SceneLoader] Scene \"{scene}\" is not in the Build Settings.", this);
                yield return FadeOutAndClose(0.4f);
                yield break;
            }

            var op = SceneManager.LoadSceneAsync(scene);
            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
            {
                _needleTarget = Mathf.Clamp01(op.progress / 0.9f) * 0.25f;
                yield return null;
            }

            ExposeFrame(1);
            yield return null;
            yield return null; // so the screen is drawn before the scene takes the frame

            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;

            ExposeFrame(2);
            yield return null; // Awake and Start of the new scene run in here

            // A level generator in the new scene builds the level in its Start. Give it a moment to be sure.
            var generator = FindAnyObjectByType<LevelGenerator>();
            if (generator)
            {
                var waited = 0f;
                while (generator && generator.Current == null && waited < 15f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            ExposeFrame(3);

            // Let the NavMesh bake and the spawners settle, and keep the screen up long enough to read.
            var minSeconds = style == LoadStyle.Full ? 2.2f : 0.5f;
            var settle = 0f;
            while (settle < 0.4f || Time.unscaledTime - started < minSeconds)
            {
                settle += Time.unscaledDeltaTime;
                yield return null;
            }

            ExposeFrame(4);
            _stampT = 0f;
            yield return new WaitForSecondsRealtime(style == LoadStyle.Full ? 0.6f : 0.1f);

            _holdStart = Time.unscaledTime;
            _holding = true;
            onLoaded?.Invoke();

            if (autoRelease) yield return FadeOutAndClose(0.8f);
        }

        private IEnumerator FadeOutAndClose(float seconds)
        {
            var t = 0f;
            var black = _black ? _black.color.a : 0f;
            var content = _content ? _content.alpha : 0f;

            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                var k = Mathf.Clamp01(t / seconds);
                if (_black) _black.color = new Color(0f, 0f, 0f, black * (1f - k));
                if (_content) _content.alpha = content * (1f - k);
                yield return null;
            }

            if (_canvas) Destroy(_canvas.gameObject);
            _canvas = null;
            _holding = false;
            _releasing = false;
            _busy = false;
        }

        private static IEnumerator Fade(Image image, float from, float to, float seconds)
        {
            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                image.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds)));
                yield return null;
            }

            image.color = new Color(0f, 0f, 0f, to);
        }

        private static IEnumerator FadeGroup(CanvasGroup group, float from, float to, float seconds)
        {
            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds));
                yield return null;
            }

            group.alpha = to;
        }

        private void ExposeFrame(int number)
        {
            _stage = number;
            _needleTarget = number / 4f;
            _flashAlpha = 0.35f;

            if (_frameCounter) _frameCounter.text = "FRAME " + number.ToString("000");

            var index = number - 1;
            if (_frameFill[index])
            {
                _frameFill[index].color = new Color(MenuUi.Paper.r, MenuUi.Paper.g, MenuUi.Paper.b, 0.9f);
                _frameLabel[index].color = MenuUi.Paper;
            }
        }

        // ---------- animation ----------

        private void Update()
        {
            if (!_canvas) return;

            var dt = Time.unscaledDeltaTime;
            _time += dt;

            // The caller that held the screen is gone (destroyed by the scene load): do not stay black for ever.
            if (_holding && !_releasing && Time.unscaledTime - _holdStart > HoldTimeout)
            {
                _releasing = true;
                StartCoroutine(FadeOutAndClose(0.8f));
            }

            if (_needle)
            {
                // The needle follows the real progress, a little behind it, and creeps forward while it waits.
                var creep = _stage < 4 ? 0.02f : 0f;
                _needleTarget = Mathf.Max(_needleTarget, _stage / 4f);
                _needleShown = Mathf.MoveTowards(_needleShown, Mathf.Min(_needleTarget + creep, 1f), dt * 0.5f);
                _needle.anchoredPosition = new Vector2(_needleShown * GaugeWidth, _needle.anchoredPosition.y);
            }

            if (_brief)
            {
                _typed = Mathf.Min(_briefText.Length, _typed + dt * 40f);
                _brief.text = _briefText.Substring(0, Mathf.FloorToInt(_typed));
            }

            if (_flash)
            {
                _flashAlpha = Mathf.MoveTowards(_flashAlpha, 0f, dt * 1.2f);
                _flash.color = new Color(1f, 1f, 1f, _flashAlpha);
            }

            if (_rec)
            {
                var on = Mathf.Repeat(_time, 1.2f) < 0.7f;
                _rec.color = on ? MenuUi.Rec : new Color(MenuUi.Rec.r, MenuUi.Rec.g, MenuUi.Rec.b, 0.2f);
            }

            if (_stamp && _stampT >= 0f)
            {
                _stampT += dt;
                var landed = Mathf.Clamp01(_stampT / 0.12f);
                _stamp.alpha = landed;
                var scale = Mathf.Lerp(1.7f, 1f, landed);
                _stampRect.localScale = new Vector3(scale, scale, 1f);
            }
        }

        // ---------- building ----------

        private void Build(LoadStyle style, string destination, string header, string origin)
        {
            if (_canvas) Destroy(_canvas.gameObject);

            _canvas = MenuUi.MakeCanvas("LoadingCanvas", transform, 300);
            var root = (RectTransform)_canvas.transform;

            _black = MenuUi.Box("Black", root, new Color(0f, 0f, 0f, 0f));
            MenuUi.Stretch(_black.rectTransform);
            _black.raycastTarget = true; // nothing underneath can be clicked while loading

            var content = MenuUi.Rect("Content", root);
            MenuUi.Stretch(content);
            _content = content.gameObject.AddComponent<CanvasGroup>();
            _content.alpha = 0f;
            _content.blocksRaycasts = false;

            _rec = MenuUi.Label("Rec", content, "REC", 30, MenuUi.Rec, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            MenuUi.Place(_rec.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(112f, -92f), new Vector2(200f, 40f));

            _frameCounter = MenuUi.Label("Frame", content, "FRAME 000", 26, MenuUi.Paper, TextAnchor.LowerRight, FontStyle.Normal, true);
            MenuUi.Place(_frameCounter.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-112f, 96f), new Vector2(400f, 36f));

            if (style == LoadStyle.Minimal)
            {
                var loading = MenuUi.Label("Loading", content, "LOADING", 26, MenuUi.Dim, TextAnchor.LowerLeft, FontStyle.Normal, true);
                MenuUi.Place(loading.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(112f, 96f), new Vector2(400f, 36f));
            }
            else
            {
                BuildFull(content, destination, header, origin);
            }

            // The shutter flash on top of the content.
            _flash = MenuUi.Box("Flash", content, new Color(1f, 1f, 1f, 0f));
            MenuUi.Stretch(_flash.rectTransform);
        }

        private void BuildFull(Transform parent, string destination, string header, string origin)
        {
            var topLeft = new Vector2(0f, 1f);
            var place = string.IsNullOrEmpty(destination) ? "BELOW" : destination.ToUpperInvariant();

            var headerText = MenuUi.Label("Header", parent, string.IsNullOrEmpty(header) ? "DESCENDING" : header.ToUpperInvariant(), 26, MenuUi.Accent, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            MenuUi.Place(headerText.rectTransform, topLeft, topLeft, new Vector2(150f, -170f), new Vector2(700f, 36f));

            var big = MenuUi.Label("Destination", parent, place, 150, MenuUi.Paper, TextAnchor.UpperLeft, FontStyle.Bold);
            MenuUi.Place(big.rectTransform, topLeft, topLeft, new Vector2(150f, -210f), new Vector2(1300f, 200f));

            // The elevator gauge: a ruler with the lobby at one end and the destination at the other.
            var ruler = MenuUi.Rect("Gauge", parent);
            MenuUi.Place(ruler, topLeft, topLeft, new Vector2(150f, -520f), new Vector2(GaugeWidth, 80f));

            var line = MenuUi.Box("Line", ruler, MenuUi.Dim).rectTransform;
            MenuUi.Place(line, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -40f), new Vector2(GaugeWidth, 2f));
            for (var i = 0; i < Ticks; i++)
            {
                var major = i % 5 == 0;
                var tick = MenuUi.Box("Tick", ruler, major ? MenuUi.Paper : MenuUi.Dim).rectTransform;
                MenuUi.Place(tick, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                    new Vector2(i * GaugeWidth / (Ticks - 1), -40f), new Vector2(2f, major ? 30f : 14f));
            }

            var from = MenuUi.Label("From", ruler, string.IsNullOrEmpty(origin) ? "LOBBY" : origin.ToUpperInvariant(), 22, MenuUi.Dim, TextAnchor.UpperLeft, FontStyle.Normal, true);
            MenuUi.Place(from.rectTransform, topLeft, topLeft, new Vector2(0f, -66f), new Vector2(300f, 30f));
            var to = MenuUi.Label("To", ruler, place, 22, MenuUi.Paper, TextAnchor.UpperRight, FontStyle.Normal, true);
            MenuUi.Place(to.rectTransform, topLeft, topLeft, new Vector2(GaugeWidth - 400f, -66f), new Vector2(400f, 30f));

            var needle = MenuUi.Box("Needle", ruler, MenuUi.Accent).rectTransform;
            MenuUi.Place(needle, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(4f, 76f));
            _needle = needle;

            // The four frames of film. They expose one by one.
            for (var i = 0; i < 4; i++)
            {
                var box = MenuUi.Rect("Frame " + (i + 1), parent);
                MenuUi.Place(box, topLeft, topLeft, new Vector2(150f + i * 215f, -680f), new Vector2(190f, 118f));
                _frameBox[i] = box;

                _frameFill[i] = MenuUi.Box("Fill", box, new Color(MenuUi.Paper.r, MenuUi.Paper.g, MenuUi.Paper.b, 0.04f));
                MenuUi.Stretch(_frameFill[i].rectTransform);
                var outline = MenuUi.Frame(box, "Outline", MenuUi.Dim, 2f);
                MenuUi.Stretch(outline);

                var number = MenuUi.Label("Number", box, (i + 1).ToString("00"), 40, MenuUi.Dim, TextAnchor.UpperLeft, FontStyle.Normal, true);
                MenuUi.Place(number.rectTransform, topLeft, topLeft, new Vector2(12f, -8f), new Vector2(100f, 50f));

                _frameLabel[i] = MenuUi.Label("Name", box, FrameNames[i], 22, MenuUi.Dim, TextAnchor.LowerLeft, FontStyle.Normal, true);
                MenuUi.Place(_frameLabel[i].rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 8f), new Vector2(170f, 30f));
            }

            // The Comisar's line, typed.
            var tag = MenuUi.Label("BriefTag", parent, "FROM THE COMISAR", 22, MenuUi.Accent, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            MenuUi.Place(tag.rectTransform, topLeft, topLeft, new Vector2(150f, -850f), new Vector2(700f, 30f));

            _brief = MenuUi.Label("Brief", parent, "", 34, MenuUi.Paper, TextAnchor.UpperLeft);
            _brief.horizontalOverflow = HorizontalWrapMode.Wrap;
            MenuUi.Place(_brief.rectTransform, topLeft, topLeft, new Vector2(150f, -886f), new Vector2(1300f, 90f));

            // The stamp, top right, lands when everything is ready.
            var stamp = MenuUi.Rect("Stamp", parent);
            MenuUi.Place(stamp, topLeft, new Vector2(0.5f, 0.5f), new Vector2(1500f, -250f), new Vector2(330f, 68f));
            stamp.localRotation = Quaternion.Euler(0f, 0f, -8f);
            _stampRect = stamp;
            _stamp = stamp.gameObject.AddComponent<CanvasGroup>();
            _stamp.alpha = 0f;

            var color = new Color(MenuUi.Rec.r, MenuUi.Rec.g, MenuUi.Rec.b, 0.85f);
            var frame = MenuUi.Frame(stamp, "StampFrame", color, 4f);
            MenuUi.Stretch(frame);
            var text = MenuUi.Label("StampText", stamp, "CONFIDENTIAL", 36, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            MenuUi.Stretch(text.rectTransform);
        }
    }
}