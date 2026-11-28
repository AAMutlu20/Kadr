using System.Collections.Generic;
using Core;
using Events;
using Gameplay;
using Items;
using Menus;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Weapons;

namespace Hud
{
    /// <summary>
    /// The in-game HUD. Put it on one empty GameObject in the gameplay scene. Everything is built in code.
    ///
    /// Minimal on purpose: cream type on the dark, red only for danger, nothing in the middle of the screen except the
    /// interact prompt.
    ///   Bottom left   a scrolling pulse trace instead of a health number (it speeds up and turns ragged and red as health
    ///                 drops, and goes flat when dead), with a thin stamina line under it that only shows when it matters.
    ///   Bottom right  magazine and reserve, a thin line for the magazine fill, RELOAD when empty. While the flashlight is on
    ///                 the same spot shows the battery instead.
    ///   Crosshair     a dot with ticks (pistol), four arcs the size of the pellet cone (shotgun), a tiny ring (weapon
    ///                 lowered), nothing with the flashlight on. Stamina is a short line under it.
    ///   Top left      the current task, typed in, held for a few seconds, then gone. J shows it again.
    ///   Centre        "[ E ]  Open cage" under the crosshair, and short notices below it.
    ///   Right         pickups, typed out one line each.
    ///   Screen edges  darken when hurt or low on health.
    /// Pulse and ammo sit at a low opacity while nothing is happening, and come back on damage, shooting or reloading.
    /// The whole HUD hides while the game is paused (Time.timeScale 0: the suitcase, the pause menu).
    ///
    /// It replaces the old InteractPromptView, PistolAmmoPanel, FlashlightBar and StaminaBar, which are deleted.
    /// Health is read straight from the player's Health, like InventoryHud does, and drops in it count as hits.
    /// </summary>
    public class KadrHud : MonoBehaviour
    {
        [Header("References (empty = found automatically)")]
        [SerializeField] private Health playerHealth;
        [SerializeField] private PlayerStamina stamina;
        [SerializeField] private WeaponController weapons;
        [SerializeField] private Flashlight flashlight;

        [Header("Look")]
        [Tooltip("Ammo and torch while nothing is happening.")]
        [SerializeField, Range(0f, 1f)] private float idleAlpha = 0.3f;
        [Tooltip("The pulse while health is fine.")]
        [SerializeField, Range(0f, 1f)] private float pulseIdleAlpha = 0.6f;
        [Tooltip("Also writes the health number next to PULSE (\"PULSE 78\"), or \"--\" if no player Health was found. Handy for checking.")]
        [SerializeField] private bool showHealthValue;

        [Header("Behaviour")]
        [Tooltip("Seconds the pulse and ammo stay bright after something happens.")]
        [SerializeField] private float activeSeconds = 4f;
        [SerializeField] private float objectiveSeconds = 6f;
        [SerializeField] private float toastSeconds = 3.5f;
        [SerializeField] private float noticeSeconds = 2.5f;

        [Header("First task (empty = none)")]
        [SerializeField] private string startTitle = "TASK 01";
        [SerializeField] private string startText = "Find the documents";

        [Header("Heartbeat (optional)")]
        [Tooltip("One beat. Played with each pulse spike while health is low.")]
        [SerializeField] private AudioClip heartbeatClip;
        [SerializeField, Range(0f, 1f)] private float heartbeatVolume = 0.6f;

        /// <summary>The first task, so the suitcase's orders panel can list it too.</summary>
        public string StartTitle => startTitle;
        public string StartText => startText;

        /// <summary>For loading a save: the first task would be wrong now, so it is never shown.</summary>
        public void SkipStartTask() => _startDelay = -1f;

        private static readonly Color Cream = new(0.85f, 0.82f, 0.72f, 1f);
        private static readonly Color Grey = new(0.56f, 0.54f, 0.47f, 1f);
        private static readonly Color Rule = new(0.29f, 0.29f, 0.25f, 1f);
        private static readonly Color Red = new(0.70f, 0.20f, 0.16f, 1f);
        private static readonly Color Amber = new(0.66f, 0.45f, 0.18f, 1f);
        private static readonly Color Blue = new(0.44f, 0.63f, 0.78f, 1f);

        private const float Margin = 72f;
        private const float TraceWidth = 320f;
        private const float TraceSpeed = 100f; // pixels per second
        private const float RPeak = 0.34f;     // where in the beat the tall spike is
        private const float StaminaWidth = 170f;
        private const float TorchIdleAlpha = 0.45f;
        private const int HealthTicks = 20; // one tick per 5% of health
        private const int PipMax = 24;      // magazines up to this size are drawn as one pip per round

