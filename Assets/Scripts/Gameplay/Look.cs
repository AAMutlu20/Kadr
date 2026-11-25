using Items;
using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// Drives the Kadr look (KadrLook.shader). Put it on one object in every gameplay scene (or on the camera).
    ///
    /// The shader reads global values, so this script never touches a material asset. It reacts to the game:
    ///  - the vignette closes in while the flashlight is off, and breathes with the player's pulse;
    ///  - FadeFog lets a room or an event (the boss) thicken or clear the fog over a few seconds.
    ///
    /// [ExecuteAlways]: the look also shows in the editor, so you can tune it with the Game view open.
    /// Built-in fog (Lighting > Environment) is switched off while this runs, otherwise the scene is fogged twice.
    /// </summary>
    [ExecuteAlways]
    public class KadrLook : MonoBehaviour
    {
        public static KadrLook Instance { get; private set; }

        public enum DebugView { Off, Distance, FogAmount, RawDepth }

        [Header("Debug")]
        [Tooltip("Shows what the shader sees instead of the picture. Distance: a grey ramp every 10 m (all black = no depth reaches the shader). " +
                 "Fog Amount: white = fully fogged. Raw Depth: the depth texture itself.")]
        [SerializeField] private DebugView debugView = DebugView.Off;

        [Header("Fog")]
        [SerializeField] private Color fogColor = new(0.12f, 0.125f, 0.125f);
        [Tooltip("Exponential: about 37% of a surface is still visible at 1 / density metres. 0.2 = 5 m. Rooms need 0.1 to 0.3.")]
        [SerializeField, Range(0f, 0.5f)] private float density = 0.2f;
        [Tooltip("How much the slow noise thickens and thins the fog.")]
        [SerializeField, Range(0f, 1f)] private float noiseAmount = 0.35f;
        [Tooltip("Size of the drifting clouds: higher = smaller.")]
        [SerializeField, Range(0.02f, 1f)] private float noiseScale = 0.18f;
        [SerializeField] private Vector3 drift = new(0.15f, 0.02f, 0.1f);
        [Tooltip("0 = the same everywhere, 1 = the fog lies on the floor.")]
        [SerializeField, Range(0f, 1f)] private float groundFog = 0.5f;
        [SerializeField] private float floorHeight;
        [Tooltip("Higher = the ground fog is a thinner layer.")]
        [SerializeField, Range(0.05f, 3f)] private float groundFalloff = 0.6f;
        [SerializeField] private bool disableBuiltInFog = true;

        [Header("Light through the fog")]
        [Tooltip("Pixels brighter than this are fogged less. Lamps are well above 1 with HDR on.")]
        [SerializeField, Range(0f, 4f)] private float punchStart = 0.9f;
        [SerializeField, Range(0f, 6f)] private float punchStrength = 2f;
        [Tooltip("Only light above this makes a halo.")]
        [SerializeField, Range(0f, 4f)] private float glowThreshold = 0.8f;
        [SerializeField, Range(0f, 2f)] private float glowStrength = 0.35f;
        [Tooltip("Halo size as a share of the screen height.")]
        [SerializeField, Range(0.005f, 0.15f)] private float glowRadius = 0.035f;

        [Header("Lit fog (light in the air)")]
        [Tooltip("Lights the fog: lamps glow in the air and the flashlight shows as a beam. Uses the nearest lights in the scene automatically.")]
        [SerializeField] private bool litFog = true;
        [Tooltip("How bright the lit fog is. Raise it for stronger beams.")]
        [SerializeField, Range(0f, 3f)] private float scatter = 0.25f;
        [Tooltip("A light's intensity is capped at this before it lights the fog, so one strong lamp cannot white out the screen.")]
        [SerializeField, Range(0.5f, 20f)] private float intensityCap = 4f;
        [Tooltip("0 = the beam looks the same from every side, higher = brighter when you look into the light.")]
        [SerializeField, Range(0f, 0.8f)] private float forwardScatter = 0.35f;
        [Tooltip("Longest stretch of air that is lit, in metres.")]
        [SerializeField, Range(5f, 40f)] private float scatterDistance = 20f;

        [Header("Vignette")]
        [SerializeField, Range(0f, 1f)] private float vignette = 0.55f;
        [SerializeField, Range(0f, 1f)] private float vignetteInner = 0.35f;
        [SerializeField, Range(0.2f, 1.5f)] private float vignetteOuter = 0.95f;

        [Header("Reactions (play mode)")]
        [Tooltip("Extra vignette while the flashlight is off.")]
        [SerializeField, Range(0f, 0.5f)] private float darkExtra = 0.15f;
        [Tooltip("How far the vignette breathes with the pulse, at full panic.")]
        [SerializeField, Range(0f, 0.3f)] private float pulseVignette = 0.12f;
        [Tooltip("How fast the reactions follow (higher = quicker).")]
        [SerializeField, Range(1f, 20f)] private float follow = 4f;

        private float _fogScale = 1f;
        private float _fogTarget = 1f;
        private float _fogRate = 1000f;

        private const int MaxLights = 8;
        private static readonly Vector4[] LightPos = new Vector4[MaxLights];
        private static readonly Vector4[] LightCol = new Vector4[MaxLights];
        private static readonly Vector4[] LightDir = new Vector4[MaxLights];
        private readonly float[] _lightScore = new float[MaxLights];
        private Light[] _sceneLights = System.Array.Empty<Light>();
        private float _nextLightSearch;
        private int _lightCount;

        private static readonly int LightCountId = Shader.PropertyToID("_KadrLightCount");
        private static readonly int LightPosId = Shader.PropertyToID("_KadrLightPos");
        private static readonly int LightColId = Shader.PropertyToID("_KadrLightCol");
        private static readonly int LightDirId = Shader.PropertyToID("_KadrLightDir");
        private static readonly int ScatterId = Shader.PropertyToID("_KadrScatter");
        private static readonly int ScatterMaxId = Shader.PropertyToID("_KadrScatterMax");
        private static readonly int AnisoId = Shader.PropertyToID("_KadrAniso");

        private float _extra;
        private float _pulseBeat;
        private Flashlight _flashlight;
        private float _nextFind;

        private static readonly int DebugId = Shader.PropertyToID("_KadrDebug");
        private static readonly int FogColorId = Shader.PropertyToID("_KadrFogColor");
        private static readonly int WindId = Shader.PropertyToID("_KadrWind");
        private static readonly int DensityId = Shader.PropertyToID("_KadrDensity");
        private static readonly int NoiseAmountId = Shader.PropertyToID("_KadrNoiseAmount");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_KadrNoiseScale");
        private static readonly int HeightInfluenceId = Shader.PropertyToID("_KadrHeightInfluence");
        private static readonly int FogBaseId = Shader.PropertyToID("_KadrFogBase");
        private static readonly int HeightFalloffId = Shader.PropertyToID("_KadrHeightFalloff");
        private static readonly int PunchStartId = Shader.PropertyToID("_KadrPunchStart");
        private static readonly int PunchStrengthId = Shader.PropertyToID("_KadrPunchStrength");
        private static readonly int GlowThresholdId = Shader.PropertyToID("_KadrGlowThreshold");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_KadrGlowStrength");
        private static readonly int GlowRadiusId = Shader.PropertyToID("_KadrGlowRadius");
        private static readonly int VigAmountId = Shader.PropertyToID("_KadrVigAmount");
        private static readonly int VigInnerId = Shader.PropertyToID("_KadrVigInner");
        private static readonly int VigOuterId = Shader.PropertyToID("_KadrVigOuter");

        /// <summary>Multiplier on the fog density right now (1 = as set here).</summary>
        public float FogScale => _fogScale;

        /// <summary>
        /// Thickens (above 1) or clears (below 1) the fog over the given time. For a boss room, a gas leak, a room
        /// the player has lit. Call it again with 1 to go back.
        /// </summary>
        public void FadeFog(float multiplier, float seconds)
        {
            _fogTarget = Mathf.Max(0f, multiplier);
            _fogRate = seconds <= 0.01f ? 1000f : Mathf.Abs(_fogTarget - _fogScale) / seconds;
        }

        private void OnEnable()
        {
            Instance = this;
            _fogScale = _fogTarget = 1f;
            Push(0f);
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;

            // Back to "nothing set": the shader then leaves the picture alone.
            Shader.SetGlobalFloat(DensityId, 0f);
            Shader.SetGlobalFloat(VigAmountId, 0f);
            Shader.SetGlobalFloat(GlowStrengthId, 0f);
            Shader.SetGlobalFloat(DebugId, 0f);
            Shader.SetGlobalFloat(LightCountId, 0f);
            Shader.SetGlobalFloat(ScatterId, 0f);
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled) Push(0f);
        }

        private void Update()
        {
            _fogScale = Mathf.MoveTowards(_fogScale, _fogTarget, _fogRate * Time.unscaledDeltaTime);

            var extra = 0f;
            if (Application.isPlaying) extra = React();

            _extra = Mathf.Lerp(_extra, extra, 1f - Mathf.Exp(-follow * Time.unscaledDeltaTime));
            Push(_extra);
        }

        /// <summary>Extra vignette from what the player is doing: dark when the torch is off, breathing with the pulse.</summary>
        private float React()
        {
            if (!_flashlight && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 1f;
                _flashlight = FindAnyObjectByType<Flashlight>();
            }

            var extra = _flashlight && !_flashlight.IsOn ? darkExtra : 0f;

            var pulse = PlayerPulse.Instance;
            if (pulse)
            {
                // One soft swell per heartbeat; a calm heart barely moves it, a panicking one clearly does.
                _pulseBeat += pulse.Bpm / 60f * Time.unscaledDeltaTime;
                var beat = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(_pulseBeat * Mathf.PI * 2f)), 3f);
                extra += beat * pulseVignette * pulse.Excitement01;
            }

            return extra;
        }

        private void Push(float extraVignette)
        {
            if (disableBuiltInFog && RenderSettings.fog) RenderSettings.fog = false;

            Shader.SetGlobalFloat(DebugId, (float)debugView);
            Shader.SetGlobalColor(FogColorId, fogColor);
            Shader.SetGlobalVector(WindId, drift);
            Shader.SetGlobalFloat(DensityId, density * _fogScale);
            Shader.SetGlobalFloat(NoiseAmountId, noiseAmount);
            Shader.SetGlobalFloat(NoiseScaleId, noiseScale);
            Shader.SetGlobalFloat(HeightInfluenceId, groundFog);
            Shader.SetGlobalFloat(FogBaseId, floorHeight);
            Shader.SetGlobalFloat(HeightFalloffId, groundFalloff);
            Shader.SetGlobalFloat(PunchStartId, punchStart);
            Shader.SetGlobalFloat(PunchStrengthId, punchStrength);
            Shader.SetGlobalFloat(GlowThresholdId, glowThreshold);
            Shader.SetGlobalFloat(GlowStrengthId, glowStrength);
            Shader.SetGlobalFloat(GlowRadiusId, glowRadius);
            Shader.SetGlobalFloat(VigAmountId, Mathf.Clamp01(vignette + extraVignette));
            Shader.SetGlobalFloat(VigInnerId, vignetteInner);
            Shader.SetGlobalFloat(VigOuterId, vignetteOuter);

            PushLights();
        }

        /// <summary>
        /// Hands the nearest lights to the shader so it can light the fog. The scene is searched twice a second
        /// (rooms are built at run time), the nearest lights are re-picked every frame (the flashlight moves).
        /// </summary>
        private void PushLights()
        {
            _lightCount = 0;

            var cam = Camera.main;
            if (litFog && scatter > 0f && cam)
            {
                if (Time.realtimeSinceStartup >= _nextLightSearch)
                {
                    _nextLightSearch = Time.realtimeSinceStartup + 0.5f;
                    _sceneLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                }

                var camPos = cam.transform.position;
                foreach (var l in _sceneLights)
                {
                    if (!l || !l.isActiveAndEnabled || l.intensity < 0.01f || l.range < 0.1f) continue;
                    if (l.type != LightType.Point && l.type != LightType.Spot) continue;

                    var distance = Vector3.Distance(camPos, l.transform.position);
                    if (distance > scatterDistance + l.range) continue;

                    // Nearest and biggest first.
                    var score = distance - l.range * 0.5f;
                    var slot = _lightCount;
                    if (slot == MaxLights)
                    {
                        if (score >= _lightScore[MaxLights - 1]) continue;
                        slot = MaxLights - 1;
                    }
                    else _lightCount++;

                    while (slot > 0 && _lightScore[slot - 1] > score)
                    {
                        _lightScore[slot] = _lightScore[slot - 1];
                        LightPos[slot] = LightPos[slot - 1];
                        LightCol[slot] = LightCol[slot - 1];
                        LightDir[slot] = LightDir[slot - 1];
                        slot--;
                    }

                    var tr = l.transform;
                    var colour = l.color.linear * Mathf.Min(l.intensity, intensityCap);
                    var p = tr.position;
                    _lightScore[slot] = score;
                    LightPos[slot] = new Vector4(p.x, p.y, p.z, l.range);

                    if (l.type == LightType.Spot)
                    {
                        var outer = Mathf.Cos(l.spotAngle * 0.5f * Mathf.Deg2Rad);
                        var inner = Mathf.Cos(Mathf.Min(l.innerSpotAngle, l.spotAngle - 1f) * 0.5f * Mathf.Deg2Rad);
                        var f = tr.forward;
                        LightDir[slot] = new Vector4(f.x, f.y, f.z, outer);
                        LightCol[slot] = new Vector4(colour.r, colour.g, colour.b, 1f / Mathf.Max(0.001f, inner - outer));
                    }
                    else
                    {
                        LightDir[slot] = new Vector4(0f, 0f, 1f, -1f);
                        LightCol[slot] = new Vector4(colour.r, colour.g, colour.b, 0f);
                    }
                }
            }

            Shader.SetGlobalFloat(LightCountId, _lightCount);
            Shader.SetGlobalFloat(ScatterId, litFog ? scatter : 0f);
            Shader.SetGlobalFloat(ScatterMaxId, scatterDistance);
            Shader.SetGlobalFloat(AnisoId, forwardScatter);
            if (_lightCount > 0)
            {
                Shader.SetGlobalVectorArray(LightPosId, LightPos);
                Shader.SetGlobalVectorArray(LightColId, LightCol);
                Shader.SetGlobalVectorArray(LightDirId, LightDir);
            }
        }
    }
}