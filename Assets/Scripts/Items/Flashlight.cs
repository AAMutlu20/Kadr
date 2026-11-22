using Core;
using Events;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Items
{
    /// <summary>
    /// The flashlight. On the Player, next to the Inventory. It builds its own spot light on the camera and a small
    /// battery bar on screen, so nothing else needs setting up.
    ///
    ///   Toggle key (Q)     on / off. With a dead battery it swaps in a new one from the suitcase first.
    ///   Swap key (B)       puts in a fresh battery now (uses one battery, charge back to full).
    ///
    /// It only works while the flashlight key item is in the suitcase. The battery drains only while the light is on
    /// and the game is running (never while paused). Below 20% the light flickers. Battery life and the light level
    /// are upgraded by the Comisar later: change Battery Seconds and call SetLevel.
    /// </summary>
    public class Flashlight : MonoBehaviour
    {
        public enum LightLevel { Low, Medium, High }

        [System.Serializable]
        private struct LevelSettings
        {
            public float intensity;
            public float range;
            public float spotAngle;
        }

        [Header("References (empty = found automatically)")]
        [SerializeField] private Inventory inventory;
        [SerializeField] private Camera cam;

        [Header("Items")]
        [Tooltip("The flashlight key item. The light only works while it is in the suitcase. Empty = always available (testing).")]
        [SerializeField] private ItemDefinition flashlightItem;
        [Tooltip("The battery item that the swap key and an empty battery take from the suitcase.")]
        [SerializeField] private ItemDefinition batteryItem;

        [Header("Keys")]
        [SerializeField] private Key toggleKey = Key.Q;
        [SerializeField] private Key swapBatteryKey = Key.B;

        [Header("Battery")]
        [Tooltip("Seconds of light from a full battery. 10 at the start, 15 and 20 after the Comisar's upgrades.")]
        [SerializeField, Min(1f)] private float batterySeconds = 10f;
        [SerializeField, Range(0f, 1f)] private float flickerBelow = 0.2f;

        [Header("Light")]
        [SerializeField] private LightLevel level = LightLevel.Low;
        [SerializeField] private LevelSettings low = new() { intensity = 4f, range = 18f, spotAngle = 50f };
        [SerializeField] private LevelSettings medium = new() { intensity = 7f, range = 28f, spotAngle = 58f };
        [SerializeField] private LevelSettings high = new() { intensity = 12f, range = 40f, spotAngle = 66f };
        [SerializeField] private Vector3 lightOffset = new(0.25f, -0.15f, 0.2f);

        private Light _light;
        private float _charge = 1f;
        private bool _on;
        private float _lastPublishedCharge = -1f;
        private bool _lastPublishedOn;
        private bool _lastPublishedOwned;
        private EventBus _bus;
        private FlashlightBar _bar;

        public float Charge01 => _charge;
        public bool IsOn => _on;
        public bool Owned => !flashlightItem || (inventory && inventory.GridFor(flashlightItem.Category).Find(flashlightItem) != null);
        public bool CanRecharge => Owned && _charge < 0.999f;

        public void SetLevel(LightLevel newLevel)
        {
            level = newLevel;
            ApplyLevel();
        }

        public void SetBatterySeconds(float seconds) => batterySeconds = Mathf.Max(1f, seconds);

        public void Recharge()
        {
            _charge = 1f;
        }

        private void Awake()
        {
            if (!inventory) inventory = GetComponentInParent<Inventory>();
            if (!inventory) inventory = GetComponent<Inventory>();
            if (!cam) cam = Camera.main;

            if (cam)
            {
                var go = new GameObject("FlashlightLight");
                go.transform.SetParent(cam.transform, false);
                go.transform.localPosition = lightOffset;

                _light = go.AddComponent<Light>();
                _light.type = LightType.Spot;
                _light.shadows = LightShadows.Soft;
                _light.color = new Color(1f, 0.95f, 0.8f);
                _light.enabled = false;
                ApplyLevel();
            }
            else
            {
                Debug.LogWarning("[Flashlight] No camera found. Assign one in the inspector.", this);
            }

            _bar = FlashlightBar.Create(transform);
        }

        private void OnEnable()
        {
            GameServices.TryGet(out _bus);
        }

        private void OnDisable()
        {
            if (_light) _light.enabled = false;
        }

        private void OnValidate()
        {
            if (_light) ApplyLevel();
        }

        private void Update()
        {
            var owned = Owned;
            if (!owned && _on) SetOn(false);

            var paused = Time.timeScale == 0f || (inventory && inventory.IsOpen);
            var kb = Keyboard.current;

            if (!paused && kb != null && owned)
            {
                if (kb[toggleKey].wasPressedThisFrame) Toggle();
                if (kb[swapBatteryKey].wasPressedThisFrame) SwapBattery();
            }

            if (_on && !paused)
            {
                _charge = Mathf.Max(0f, _charge - Time.deltaTime / batterySeconds);
                if (_charge <= 0f)
                {
                    SetOn(false);
                    Notice("The flashlight battery is dead");
                }
            }

            UpdateLight();
            Publish(owned);
        }

        private void Toggle()
        {
            if (_on)
            {
                SetOn(false);
                return;
            }

            if (_charge <= 0f && !SwapBattery()) return;

            SetOn(true);
        }

        /// <summary>Uses one battery from the suitcase. False (with a message) if there is none or the light is already full.</summary>
        private bool SwapBattery()
        {
            if (_charge > 0.999f)
            {
                Notice("The battery is still full");
                return false;
            }

            if (!batteryItem || !inventory)
            {
                Notice("No battery item assigned");
                return false;
            }

            var stack = inventory.GridFor(batteryItem.Category).Find(batteryItem);
            if (stack == null)
            {
                Notice("No batteries left");
                return false;
            }

            // Goes through the normal item use, so the battery is only consumed when the recharge really happened.
            if (!inventory.TryUse(stack)) return false;

            Notice("New battery");
            return true;
        }

        private void SetOn(bool on)
        {
            _on = on;
            UpdateLight();
        }

        private void UpdateLight()
        {
            if (!_light) return;

            _light.enabled = _on;
            if (!_on) return;

            var settings = Current();
            var intensity = settings.intensity;

            // A weak battery flickers, more and more as it runs out.
            if (_charge < flickerBelow)
            {
                var weak = 1f - _charge / flickerBelow;
                var noise = Mathf.PerlinNoise(Time.time * 18f, 0.3f);
                intensity *= Mathf.Lerp(1f, Mathf.Lerp(0.15f, 1f, noise), weak);
            }

            _light.intensity = intensity;
        }

        private void ApplyLevel()
        {
            if (!_light) return;

            var settings = Current();
            _light.intensity = settings.intensity;
            _light.range = settings.range;
            _light.spotAngle = settings.spotAngle;
            _light.innerSpotAngle = settings.spotAngle * 0.6f;
        }

        private LevelSettings Current()
        {
            return level == LightLevel.High ? high : level == LightLevel.Medium ? medium : low;
        }

        private void Publish(bool owned)
        {
            if (Mathf.Abs(_charge - _lastPublishedCharge) < 0.005f && _on == _lastPublishedOn && owned == _lastPublishedOwned) return;

            _lastPublishedCharge = _charge;
            _lastPublishedOn = _on;
            _lastPublishedOwned = owned;

            var evt = new FlashlightChangedEvent(_charge, _on, owned);
            if (_bar) _bar.Show(evt);
            if (_bus != null || GameServices.TryGet(out _bus)) _bus.Publish(evt);
        }

        private void Notice(string message)
        {
            if (_bus != null || GameServices.TryGet(out _bus)) _bus.Publish(new NoticeEvent(message));
        }
    }

    /// <summary>The little battery bar in the bottom left corner: ten segments, red when low, dim when the light is off.</summary>
    public class FlashlightBar : MonoBehaviour
    {
        private const int Segments = 10;

        private readonly Image[] _cells = new Image[Segments];
        private GameObject _root;
        private Text _label;

        public static FlashlightBar Create(Transform parent)
        {
            var go = new GameObject("FlashlightBar");
            go.transform.SetParent(parent, false);

            var bar = go.AddComponent<FlashlightBar>();
            bar.Build();
            return bar;
        }

        private void Build()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(canvasGo.transform, false);
            var rect = _root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(40f, 40f);
            rect.sizeDelta = new Vector2(250f, 50f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(_root.transform, false);
            _label = labelGo.GetComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize = 20;
            _label.fontStyle = FontStyle.Bold;
            _label.text = "BATTERY";
            _label.alignment = TextAnchor.UpperLeft;
            _label.raycastTarget = false;
            var labelRect = _label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(0f, 26f);

            for (var i = 0; i < Segments; i++)
            {
                var cellGo = new GameObject("Cell" + i, typeof(RectTransform), typeof(Image));
                cellGo.transform.SetParent(_root.transform, false);
                var image = cellGo.GetComponent<Image>();
                image.raycastTarget = false;
                var cellRect = image.rectTransform;
                cellRect.anchorMin = cellRect.anchorMax = Vector2.zero;
                cellRect.pivot = Vector2.zero;
                cellRect.sizeDelta = new Vector2(21f, 16f);
                cellRect.anchoredPosition = new Vector2(i * 24f, 0f);
                _cells[i] = image;
            }

            _root.SetActive(false);
        }

        public void Show(FlashlightChangedEvent e)
        {
            _root.SetActive(e.Owned);
            if (!e.Owned) return;

            var low = e.Charge <= 0.3f;
            var on = new Color(low ? 0.9f : 0.55f, low ? 0.25f : 0.85f, low ? 0.2f : 0.45f, e.IsOn ? 1f : 0.55f);
            var off = new Color(0.12f, 0.12f, 0.12f, 0.8f);
            var filled = Mathf.CeilToInt(e.Charge * Segments - 0.001f);

            for (var i = 0; i < Segments; i++) _cells[i].color = i < filled ? on : off;
            _label.color = new Color(1f, 1f, 1f, e.IsOn ? 1f : 0.6f);
        }
    }
}