        // ---------- ui ----------
        private CanvasGroup _root;
        private CanvasGroup _pulseGroup;
        private CanvasGroup _staminaGroup;
        private CanvasGroup _ammoGroup;
        private CanvasGroup _objectiveGroup;
        private TraceGraphic _trace;
        private Text _pulseLabel;
        private Text _bpmLabel;
        private readonly Image[] _healthTicks = new Image[HealthTicks];
        private readonly float[] _tickFlash = new float[HealthTicks];
        private int _tickShown = HealthTicks;
        private float _shake;
        private RectTransform _pipsRoot;
        private readonly Image[] _pips = new Image[PipMax];
        private readonly float[] _pipFlash = new float[PipMax];
        private int _pipCount = -1;
        private int _pipMag = -1;
        private RectTransform _staminaFill;
        private Image _staminaFillImage;
        private Text _ammoLabel;
        private Text _ammoNumber;
        private Text _reload;
        private string _reloadKey;
        private RectTransform _ammoFill;
        private Image _ammoFillImage;
        private RectTransform _rule;
        private RectTransform _cellsRoot;
        private readonly Image[] _cells = new Image[10];
        private Canvas _hudCanvas;
        private CanvasGroup _xhPistol;
        private CanvasGroup _xhShotgun;
        private CanvasGroup _xhLowered;
        private readonly RectTransform[] _ticks = new RectTransform[4];
        private RingGraphic _arcs;
        private Text _objectiveTitleText;
        private Text _objectiveBody;
        private Image _objectiveLine;
        private Text _prompt;
        private Text _notice;
        private RectTransform _toastRoot;
        private RawImage _edges;
        private AudioSource _audio;
        private Texture2D _vignette;

        private readonly List<Toast> _toasts = new();

        private class Toast
        {
            public Text Text;
            public string Full;
            public float Age;
        }

        // ---------- state ----------
        private EventBus _bus;
        private float _findTimer;

        private float _prevHealth = -1f;
        private float _fraction = 1f;
        private bool _dead;
        private float _pulseActive;
        private float _hitFlash;
        private float _beatPulse;

        private PlayerPulse pulse;
        private float _bpm = 64f;
        private float _phase;
        private float _beatAmp = 1f;
        private float _beatRate = 1f;
        private float _sampleAcc;

        private WeaponBase _lastWeapon;
        private int _lastMag = -1;
        private int _lastReserve = -1;
        private int _reserve;
        private float _reserveTimer;
        private float _ammoActive;

        private enum SlotMode { None, Ammo, Torch }
        private SlotMode _slotMode;
        private bool _cameraRaised;
        private WeaponBase _kickWeapon;
        private int _kickMag = -1;
        private float _kick;
        private float _gap = 10f;
        private float _radius = 24f;
        private int _findTries;
        private AmmoKind _lastKind;

        private string _objTitle = "";
        private string _objBody = "";
        private bool _objCompleted;
        private float _objTimer;
        private float _objTyped;
        private float _startDelay = 1.5f;

        private bool _promptVisible;
        private string _promptText = "";
        private float _promptTyped;
        private string _noticeText = "";
        private float _noticeTimer;
        private float _noticeTyped;

        // ---------- setup ----------

        private void Awake()
        {
            Build();
        }

        private void OnEnable()
        {
            if (!GameServices.TryGet(out _bus)) return;

            _bus.Subscribe<InteractPromptEvent>(OnPrompt);
            _bus.Subscribe<NoticeEvent>(OnNotice);
            _bus.Subscribe<ItemCollectedEvent>(OnCollected);
            _bus.Subscribe<ObjectiveEvent>(OnObjective);
            _bus.Subscribe<CameraRaisedEvent>(OnCameraRaised);
        }

        private void OnDisable()
        {
            if (_bus == null) return;

            _bus.Unsubscribe<InteractPromptEvent>(OnPrompt);
            _bus.Unsubscribe<NoticeEvent>(OnNotice);
            _bus.Unsubscribe<ItemCollectedEvent>(OnCollected);
            _bus.Unsubscribe<ObjectiveEvent>(OnObjective);
            _bus.Unsubscribe<CameraRaisedEvent>(OnCameraRaised);
            _bus = null;
        }

        private void OnDestroy()
        {
            if (_vignette) Destroy(_vignette);
        }

        private void FindPlayer()
        {
            var player = FindAnyObjectByType<PlayerController>();

            if (!playerHealth && player)
            {
                playerHealth = player.GetComponentInChildren<Health>();
                if (!playerHealth) playerHealth = player.GetComponentInParent<Health>();
            }

            if (!stamina && player) stamina = player.GetComponentInChildren<PlayerStamina>();
            if (!weapons) weapons = FindAnyObjectByType<WeaponController>();

            if (!flashlight) flashlight = FindAnyObjectByType<Flashlight>();
            if (!pulse) pulse = PlayerPulse.Instance;
            if (!pulse && player) pulse = PlayerPulse.Of(player.gameObject);
        }

        // ---------- events ----------

        private void OnPrompt(InteractPromptEvent e)
        {
            if (e.Visible && (!_promptVisible || e.Text != _promptText)) _promptTyped = 0f;

            _promptVisible = e.Visible;
            if (e.Visible) _promptText = e.Text;
        }

        private void OnCameraRaised(CameraRaisedEvent e) => _cameraRaised = e.Raised;

        private void OnNotice(NoticeEvent e)
        {
            _noticeText = e.Message;
            _noticeTimer = noticeSeconds;
            _noticeTyped = 0f;
        }

        private void OnCollected(ItemCollectedEvent e)
        {
            var label = e.Count > 1 ? $"+ {e.DisplayName}  x{e.Count}" : $"+ {e.DisplayName}";
            AddToast(label);
        }

        private void OnObjective(ObjectiveEvent e)
        {
            _objTitle = string.IsNullOrEmpty(e.Title) ? "TASK" : e.Title;
            _objBody = e.Text ?? "";
            _objCompleted = e.Completed;
            _objTimer = objectiveSeconds;
            _objTyped = 0f;
        }

        // ---------- every frame ----------

        private void Start()
        {
            _trace.Init(Mathf.RoundToInt(TraceWidth), 110);
            for (var i = 0; i < Mathf.RoundToInt(TraceWidth); i++) _trace.Push(0f);

            FindPlayer();

            if (!string.IsNullOrEmpty(startText)) _startDelay = 1.5f;
            else _startDelay = -1f;
        }

