using UnityEngine;
using UnityEngine.UI;

namespace Gameplay
{
    /// <summary>
    /// Sprint stamina. Put it on the Player next to PlayerController. It builds its own thin bar at the bottom centre
    /// of the screen, so nothing else needs setting up.
    ///
    ///   Sprinting drains it. Walking, standing and being still refill it after a short delay.
    ///   At zero the player is exhausted: no sprinting until it has recovered to the Recover Threshold
    ///   (so it can't flicker on and off at the edge). The bar turns red while exhausted.
    ///   The bar fades out while full and only shows up when there is something to see.
    ///
    /// WeaponController asks CanSprint, so aiming, reloading and being out of breath all use the same switch
    /// on PlayerController. Leninade, wine and upgrades call Restore() or SetMax().
    /// Time.deltaTime is 0 while the suitcase is open, so nothing drains or refills then.
    /// PlayerBuffs changes it: Leninade stops the drain (the bar turns blue and counts the effect down), being drunk
    /// drains faster and refills slower.
    /// </summary>
    public class PlayerStamina : MonoBehaviour
    {
        [Header("References (empty = found automatically)")]
        [SerializeField] private PlayerController player;

        [Header("Stamina")]
        [SerializeField, Min(1f)] private float maxStamina = 100f;
        [Tooltip("Per second while sprinting. 20 with a max of 100 = 5 seconds of sprint.")]
        [SerializeField, Min(0f)] private float drainPerSecond = 20f;
        [Tooltip("Per second while not sprinting, once the delay is over. 25 = 4 seconds from empty to full.")]
        [SerializeField, Min(0f)] private float regenPerSecond = 25f;
        [Tooltip("Seconds after the last sprint before it starts refilling.")]
        [SerializeField, Min(0f)] private float regenDelay = 1f;
        [Tooltip("After running dry, sprinting is locked until stamina is back to this fraction (0.25 = a quarter).")]
        [SerializeField, Range(0f, 1f)] private float recoverThreshold = 0.25f;

        [Header("Bar")]
        [SerializeField] private Vector2 barSize = new(420f, 10f);
        [Tooltip("Pixels above the bottom edge of the screen (1080p reference).")]
        [SerializeField] private float barBottomOffset = 70f;
        [SerializeField] private float fadeSpeed = 3f;

        private CharacterController _controller;
        private float _stamina;
        private float _sinceSprint;
        private bool _exhausted;
        private bool _sprinting;

        private StaminaBar _bar;
        private float _alpha;

        public float Fraction => _stamina / maxStamina;
        public bool IsExhausted => _exhausted;
        public bool IsSprinting => _sprinting;

        /// <summary>False while out of breath. WeaponController combines this with its own aim and reload rules.</summary>
        public bool CanSprint => !_exhausted;

        /// <summary>Adds stamina (Leninade, wine). Clears the exhaustion if it brings the player past the threshold.</summary>
        public void Restore(float amount)
        {
            _stamina = Mathf.Min(maxStamina, _stamina + amount);
            if (_exhausted && Fraction >= recoverThreshold) _exhausted = false;
        }

        /// <summary>For upgrades. Keeps the same fraction of the bar filled.</summary>
        public void SetMax(float newMax)
        {
            var fraction = Fraction;
            maxStamina = Mathf.Max(1f, newMax);
            _stamina = maxStamina * fraction;
        }

        private void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
            _controller = GetComponent<CharacterController>();
            _stamina = maxStamina;
            _sinceSprint = regenDelay;
            _bar = StaminaBar.Create(transform, barSize, barBottomOffset);
        }

