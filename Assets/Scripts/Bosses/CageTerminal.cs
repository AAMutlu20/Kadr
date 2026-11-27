using Core;
using Enemies;
using Events;
using Gameplay;
using Items;
using Menus;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bosses
{
    /// <summary>
    /// The computer that opens the boss's cage. Put it on the terminal, with a Collider the interaction ray can hit.
    ///
    /// Looking at it shows the prompt, and the Interact key opens a terminal screen:
    ///  - while threats are left on the floor (ThreatTracker) the cage is quarantined, the text says so and the button is
    ///    grey. Using the terminal then also publishes CageBlockedEvent, which starts the "clear the floor" objective.
    ///  - with no threats left the text says testing may continue and the button is green. Pressing it opens the cage:
    ///    On Cage Opened runs (play the animation, switch the bars off, change the monitor) and the arena begins the fight.
    ///
    /// Set the BossArena to Start On Enter = off, so the fight starts here and not when the player walks in.
    /// </summary>
    public class CageTerminal : MonoBehaviour, IInteractTarget
    {
        [SerializeField] private BossArena arena;
        [SerializeField] private string prompt = "Cage control";
        [SerializeField] private string subjectName = "Subject 01";

        [Header("Screen text")]
        [SerializeField, TextArea] private string quarantinedMany = "Cage is quarantined. Multiple threats detected on the floor. Clear the threats to resume testing.";
        [SerializeField, TextArea] private string quarantinedOne = "Cage is quarantined. A threat is detected on the floor. Clear the threat to resume testing.";
        [SerializeField, TextArea] private string clear = "No threats detected. Testing may continue.";
        [SerializeField, TextArea] private string released = "Cage open. Subject released.";

        [Header("Cage")]
        [Tooltip("Runs once when the cage opens: the door animation, the bars' colliders off, the monitor changing to 'Cage: Open'.")]
        [SerializeField] private UnityEvent onCageOpened;

        public bool IsOpen { get; private set; }
        public int Threats => ThreatTracker.Count;
        public bool CanOpen => !IsOpen && Threats == 0;
        public string SubjectName => subjectName;

        public string Prompt => prompt;

        public string StatusLine => IsOpen ? "CAGE: OPEN" : "CAGE: CLOSED";

        public string Message
        {
            get
            {
                if (IsOpen) return released;

                var count = Threats;
                return count <= 0 ? clear : count == 1 ? quarantinedOne : quarantinedMany;
            }
        }

        public void Interact(Transform interactor)
        {
            // Using the terminal with threats left is what hands the player the "clear the floor" task.
            if (!IsOpen && Threats > 0) Publish(new CageBlockedEvent(Threats));

            CageTerminalScreen.Show(this);
        }

        /// <summary>Opens the cage. Called by the screen's button, and only works with no threats left.</summary>
        public void OpenCage()
        {
            if (!CanOpen) return;

            IsOpen = true;
            onCageOpened?.Invoke();
            if (arena) arena.Begin();

            Publish(new CageOpenedEvent());
        }

        private static void Publish<T>(T evt) where T : struct, IGameEvent
        {
            if (GameServices.TryGet(out EventBus bus)) bus.Publish(evt);
        }
    }

    /// <summary>
    /// The terminal's window. Creates itself the first time a terminal is used, so the scene needs nothing. The game is
    /// paused while it is open. Esc, the Interact key or right click closes it.
    /// </summary>
    public class CageTerminalScreen : MonoBehaviour
    {
        private const float CharsPerSecond = 80f;
        private static readonly Color Green = new(0.55f, 0.92f, 0.45f, 1f);
        private static readonly Color ButtonOn = new(0.2f, 0.5f, 0.27f, 1f);
        private static readonly Color ButtonOnHover = new(0.3f, 0.68f, 0.37f, 1f);
        private static readonly Color ButtonOff = new(0.13f, 0.15f, 0.14f, 1f);

        private static CageTerminalScreen _instance;

        private CageTerminal _terminal;
        private PlayerController _player;
        private Health _health;
        private InventoryView3D _suitcase;

        private GameObject _root;
        private Text _title;
        private Text _body;
        private Text _denied;
        private RectTransform _buttonRect;
        private Image _buttonImage;
        private Text _buttonLabel;

        private bool _open;
        private int _openedFrame;
        private float _savedTimeScale = 1f;
        private string _fullText = "";
        private float _typed;
        private float _deniedUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        public static void Show(CageTerminal terminal)
        {
            if (!_instance) _instance = FindAnyObjectByType<CageTerminalScreen>();
            if (!_instance) _instance = new GameObject("CageTerminalScreen").AddComponent<CageTerminalScreen>();

            _instance.Open(terminal);
        }

        private void Awake()
        {
            Build();
            _root.SetActive(false);
        }

        private void OnDisable()
        {
            if (_open) Close();
        }

        // ---------- open and close ----------

        private void Open(CageTerminal terminal)
        {
            if (_open || !terminal || Time.timeScale == 0f) return;

            _terminal = terminal;
            _player = FindAnyObjectByType<PlayerController>();
            _health = _player ? _player.GetComponentInParent<Health>() : null;
            _suitcase = FindAnyObjectByType<InventoryView3D>();

            _open = true;
            _openedFrame = Time.frameCount;

            // Freeze the world and the player, and keep Tab from opening the suitcase underneath.
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            if (_player) _player.InputEnabled = false;
            if (_suitcase) _suitcase.enabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _root.SetActive(true);
            Refresh();
        }

        private void Close()
        {
            if (!_open) return;

            _open = false;
            _root.SetActive(false);

            Time.timeScale = _savedTimeScale;
            if (_suitcase) _suitcase.enabled = true;

            // A dead player keeps input off, so closing must not switch anything back on.
            var dead = _health && _health.IsDead;
            if (_player && !dead) _player.InputEnabled = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            _terminal = null;
        }

        /// <summary>Rewrites the screen from the terminal's current state and restarts the typing.</summary>
        private void Refresh()
        {
            _title.text = $"CAGE CONTROL   //   {_terminal.SubjectName.ToUpperInvariant()}";
            _fullText = $"{_terminal.StatusLine}\n\n> {_terminal.Message.ToUpperInvariant()}";
            _typed = 0f;
            _denied.text = "";

            _buttonRect.gameObject.SetActive(!_terminal.IsOpen);
            SetButton(false);
        }

        private void SetButton(bool hover)
        {
            var enabledNow = _terminal && _terminal.CanOpen;
            _buttonImage.color = enabledNow ? (hover ? ButtonOnHover : ButtonOn) : ButtonOff;
            _buttonLabel.color = enabledNow ? Color.white : new Color(1f, 1f, 1f, 0.25f);
        }

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
                (kb.escapeKey.wasPressedThisFrame || KadrControls.Pressed(GameAction.Interact) || mouse.rightButton.wasPressedThisFrame))
            {
                Close();
                return;
            }

            // Typewriter, on unscaled time because the game is paused.
            if (_typed < _fullText.Length)
            {
                _typed = Mathf.Min(_fullText.Length, _typed + CharsPerSecond * Time.unscaledDeltaTime);
                _body.text = _fullText.Substring(0, Mathf.FloorToInt(_typed));
            }

            if (_denied.text.Length > 0 && Time.unscaledTime > _deniedUntil) _denied.text = "";

            if (_terminal.IsOpen) return;

            var over = RectTransformUtility.RectangleContainsScreenPoint(_buttonRect, mouse.position.ReadValue(), null);
            SetButton(over);

            if (over && mouse.leftButton.wasPressedThisFrame && Time.frameCount != _openedFrame)
            {
                if (_terminal.CanOpen)
                {
                    _terminal.OpenCage();
                    Close(); // the fight starts: back to the game
                }
                else
                {
                    _denied.text = "ACCESS DENIED";
                    _deniedUntil = Time.unscaledTime + 1.6f;
                }
            }
        }

        // ---------- building the window ----------

        private void Build()
        {
            var canvas = MenuUi.MakeCanvas("CageTerminalCanvas", transform, 100);
            _root = canvas.gameObject;

            var dim = MenuUi.Box("Dim", canvas.transform, new Color(0f, 0f, 0f, 0.74f));
            MenuUi.Stretch(dim.rectTransform);

            var centre = new Vector2(0.5f, 0.5f);
            var panel = MenuUi.Box("Panel", canvas.transform, new Color(0.015f, 0.04f, 0.025f, 0.97f));
            MenuUi.Place(panel.rectTransform, centre, centre, Vector2.zero, new Vector2(1100f, 620f));

            var frame = MenuUi.Frame(panel.transform, "Frame", new Color(Green.r, Green.g, Green.b, 0.55f), 3f);
            MenuUi.Stretch(frame);

            var topLeft = new Vector2(0f, 1f);

            _title = MenuUi.Label("Title", panel.transform, "", 34, Green, TextAnchor.UpperLeft, mono: true);
            MenuUi.Place(_title.rectTransform, topLeft, topLeft, new Vector2(50f, -36f), new Vector2(1000f, 48f));

            var rule = MenuUi.Box("Rule", panel.transform, new Color(Green.r, Green.g, Green.b, 0.35f));
            MenuUi.Place(rule.rectTransform, topLeft, topLeft, new Vector2(50f, -92f), new Vector2(1000f, 2f));

            _body = MenuUi.Label("Body", panel.transform, "", 30, Green, TextAnchor.UpperLeft, mono: true);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.lineSpacing = 1.25f;
            MenuUi.Place(_body.rectTransform, topLeft, topLeft, new Vector2(50f, -126f), new Vector2(1000f, 300f));

            var bottom = new Vector2(0.5f, 0f);
            var button = MenuUi.Box("Button", panel.transform, ButtonOff);
            _buttonImage = button;
            _buttonRect = button.rectTransform;
            MenuUi.Place(_buttonRect, bottom, bottom, new Vector2(0f, 56f), new Vector2(480f, 84f));

            var buttonFrame = MenuUi.Frame(button.transform, "ButtonFrame", new Color(Green.r, Green.g, Green.b, 0.7f), 2f);
            MenuUi.Stretch(buttonFrame);

            _buttonLabel = MenuUi.Label("Label", button.transform, "OPEN CAGE", 34, Color.white, TextAnchor.MiddleCenter, mono: true);
            MenuUi.Stretch(_buttonLabel.rectTransform);

            _denied = MenuUi.Label("Denied", panel.transform, "", 26, new Color(0.95f, 0.35f, 0.3f, 1f), TextAnchor.MiddleCenter, mono: true);
            MenuUi.Place(_denied.rectTransform, bottom, bottom, new Vector2(0f, 150f), new Vector2(600f, 36f));

            var hint = MenuUi.Label("Hint", panel.transform, "[ESC] CLOSE", 22, new Color(Green.r, Green.g, Green.b, 0.5f),
                TextAnchor.LowerLeft, mono: true);
            var bottomLeft = new Vector2(0f, 0f);
            MenuUi.Place(hint.rectTransform, bottomLeft, bottomLeft, new Vector2(50f, 24f), new Vector2(300f, 30f));
        }
    }
}