        private void Update()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            var paused = Time.timeScale < 0.01f;

            _root.alpha = Mathf.MoveTowards(_root.alpha, paused ? 0f : 1f, dt * 6f);
            if (paused)
            {
                _edges.color = new Color(0f, 0f, 0f, 0f);
                return;
            }

            if (!playerHealth || !stamina || !weapons || !pulse)
            {
                _findTimer -= dt;
                if (_findTimer <= 0f)
                {
                    _findTimer = 1f;
                    FindPlayer();

                    if (++_findTries == 4 && !playerHealth) Debug.LogWarning("[KadrHud] No player Health found. Assign it in the inspector.", this);
                    if (_findTries == 4 && !weapons) Debug.LogWarning("[KadrHud] No WeaponController found. Assign it in the inspector.", this);
                }
            }

            if (_startDelay > 0f)
            {
                _startDelay -= dt;
                if (_startDelay <= 0f) OnObjective(new ObjectiveEvent(startTitle, startText));
            }

            ReadPlayer(dt);
            UpdatePulse(dt);
            UpdateStamina(dt);
            UpdateSlot(dt);
            UpdateCrosshair(dt);
            UpdateObjective(dt);
            UpdatePrompt(dt);
            UpdateToasts(dt);
            UpdateEdges(dt);
        }

        private void ReadPlayer(float dt)
        {
            _pulseActive = Mathf.Max(0f, _pulseActive - dt);
            _hitFlash = Mathf.MoveTowards(_hitFlash, 0f, dt * 1.8f);
            _beatPulse = Mathf.MoveTowards(_beatPulse, 0f, dt * 3f);

            if (!playerHealth) return;

            var current = playerHealth.Current;
            var max = Mathf.Max(1f, playerHealth.Max);
            _fraction = Mathf.Clamp01(current / max);
            _dead = playerHealth.IsDead;

            if (_prevHealth >= 0f)
            {
                var delta = _prevHealth - current;
                if (delta > 0.01f)
                {
                    _pulseActive = activeSeconds;
                    _hitFlash = Mathf.Clamp01(delta / max * 4f);

                    // A hit jolts the trace at once: an extra beat right now, a shake, and the heart rate jumps.
                    _shake = Mathf.Min(1f, 0.3f + delta / max * 4f);
                    _phase = Mathf.Min(_phase, 0.26f);
                    _beatAmp = 1.2f;
                }
                else if (delta < -0.01f)
                {
                    _pulseActive = activeSeconds;
                    _beatPulse = 1f;
                }
            }

            _prevHealth = current;
        }

        // ---------- pulse ----------