        private void Update()
        {
            var dt = Time.deltaTime;

            var buffs = PlayerBuffs.Instance;
            var infinite = buffs && buffs.InfiniteStamina; // Leninade
            var drain = drainPerSecond * (buffs ? buffs.StaminaDrainMultiplier : 1f); // drunk: faster
            var regen = regenPerSecond * (buffs ? buffs.StaminaRegenMultiplier : 1f); // drunk: slower

            if (infinite) _exhausted = false;
            _sprinting = !_exhausted && IsReallySprinting();

            if (_sprinting)
            {
                _sinceSprint = 0f;
                if (!infinite) _stamina = Mathf.Max(0f, _stamina - drain * dt);
                if (_stamina <= 0f) _exhausted = true;
            }
            else
            {
                _sinceSprint += dt;
                if (_sinceSprint >= regenDelay || infinite)
                {
                    _stamina = Mathf.Min(maxStamina, _stamina + regen * dt);
                    if (_exhausted && Fraction >= recoverThreshold) _exhausted = false;
                }
            }

            UpdateBar(dt, infinite ? buffs.InfiniteFraction : -1f);
        }

        /// <summary>Same condition PlayerController uses to sprint, plus actually moving (running into a wall costs nothing).</summary>
        private bool IsReallySprinting()
        {
            if (!player || !player.InputEnabled || !player.CanSprint) return false;
            if (!GameInput.SprintHeld() || GameInput.Move().y <= 0f) return false;
            if (!_controller) return true;

            var v = _controller.velocity;
            v.y = 0f;
            return v.sqrMagnitude > 1f;
        }

        /// <param name="boostFraction">Time left of the infinite stamina (1..0), or -1 when there is none.</param>
        private void UpdateBar(float dt, float boostFraction)
        {
            var boosted = boostFraction >= 0f;
            var hidden = Time.timeScale == 0f || (!boosted && Fraction >= 0.999f && !_exhausted);
            _alpha = Mathf.MoveTowards(_alpha, hidden ? 0f : 1f, dt * fadeSpeed);
            if (Time.timeScale == 0f) _alpha = 0f;

            // With infinite stamina the bar counts the effect down instead of showing stamina.
            _bar.Set(boosted ? boostFraction : Fraction, recoverThreshold, _exhausted, _alpha, boosted);
        }
    }

    /// <summary>The thin stamina bar. A dark back, a fill that shrinks, and a tick at the recover threshold.</summary>
    public class StaminaBar : MonoBehaviour
    {
        private RectTransform _fill;
        private Image _fillImage;
        private Image _back;
        private CanvasGroup _group;

        public static StaminaBar Create(Transform parent, Vector2 size, float bottomOffset)
        {
            var go = new GameObject("StaminaBar");
            go.transform.SetParent(parent, false);

            var bar = go.AddComponent<StaminaBar>();
            bar.Build(size, bottomOffset);
            return bar;
        }

        private void Build(Vector2 size, float bottomOffset)
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 14;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _group = canvasGo.GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var back = new GameObject("Back", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(canvasGo.transform, false);
            _back = back.GetComponent<Image>();
            _back.color = new Color(0.05f, 0.05f, 0.05f, 0.7f);
            _back.raycastTarget = false;
            var backRect = _back.rectTransform;
            backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0f);
            backRect.pivot = new Vector2(0.5f, 0f);
            backRect.anchoredPosition = new Vector2(0f, bottomOffset);
            backRect.sizeDelta = size + new Vector2(4f, 4f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(back.transform, false);
            _fillImage = fill.GetComponent<Image>();
            _fillImage.raycastTarget = false;
            _fill = _fillImage.rectTransform;
            _fill.anchorMin = new Vector2(0f, 0f);
            _fill.anchorMax = new Vector2(1f, 1f);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.offsetMin = new Vector2(2f, 2f);
            _fill.offsetMax = new Vector2(-2f, -2f);
        }

        public void Set(float fraction, float threshold, bool exhausted, float alpha, bool boosted = false)
        {
            _group.alpha = alpha;
            if (alpha <= 0f) return;

            // Shrink the fill from the right by moving its right anchor.
            _fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            _fillImage.color = boosted
                ? new Color(0.35f, 0.8f, 1f)
                : exhausted
                ? new Color(0.85f, 0.2f, 0.18f)
                : fraction < threshold ? new Color(0.9f, 0.6f, 0.2f) : new Color(0.85f, 0.88f, 0.6f);
        }
    }
}