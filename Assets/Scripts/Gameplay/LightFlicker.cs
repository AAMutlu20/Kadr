using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// Makes the lights of a lamp flicker like the flashlight does with a weak battery: smooth noise that dips the
    /// intensity, plus now and then a short stutter or a brief blackout (a dying tube lamp).
    ///
    /// Put it on the lamp itself. The Lights list is filled in by itself with every Light on the lamp and its children;
    /// drag lights in to choose them by hand instead. All of them flicker together, so a lamp with two bulbs stays in step.
    ///
    /// Randomness (the Variation setting):
    ///   Identical  every lamp flickers at exactly the same moments, rate and depth (power failures, alarms).
    ///   Seeded     every lamp is different, but the result is the same on every run (predictable, good for testing).
    ///   Random     every lamp is different and it changes every run.
    /// Spread sets how far lamps differ from the values below in Seeded and Random.
    ///
    /// It costs almost nothing, and it sleeps (does no work at all) while the player is farther away than Active
    /// Distance. The lit fog in KadrLook follows each light's intensity by itself, so the haze around a flickering
    /// lamp flickers with it.
    ///
    /// Optional: drag the lamp's mesh into Glow Renderer and its emission flickers too (the material needs Emission on).
    /// </summary>
    public class LightFlicker : MonoBehaviour
    {
        [Header("Lights")]
        [Tooltip("Empty = every Light on this object and its children (found when the game starts). Or drag the ones you want.")]
        [SerializeField] private Light[] lights;

        public enum LampVariation { Identical, Seeded, Random }

        [Header("Randomness")]
        [Tooltip("Identical = all lamps in step. Seeded = every lamp different, the same on every run. Random = every lamp different, new each run.")]
        [SerializeField] private LampVariation variation = LampVariation.Seeded;
        [Tooltip("Seeded only: change this number to get a different (but still repeatable) set of flickers.")]
        [SerializeField] private int seed = 1;
        [Tooltip("How far each lamp strays from the values below: speed, depth, lowest and stutter timing (0.35 = about plus or minus a third).")]
        [SerializeField, Range(0f, 1f)] private float spread = 0.35f;

        [Header("Flicker")]
        [Tooltip("0 = steady, 1 = the light dips all the way down to Lowest.")]
        [SerializeField, Range(0f, 1f)] private float strength = 0.6f;
        [Tooltip("How fast the light wobbles (noise per second).")]
        [SerializeField, Range(0.5f, 30f)] private float speed = 12f;
        [Tooltip("The dimmest the light gets, as a share of its normal brightness.")]
        [SerializeField, Range(0f, 1f)] private float lowest = 0.15f;

        [Header("Stutters and blackouts")]
        [Tooltip("Average seconds between stutters. 0 = never.")]
        [SerializeField, Min(0f)] private float stutterEvery = 6f;
        [Tooltip("How long a stutter lasts, in seconds (min, max).")]
        [SerializeField] private Vector2 stutterLength = new(0.08f, 0.4f);

        [Header("Cost")]
        [Tooltip("Farther than this from the player the lamp stays steady and does no work. 0 = always runs.")]
        [SerializeField, Min(0f)] private float activeDistance = 30f;

        [Header("Lamp mesh (optional)")]
        [SerializeField] private Renderer glowRenderer;
        [Tooltip("Emission colour at full brightness. Leave black to use the first light's colour.")]
        [SerializeField, ColorUsage(false, true)] private Color glowColor = Color.black;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private float[] _baseIntensity;
        private float _noiseSeed;
        private float _timeOffset;
        private float _speed;
        private float _strength;
        private float _lowest;
        private float _stutterEvery;
        private Vector2 _stutterLength;
        private float _nextDistanceCheck;
        private bool _near = true;
        private MaterialPropertyBlock _block;
        private Camera _cam;

        private void Awake()
        {
            if (lights == null || lights.Length == 0) lights = GetComponentsInChildren<Light>(true);

            if (lights.Length == 0) Debug.LogWarning("[LightFlicker] No Light found on this object or its children.", this);

            _baseIntensity = new float[lights.Length];
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i]) _baseIntensity[i] = lights[i].intensity;
            }

            ChooseValues();

            if (glowRenderer) _block = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            if (_baseIntensity == null) return;

            Apply(1f);
        }

        private void Update()
        {
            if (activeDistance > 0f && Time.time >= _nextDistanceCheck)
            {
                _nextDistanceCheck = Time.time + 0.25f;
                if (!_cam) _cam = Camera.main;

                var near = !_cam || (_cam.transform.position - transform.position).sqrMagnitude <= activeDistance * activeDistance;
                if (near != _near)
                {
                    _near = near;
                    if (!_near) Apply(1f); // far away: steady at full brightness
                }
            }

            if (!_near) return;

            var t = Time.time + _timeOffset;

            // Smooth wobble, the same shape the flashlight uses.
            var noise = Mathf.PerlinNoise(t * _speed + _noiseSeed, 0.3f);
            var factor = Mathf.Lerp(1f, Mathf.Lerp(_lowest, 1f, noise), _strength);

            factor *= StutterFactor(t);

            Apply(factor);
        }

        private void Apply(float factor)
        {
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i]) lights[i].intensity = _baseIntensity[i] * factor;
            }

            ApplyGlow(factor);
        }

        private void ApplyGlow(float factor)
        {
            if (!glowRenderer || _block == null) return;

            var colour = glowColor.maxColorComponent > 0f ? glowColor
                : lights.Length > 0 && lights[0] ? lights[0].color : Color.white;

            glowRenderer.GetPropertyBlock(_block);
            _block.SetColor(EmissionColorId, colour * factor);
            glowRenderer.SetPropertyBlock(_block);
        }

        /// <summary>
        /// This lamp's own numbers. Identical: the values as set, no offsets, so every lamp matches. Seeded: derived
        /// from the seed and the lamp's position, so the same lamp always gets the same flicker. Random: new each run.
        /// </summary>
        private void ChooseValues()
        {
            _speed = speed;
            _strength = strength;
            _lowest = lowest;
            _stutterEvery = stutterEvery;
            _stutterLength = stutterLength;
            _noiseSeed = 0f;
            _timeOffset = 0f;

            if (variation == LampVariation.Identical) return;

            int rngSeed;
            if (variation == LampVariation.Seeded)
            {
                var p = transform.position;
                unchecked
                {
                    rngSeed = seed;
                    rngSeed = rngSeed * 31 + Mathf.RoundToInt(p.x * 100f);
                    rngSeed = rngSeed * 31 + Mathf.RoundToInt(p.y * 100f);
                    rngSeed = rngSeed * 31 + Mathf.RoundToInt(p.z * 100f);
                }
            }
            else
            {
                rngSeed = Random.Range(int.MinValue, int.MaxValue);
            }

            var rng = new System.Random(rngSeed);
            float Around() => Mathf.Lerp(1f - spread, 1f + spread, (float)rng.NextDouble());

            _speed = Mathf.Max(0.5f, speed * Around());
            _strength = Mathf.Clamp01(strength * Around());
            _lowest = Mathf.Clamp01(lowest * Around());
            _stutterEvery = stutterEvery * Around();
            _stutterLength = stutterLength * Around();
            _noiseSeed = (float)rng.NextDouble() * 100f;
            _timeOffset = (float)rng.NextDouble() * 100f;
        }

        /// <summary>
        /// One stutter per time slot, at a place and for a length that depend only on the slot number and this lamp's
        /// noise seed. No stored state, so lamps with the same values stutter at exactly the same moments.
        /// </summary>
        private float StutterFactor(float t)
        {
            if (_stutterEvery <= 0.01f) return 1f;

            var u = t / _stutterEvery;
            var slot = Mathf.Floor(u);
            var inSlot = u - slot;

            var start = Hash(slot, _noiseSeed) * 0.6f;
            var length = Mathf.Lerp(_stutterLength.x, _stutterLength.y, Hash(slot, _noiseSeed + 17.3f)) / _stutterEvery;
            if (inSlot < start || inSlot >= start + length) return 1f;

            var flash = Mathf.PerlinNoise(t * 45f + _noiseSeed, 1.7f);
            return flash > 0.45f ? 1f : _lowest * 0.3f;
        }

        private static float Hash(float a, float b)
        {
            var x = Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f;
            return x - Mathf.Floor(x);
        }
    }
}