        private void UpdatePulse(float dt)
        {
            var f = _fraction;
            var buffs = PlayerBuffs.Instance;
            var drunk = buffs ? buffs.Drunk : 0f;

            // The heart rate itself lives in PlayerPulse (it also shakes the pistol's aim). The HUD only draws it.
            _bpm = pulse ? pulse.Bpm : _dead ? 0f : 64f;

            _shake = Mathf.MoveTowards(_shake, 0f, dt * 2.2f);
            var ragged = _dead ? 0f : Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.1f, 0.5f, f));

            _sampleAcc += dt * TraceSpeed;
            var guard = 0;
            while (_sampleAcc >= 1f && guard++ < 12)
            {
                _sampleAcc -= 1f;

                var step = 1f / TraceSpeed;
                var previous = _phase;
                _phase += step * _bpm / 60f * _beatRate;

                if (_phase >= 1f)
                {
                    _phase -= 1f;
                    previous -= 1f;
                    _beatAmp = Mathf.Lerp(1f, Random.Range(0.5f, 1.15f), ragged);
                    _beatRate = Mathf.Lerp(1f, Random.Range(0.7f, 1.3f), ragged);
                }

                if (previous < RPeak && _phase >= RPeak && !_dead) OnBeat();

                var value = 0f;
                if (!_dead)
                {
                    value = Ecg(_phase) * _beatAmp;
                    value += (Random.value - 0.5f) * 0.14f * ragged;
                    value += (Random.value - 0.5f) * 1.6f * _shake;
                    value += Mathf.Sin(Time.unscaledTime * 1.4f) * 0.2f * drunk;
                }

                _trace.Push(value * 0.8f);
            }

            var danger = _dead ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.25f, f));
            var colour = Color.Lerp(Cream, Red, danger);
            _trace.color = Color.Lerp(colour, Color.white, 0.35f * _beatPulse); // brightens with every beat
            _trace.HeadPulse = _beatPulse;

            _pulseLabel.color = Color.Lerp(Grey, Red, danger);
            var healthText = showHealthValue ? (playerHealth ? "  " + Mathf.CeilToInt(playerHealth.Current) : "  --") : "";
            _pulseLabel.text = (_dead ? "NO PULSE" : "PULSE") + healthText;

            _bpmLabel.text = _dead ? "--" : Mathf.RoundToInt(_bpm) + " BPM";
            _bpmLabel.color = _bpm > 120f ? Red : Grey;

            UpdateHealthTicks(dt, f, danger);

            var bright = _dead || f < 0.5f || _pulseActive > 0f || drunk > 0.05f;
            _pulseGroup.alpha = Mathf.MoveTowards(_pulseGroup.alpha, bright ? 1f : pulseIdleAlpha, dt * 3f);
        }

        /// <summary>
        /// A ruler of twenty ticks under the trace, one per 5% of health. Lost ticks flash red and burn out; the ticks are the
        /// exact reading that the trace does not give.
        /// </summary>
        private void UpdateHealthTicks(float dt, float f, float danger)
        {
            var shown = _dead ? 0 : Mathf.Clamp(Mathf.CeilToInt(f * HealthTicks - 0.001f), 0, HealthTicks);

            if (shown < _tickShown)
            {
                for (var i = shown; i < _tickShown; i++) _tickFlash[i] = 1f;
            }

            _tickShown = shown;

            var on = Color.Lerp(Cream, Red, danger);
            for (var i = 0; i < HealthTicks; i++)
            {
                var colour = i < shown ? on : Rule;

                if (_tickFlash[i] > 0f)
                {
                    _tickFlash[i] = Mathf.MoveTowards(_tickFlash[i], 0f, dt * 1.6f);
                    colour = Color.Lerp(colour, _tickFlash[i] > 0.7f ? Color.white : Red, _tickFlash[i]);
                }

                _healthTicks[i].color = colour;
            }
        }

        private void OnBeat()
        {
            _beatPulse = 1f;

            if (!heartbeatClip || !_audio) return;

            var weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.2f, _fraction));
            if (weight < 0.02f) return;

            _audio.pitch = Mathf.Clamp(_bpm / 80f, 0.9f, 1.3f);
            _audio.PlayOneShot(heartbeatClip, heartbeatVolume * weight);
        }

        /// <summary>One heartbeat on an ECG: a small P wave, a Q dip, the tall R spike, an S dip and a T wave. p is 0..1.</summary>
        private static float Ecg(float p)
        {
            return Gauss(p, 0.18f, 0.045f, 0.12f)
                   + Gauss(p, 0.31f, 0.016f, -0.15f)
                   + Gauss(p, RPeak, 0.02f, 1f)
                   + Gauss(p, 0.37f, 0.02f, -0.3f)
                   + Gauss(p, 0.60f, 0.06f, 0.25f);
        }

        private static float Gauss(float x, float centre, float width, float amplitude)
        {
            var d = (x - centre) / width;
            return amplitude * Mathf.Exp(-d * d);
        }

        // ---------- stamina ----------

        private void UpdateStamina(float dt)
        {
            if (!stamina)
            {
                _staminaGroup.alpha = 0f;
                return;
            }

            var buffs = PlayerBuffs.Instance;
            var boosted = buffs && buffs.InfiniteStamina;
            var value = boosted ? buffs.InfiniteFraction : stamina.Fraction;
            var show = boosted || value < 0.999f || stamina.IsExhausted;

            _staminaGroup.alpha = Mathf.MoveTowards(_staminaGroup.alpha, show ? 1f : 0f, dt * 4f);
            _staminaFill.sizeDelta = new Vector2(StaminaWidth * Mathf.Clamp01(value), 3f); // shrinks towards the middle
            _staminaFillImage.color = boosted ? Blue : stamina.IsExhausted ? Red : Cream;
        }

        // ---------- bottom right: ammo, or the torch while it is on ----------

        private void UpdateSlot(float dt)
        {
            var weapon = weapons ? weapons.Current as WeaponBase : null;
            var torch = flashlight && flashlight.Owned && flashlight.IsOn;

            var mode = _cameraRaised || _dead ? SlotMode.None
                : torch ? SlotMode.Torch
                : weapon != null ? SlotMode.Ammo
                : SlotMode.None;

            if (mode != _slotMode)
            {
                _slotMode = mode;
                _ammoActive = activeSeconds;
                _ammoGroup.alpha = 0f; // the new readout fades in
            }

            if (mode == SlotMode.None)
            {
                _ammoGroup.alpha = Mathf.MoveTowards(_ammoGroup.alpha, 0f, dt * 4f);
                return;
            }

            _ammoActive = Mathf.Max(0f, _ammoActive - dt);

            if (mode == SlotMode.Torch)
            {
                var charge = Mathf.Clamp01(flashlight.Charge01);
                var low = charge <= 0.3f;

                _ammoLabel.text = "TORCH";
                _ammoLabel.color = low ? Red : Grey;
                _ammoNumber.gameObject.SetActive(false);
                _rule.gameObject.SetActive(false);
                _pipsRoot.gameObject.SetActive(false);
                _cellsRoot.gameObject.SetActive(true);
                _reload.gameObject.SetActive(false);

                var filled = Mathf.CeilToInt(charge * _cells.Length - 0.001f);
                for (var i = 0; i < _cells.Length; i++) _cells[i].color = i < filled ? (low ? Red : Cream) : Rule;

                var torchAlpha = _ammoActive > 0f || low ? 1f : TorchIdleAlpha;
                if (charge <= 0.12f && Mathf.Sin(Time.unscaledTime * 8f) < 0f) torchAlpha *= 0.35f;
                _ammoGroup.alpha = Mathf.MoveTowards(_ammoGroup.alpha, torchAlpha, dt * 4f);
                return;
            }

            _ammoNumber.gameObject.SetActive(true);
            _rule.gameObject.SetActive(true);
            _cellsRoot.gameObject.SetActive(false);

            var reserveSource = AmmoReserve.Instance;
            var kind = reserveSource ? reserveSource.Selected : AmmoKind.Normal;

            _reserveTimer -= dt;
            if (reserveSource && _reserveTimer <= 0f)
            {
                _reserveTimer = 0.25f;
                _reserve = reserveSource.Count(kind);
            }

            var mag = weapon.Ammo;
            var max = Mathf.Max(1, weapon.MaxAmmo);

            if (weapon != _lastWeapon || mag != _lastMag || _reserve != _lastReserve || kind != _lastKind || weapon.IsBusy) _ammoActive = activeSeconds;
            _lastKind = kind;
            if (weapon != _lastWeapon) _pipMag = -1;
            _lastWeapon = weapon;
            _lastMag = mag;
            _lastReserve = _reserve;

            // The pistol can have one kind loaded while another is selected for the next reload.
            var pistol = weapon as PistolStrategy;
            var loaded = pistol != null ? pistol.LoadedKind : AmmoKind.Normal;
            var loadedFire = pistol != null && loaded == AmmoKind.Incendiary;
            var selectedFire = reserveSource && kind == AmmoKind.Incendiary;

            var label = weapon.Name.ToUpperInvariant();
            if (loadedFire) label += "  /  INCENDIARY";
            if (pistol != null && reserveSource && kind != loaded) label += "   NEXT: " + (selectedFire ? "INCENDIARY" : "NORMAL");
            _ammoLabel.text = label;
            _ammoLabel.color = loadedFire ? Amber : Grey;

            var numberColour = mag == 0 ? "#B3342A" : loadedFire ? "#C98A2E" : "#D9D2B8";
            var reserveColour = selectedFire ? "#A8742F" : "#8F8A78";
            var reserveText = reserveSource ? $"<size=34><color={reserveColour}>  / {_reserve}</color></size>" : "";
            _ammoNumber.text = $"<color={numberColour}>{mag:00}</color>{reserveText}";

            _ammoFill.sizeDelta = new Vector2(TraceWidth * 1.125f * Mathf.Clamp01(mag / (float)max), 0f);
            _ammoFillImage.color = Cream;
            UpdatePips(mag, max, dt);

            var showReload = mag == 0 && !weapon.IsBusy && !weapon.IsDepleted;
            _reload.gameObject.SetActive(showReload);
            if (showReload)
            {
                var keyName = KadrControls.Name(GameAction.Reload);
                if (!ReferenceEquals(keyName, _reloadKey))
                {
                    _reloadKey = keyName;
                    _reload.text = "RELOAD   [ " + keyName + " ]";
                }

                var blink = Mathf.Sin(Time.unscaledTime * 6f) > -0.3f;
                _reload.color = blink ? Red : new Color(Red.r, Red.g, Red.b, 0.3f);
            }

            var bright = _ammoActive > 0f || mag == 0 || mag <= max * 0.25f;
            _ammoGroup.alpha = Mathf.MoveTowards(_ammoGroup.alpha, bright ? 1f : idleAlpha, dt * 3f);
        }

        /// <summary>One pip per round. A fired round's pip flashes white and burns out.</summary>
        private void UpdatePips(int mag, int max, float dt)
        {
            var usePips = max <= PipMax;
            _rule.gameObject.SetActive(!usePips);
            _pipsRoot.gameObject.SetActive(usePips);
            if (!usePips) return;

            if (max != _pipCount)
            {
                _pipCount = max;
                const float gap = 5f;
                var width = (360f - gap * (max - 1)) / max;

                for (var i = 0; i < PipMax; i++)
                {
                    var active = i < max;
                    _pips[i].gameObject.SetActive(active);
                    if (!active) continue;

                    Place(_pips[i].rectTransform, Vector2.zero, Vector2.zero, new Vector2(i * (width + gap), 0f), new Vector2(width, 10f));
                }
            }

            if (_pipMag >= 0 && mag < _pipMag)
            {
                for (var i = mag; i < _pipMag && i < PipMax; i++) _pipFlash[i] = 1f;
            }

            _pipMag = mag;

            var low = mag <= max * 0.25f;
            for (var i = 0; i < max; i++)
            {
                var colour = i < mag ? (low ? Red : Cream) : Rule;

                if (_pipFlash[i] > 0f)
                {
                    _pipFlash[i] = Mathf.MoveTowards(_pipFlash[i], 0f, dt * 3f);
                    colour = Color.Lerp(colour, Color.white, _pipFlash[i]);
                }

                _pips[i].color = colour;
            }
        }

        // ---------- crosshair ----------

        /// <summary>
        /// Three looks, cross-faded. Pistol: a dot with four ticks that open with the real spread and kick on every shot.
        /// Shotgun: a ring broken into four arcs, as wide as the pellet cone. Weapon lowered: a tiny closed ring.
        /// Nothing while the flashlight is on (the weapon is down then), the camera is raised, or you are dead.
        /// </summary>
        private void UpdateCrosshair(float dt)
        {
            var weapon = weapons ? weapons.Current as WeaponBase : null;
            var torch = flashlight && flashlight.Owned && flashlight.IsOn;
            var hidden = weapon == null || torch || _cameraRaised || _dead;
            var lowered = weapon != null && weapon.View != null && weapon.View.IsLowered;
            var shotgun = weapon != null && weapon.Pellets > 1;

            FadeGroup(_xhPistol, !hidden && !lowered && !shotgun ? 1f : 0f, dt);
            FadeGroup(_xhShotgun, !hidden && !lowered && shotgun ? 1f : 0f, dt);
            FadeGroup(_xhLowered, !hidden && lowered ? 0.7f : 0f, dt);

            if (weapon == null) return;

            // Every shot kicks the crosshair open.
            if (weapon != _kickWeapon)
            {
                _kickWeapon = weapon;
                _kickMag = weapon.Ammo;
            }

            if (weapon.Ammo < _kickMag) _kick = shotgun ? 26f : 12f;
            _kickMag = weapon.Ammo;
            _kick *= Mathf.Exp(-9f * dt);

            var spread = SpreadPixels(weapon.SpreadDegrees);
            var smooth = 1f - Mathf.Exp(-18f * dt);

            _gap = Mathf.Lerp(_gap, Mathf.Max(6f, spread) + _kick + 4f, smooth);
            const float length = 9f;
            var offset = _gap + length * 0.5f;
            SetTick(_ticks[0], new Vector2(0f, offset), new Vector2(2f, length));
            SetTick(_ticks[1], new Vector2(0f, -offset), new Vector2(2f, length));
            SetTick(_ticks[2], new Vector2(-offset, 0f), new Vector2(length, 2f));
            SetTick(_ticks[3], new Vector2(offset, 0f), new Vector2(length, 2f));

            _radius = Mathf.Lerp(_radius, Mathf.Max(24f, spread) + _kick, smooth);
            _arcs.SetRadius(_radius);
        }

        private static void FadeGroup(CanvasGroup group, float target, float dt)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, target, dt * 8f);
        }

        private static void SetTick(RectTransform tick, Vector2 position, Vector2 size)
        {
            tick.anchoredPosition = position;
            tick.sizeDelta = size;
        }

        /// <summary>Degrees of spread on the screen, in canvas units, for the main camera's field of view.</summary>
        private float SpreadPixels(float degrees)
        {
            var cam = Camera.main;
            if (!cam || !_hudCanvas) return 0f;

            var half = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var pixels = Mathf.Tan(degrees * Mathf.Deg2Rad) / Mathf.Max(0.01f, half) * (Screen.height * 0.5f);
            return pixels / Mathf.Max(0.01f, _hudCanvas.scaleFactor);
        }

        // ---------- objective, prompt, pickups ----------

        private void UpdateObjective(float dt)
        {
            if (!string.IsNullOrEmpty(_objBody) && KadrControls.Pressed(GameAction.Objective))
            {
                _objTimer = objectiveSeconds;
                _objTyped = 0f;
            }

            _objTimer = Mathf.Max(0f, _objTimer - dt);
            _objTyped += dt * 45f;

            _objectiveGroup.alpha = Mathf.MoveTowards(_objectiveGroup.alpha, _objTimer > 0f ? 1f : 0f, dt * (_objTimer > 0f ? 4f : 1.5f));

            var title = _objCompleted ? "COMPLETE" : _objTitle;
            _objectiveTitleText.text = Typed(title.ToUpperInvariant(), _objTyped * 1.5f);
            _objectiveBody.text = Typed(_objBody, _objTyped - 4f);
            _objectiveLine.color = _objCompleted ? Amber : Red;
        }

        private void UpdatePrompt(float dt)
        {
            _promptTyped += dt * 60f;
            _prompt.gameObject.SetActive(_promptVisible);
            if (_promptVisible) _prompt.text = Typed($"[ {KadrControls.Name(GameAction.Interact)} ]   {_promptText}", _promptTyped);

            _noticeTimer = Mathf.Max(0f, _noticeTimer - dt);
            _noticeTyped += dt * 55f;
            _notice.gameObject.SetActive(_noticeTimer > 0f);
            if (_noticeTimer > 0f)
            {
                _notice.text = Typed(_noticeText, _noticeTyped);
                _notice.color = new Color(Amber.r, Amber.g, Amber.b, Mathf.Clamp01(_noticeTimer / 0.5f));
            }
        }

        private void AddToast(string label)
        {
            var text = MakeText(_toastRoot, "Toast", 26, Cream, TextAnchor.UpperRight, true);
            Place(text.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(700f, 34f));

            _toasts.Insert(0, new Toast { Text = text, Full = label });

            while (_toasts.Count > 4)
            {
                var last = _toasts[_toasts.Count - 1];
                Destroy(last.Text.gameObject);
                _toasts.RemoveAt(_toasts.Count - 1);
            }
        }

        private void UpdateToasts(float dt)
        {
            for (var i = _toasts.Count - 1; i >= 0; i--)
            {
                var toast = _toasts[i];
                toast.Age += dt;

                if (toast.Age >= toastSeconds)
                {
                    Destroy(toast.Text.gameObject);
                    _toasts.RemoveAt(i);
                    continue;
                }

                toast.Text.text = Typed(toast.Full, toast.Age * 50f);
                toast.Text.rectTransform.anchoredPosition = new Vector2(0f, -i * 40f);

                var colour = Cream;
                colour.a = Mathf.Clamp01((toastSeconds - toast.Age) / 0.6f);
                toast.Text.color = colour;
            }
        }

        private static string Typed(string full, float chars)
        {
            if (string.IsNullOrEmpty(full)) return "";

            var count = Mathf.Clamp(Mathf.FloorToInt(chars), 0, full.Length);
            return count >= full.Length ? full : full.Substring(0, count);
        }

        // ---------- screen edges ----------

        private void UpdateEdges(float dt)
        {
            var low = _dead ? 0.6f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.05f, _fraction)) * 0.55f;
            low *= 1f + 0.18f * _beatPulse;

            var alpha = Mathf.Clamp01(Mathf.Max(low, _hitFlash * 0.55f));
            _edges.color = new Color(0f, 0f, 0f, alpha);
        }

        // ---------- building the canvas ----------

        private void Build()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;

            // Screen edges. Under the HUD, over the game.
            var edgeCanvas = MakeCanvas("HudEdges", 5);
            _vignette = MakeVignette();
            _edges = new GameObject("Edges", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            _edges.transform.SetParent(edgeCanvas.transform, false);
            _edges.texture = _vignette;
            _edges.color = new Color(0f, 0f, 0f, 0f);
            _edges.raycastTarget = false;
            Stretch(_edges.rectTransform);

            var canvas = MakeCanvas("HudCanvas", 40);
            _hudCanvas = canvas;
            _root = canvas.gameObject.AddComponent<CanvasGroup>();
            _root.interactable = false;
            _root.blocksRaycasts = false;

            BuildPulse(canvas.transform);
            BuildAmmo(canvas.transform);
            BuildObjective(canvas.transform);
            BuildCentre(canvas.transform);

            _toastRoot = NewRect("Toasts", canvas.transform);
            Place(_toastRoot, new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(-Margin, 90f), new Vector2(700f, 170f));
        }

        private void BuildPulse(Transform parent)
        {
            var root = NewRect("Pulse", parent);
            Place(root, Vector2.zero, Vector2.zero, new Vector2(Margin, Margin), new Vector2(TraceWidth, 190f));

            // Everything of the pulse shares one fade.
            var group = NewRect("TraceGroup", root);
            Stretch(group);
            _pulseGroup = group.gameObject.AddComponent<CanvasGroup>();

            _pulseLabel = MakeText(group, "Label", 22, Grey, TextAnchor.LowerLeft);
            Place(_pulseLabel.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(TraceWidth, 26f));

            _bpmLabel = MakeText(group, "Bpm", 22, Grey, TextAnchor.LowerRight);
            Place(_bpmLabel.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(TraceWidth, 26f));

            // The health ruler: a tick per 5%, a taller one every fifth.
            var spacing = TraceWidth / HealthTicks;
            for (var i = 0; i < HealthTicks; i++)
            {
                var major = i % 5 == 0;
                _healthTicks[i] = MakeImage(group, "Tick", Cream);
                Place(_healthTicks[i].rectTransform, Vector2.zero, Vector2.zero, new Vector2(i * spacing, 32f), new Vector2(2f, major ? 12f : 7f));
            }

            var traceGo = new GameObject("Trace", typeof(RectTransform), typeof(TraceGraphic));
            traceGo.transform.SetParent(group, false);
            _trace = traceGo.GetComponent<TraceGraphic>();
            _trace.raycastTarget = false;
            Place(_trace.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0f, 52f), new Vector2(TraceWidth, 110f));
        }

        private void BuildAmmo(Transform parent)
        {
            var root = NewRect("Ammo", parent);
            Place(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, Margin), new Vector2(360f, 210f));
            _ammoGroup = root.gameObject.AddComponent<CanvasGroup>();

            _ammoLabel = MakeText(root, "Label", 22, Grey, TextAnchor.LowerRight);
            Place(_ammoLabel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(360f, 26f));

            var rule = NewRect("Rule", root);
            _rule = rule;
            Place(rule, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 32f), new Vector2(360f, 3f));
            var back = MakeImage(rule, "Back", Rule);
            Stretch(back.rectTransform);

            // One pip per round for ordinary magazines. The thin rule above is the fallback for huge ones.
            _pipsRoot = NewRect("Pips", root);
            Place(_pipsRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 30f), new Vector2(360f, 10f));
            for (var i = 0; i < PipMax; i++) _pips[i] = MakeImage(_pipsRoot, "Pip", Cream);

            var fill = MakeImage(rule, "Fill", Cream);
            _ammoFillImage = fill;
            _ammoFill = fill.rectTransform;
            _ammoFill.anchorMin = new Vector2(0f, 0f);
            _ammoFill.anchorMax = new Vector2(0f, 1f);
            _ammoFill.pivot = new Vector2(0f, 0.5f);
            _ammoFill.anchoredPosition = Vector2.zero;
            _ammoFill.sizeDelta = new Vector2(360f, 0f);

            _ammoNumber = MakeText(root, "Number", 80, Cream, TextAnchor.LowerRight);
            Place(_ammoNumber.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 42f), new Vector2(500f, 100f));

            // The torch battery: ten cells and a nub, shown instead of the number and the line while the light is on.
            _cellsRoot = NewRect("Battery", root);
            Place(_cellsRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 48f), new Vector2(360f, 44f));
            const float gap = 4f;
            var cellWidth = (350f - gap * 9f) / 10f;
            for (var i = 0; i < _cells.Length; i++)
            {
                _cells[i] = MakeImage(_cellsRoot, "Cell", Cream);
                Place(_cells[i].rectTransform, Vector2.zero, Vector2.zero, new Vector2(i * (cellWidth + gap), 0f), new Vector2(cellWidth, 44f));
            }

            var nub = MakeImage(_cellsRoot, "Nub", Cream);
            Place(nub.rectTransform, Vector2.zero, Vector2.zero, new Vector2(354f, 14f), new Vector2(6f, 16f));
            _cellsRoot.gameObject.SetActive(false);

            _reload = MakeText(root, "Reload", 26, Red, TextAnchor.LowerRight);
            Place(_reload.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 150f), new Vector2(360f, 32f));
            _reload.gameObject.SetActive(false);
        }

        private void BuildObjective(Transform parent)
        {
            var root = NewRect("Objective", parent);
            Place(root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Margin, -Margin), new Vector2(1000f, 110f));
            _objectiveGroup = root.gameObject.AddComponent<CanvasGroup>();
            _objectiveGroup.alpha = 0f;

            _objectiveTitleText = MakeText(root, "Title", 22, Grey, TextAnchor.UpperLeft);
            Place(_objectiveTitleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(1000f, 28f));

            _objectiveBody = MakeText(root, "Body", 36, Cream, TextAnchor.UpperLeft, true);
            Place(_objectiveBody.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -32f), new Vector2(1000f, 46f));

            _objectiveLine = MakeImage(root, "Line", Red);
            Place(_objectiveLine.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -88f), new Vector2(48f, 3f));
        }

        private void BuildCentre(Transform parent)
        {
            var centre = new Vector2(0.5f, 0.5f);

            // Crosshair, three looks on top of each other.
            var crosshair = NewRect("Crosshair", parent);
            Place(crosshair, centre, centre, Vector2.zero, new Vector2(400f, 400f));

            var pistol = NewRect("Pistol", crosshair);
            Stretch(pistol);
            _xhPistol = pistol.gameObject.AddComponent<CanvasGroup>();
            for (var i = 0; i < 4; i++)
            {
                var tick = MakeImage(pistol, "Tick", Cream);
                Place(tick.rectTransform, centre, centre, Vector2.zero, new Vector2(2f, 9f));
                _ticks[i] = tick.rectTransform;
            }

            var pistolDot = MakeImage(pistol, "Dot", Cream);
            Place(pistolDot.rectTransform, centre, centre, Vector2.zero, new Vector2(4f, 4f));

            var shotgun = NewRect("Shotgun", crosshair);
            Stretch(shotgun);
            _xhShotgun = shotgun.gameObject.AddComponent<CanvasGroup>();
            _arcs = MakeRing(shotgun, "Arcs", 24f, 2f, 30f, Cream);
            var shotgunDot = MakeImage(shotgun, "Dot", Cream);
            Place(shotgunDot.rectTransform, centre, centre, Vector2.zero, new Vector2(3f, 3f));

            var lowered = NewRect("Lowered", crosshair);
            Stretch(lowered);
            _xhLowered = lowered.gameObject.AddComponent<CanvasGroup>();
            MakeRing(lowered, "Ring", 7f, 2f, 0f, Cream);

            _xhPistol.alpha = _xhShotgun.alpha = _xhLowered.alpha = 0f;

            // Stamina: a short line under the crosshair, where your eyes already are. It shrinks towards the middle.
            var line = NewRect("Stamina", parent);
            Place(line, centre, centre, new Vector2(0f, -100f), new Vector2(StaminaWidth, 3f));
            _staminaGroup = line.gameObject.AddComponent<CanvasGroup>();
            _staminaGroup.alpha = 0f;

            var back = MakeImage(line, "Back", Rule);
            Stretch(back.rectTransform);

            foreach (var side in new[] { -1f, 1f })
            {
                var cap = MakeImage(line, "Cap", Grey);
                Place(cap.rectTransform, centre, centre, new Vector2(side * (StaminaWidth * 0.5f + 5f), 0f), new Vector2(2f, 11f));
            }

            _staminaFillImage = MakeImage(line, "Fill", Cream);
            _staminaFill = _staminaFillImage.rectTransform;
            Place(_staminaFill, centre, centre, Vector2.zero, new Vector2(StaminaWidth, 3f));

            _prompt = MakeText(parent, "Prompt", 32, Cream, TextAnchor.MiddleCenter, true);
            Place(_prompt.rectTransform, centre, centre, new Vector2(0f, -148f), new Vector2(1200f, 44f));
            _prompt.gameObject.SetActive(false);

            _notice = MakeText(parent, "Notice", 26, Amber, TextAnchor.MiddleCenter, true);
            Place(_notice.rectTransform, centre, centre, new Vector2(0f, -196f), new Vector2(1200f, 36f));
            _notice.gameObject.SetActive(false);
        }

        private static RingGraphic MakeRing(Transform parent, string objectName, float radius, float thickness, float gap, Color colour)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(RingGraphic));
            go.transform.SetParent(parent, false);

            var ring = go.GetComponent<RingGraphic>();
            ring.raycastTarget = false;
            ring.color = colour;
            ring.Configure(radius, thickness, gap);
            Place(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400f, 400f));
            return ring;
        }

        private Canvas MakeCanvas(string canvasName, int order)
        {
            var go = new GameObject(canvasName, typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>typewriter: Special Elite for sentences (prompt, notices, objective). Otherwise Share Tech Mono for readouts.</summary>
        private Text MakeText(Transform parent, string objectName, int size, Color colour, TextAnchor alignment, bool typewriter = false)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = typewriter ? KadrFont.Body : KadrFont.Mono;
            text.fontSize = size;
            text.color = colour;
            text.alignment = alignment;
            text.supportRichText = true;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        private static Image MakeImage(Transform parent, string objectName, Color colour)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform NewRect(string objectName, Transform parent)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Black that is clear in the middle and solid at the corners.</summary>
        private static Texture2D MakeVignette()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x / (size - 1f) * 2f - 1f;
                    var dy = y / (size - 1f) * 2f - 1f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.25f, r))));
                }
            }

            tex.Apply();
            return tex;
        }
    }
}