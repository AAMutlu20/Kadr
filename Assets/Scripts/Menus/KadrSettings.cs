using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Menus
{
    public enum DisplayMode { Borderless, Exclusive, Windowed }

    /// <summary>
    /// The player's saved settings. Stored in PlayerPrefs and applied once at start-up in every scene, so they hold
    /// even if the game is started from another scene than the menu.
    ///
    /// Things the player's own code has to read (nothing here can do it for them):
    ///  - MouseSensitivity and InvertY: multiply / flip the mouse look.
    ///
    /// Render scale, anti-aliasing and shadow distance are changed on the URP asset. In the editor that would change
    /// the asset file for good, so the original values are put back when play mode ends.
    /// </summary>
    public static class KadrSettings
    {
        private const string VolumeKey = "kadr.volume";
        private const string SensitivityKey = "kadr.sensitivity";
        private const string InvertYKey = "kadr.inverty";
        private const string QualityKey = "kadr.quality";
        private const string ModeKey = "kadr.displaymode";
        private const string WidthKey = "kadr.width";
        private const string HeightKey = "kadr.height";
        private const string VSyncKey = "kadr.vsync";
        private const string FrameCapKey = "kadr.framecap";
        private const string RenderScaleKey = "kadr.renderscale";
        private const string MsaaKey = "kadr.msaa";
        private const string ShadowsKey = "kadr.shadows";

        public static readonly int[] FrameCaps = { 0, 30, 60, 90, 120, 144, 165, 240 };
        public static readonly int[] MsaaSteps = { 1, 2, 4, 8 };
        public static readonly float[] ShadowSteps = { 0.25f, 0.5f, 0.75f, 1f };
        public static readonly string[] ShadowNames = { "SHORT", "MEDIUM", "LONG", "FULL" };

        // ---------- audio ----------

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(VolumeKey, 0.8f);
            set
            {
                value = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(VolumeKey, value);
                AudioListener.volume = value;
            }
        }

        // ---------- controls ----------

        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(SensitivityKey, 1f);
            set => PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, 0.2f, 3f));
        }

        public static bool InvertY
        {
            get => PlayerPrefs.GetInt(InvertYKey, 0) == 1;
            set => PlayerPrefs.SetInt(InvertYKey, value ? 1 : 0);
        }

        // ---------- video ----------

        public static DisplayMode Mode
        {
            get => (DisplayMode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, DefaultMode()), 0, 2);
            set
            {
                PlayerPrefs.SetInt(ModeKey, (int)value);
                ApplyScreen();
            }
        }

        private static int DefaultMode() => Screen.fullScreen ? (int)DisplayMode.Borderless : (int)DisplayMode.Windowed;

        private static List<Vector2Int> _resolutions;

        /// <summary>Every resolution the screen offers (one per width and height, smallest first).</summary>
        public static IReadOnlyList<Vector2Int> Resolutions
        {
            get
            {
                if (_resolutions != null) return _resolutions;

                _resolutions = new List<Vector2Int>();
                foreach (var r in Screen.resolutions)
                {
                    if (r.width < 800 || r.height < 600) continue;

                    var size = new Vector2Int(r.width, r.height);
                    if (!_resolutions.Contains(size)) _resolutions.Add(size);
                }

                if (_resolutions.Count == 0) _resolutions.Add(new Vector2Int(Screen.width, Screen.height));
                _resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                return _resolutions;
            }
        }

        public static Vector2Int Resolution
        {
            get
            {
                var fallback = new Vector2Int(Screen.width, Screen.height);
                return new Vector2Int(PlayerPrefs.GetInt(WidthKey, fallback.x), PlayerPrefs.GetInt(HeightKey, fallback.y));
            }
            set
            {
                PlayerPrefs.SetInt(WidthKey, value.x);
                PlayerPrefs.SetInt(HeightKey, value.y);
                ApplyScreen();
            }
        }

        /// <summary>Index into Resolutions of the saved (or current) size, or the nearest one.</summary>
        public static int ResolutionIndex()
        {
            var wanted = Resolution;
            var list = Resolutions;
            var best = 0;
            var bestDistance = int.MaxValue;

            for (var i = 0; i < list.Count; i++)
            {
                var distance = Mathf.Abs(list[i].x - wanted.x) + Mathf.Abs(list[i].y - wanted.y);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = i;
            }

            return best;
        }

        public static bool VSync
        {
            get => PlayerPrefs.GetInt(VSyncKey, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
            set
            {
                PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
                ApplyFrameRate();
            }
        }

        /// <summary>0 = unlimited. Only matters with VSync off (VSync ties the game to the monitor).</summary>
        public static int FrameCap
        {
            get => PlayerPrefs.GetInt(FrameCapKey, 0);
            set
            {
                PlayerPrefs.SetInt(FrameCapKey, value);
                ApplyFrameRate();
            }
        }

        private static void ApplyScreen()
        {
            var mode = Mode switch
            {
                DisplayMode.Exclusive => FullScreenMode.ExclusiveFullScreen,
                DisplayMode.Windowed => FullScreenMode.Windowed,
                _ => FullScreenMode.FullScreenWindow
            };

            var size = PlayerPrefs.HasKey(WidthKey) ? Resolution : new Vector2Int(Screen.width, Screen.height);
            Screen.SetResolution(size.x, size.y, mode);
        }

        private static void ApplyFrameRate()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameCap <= 0 ? -1 : FrameCap;
        }

        // ---------- graphics ----------

        public static int Quality
        {
            get => PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel());
            set
            {
                value = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1);
                PlayerPrefs.SetInt(QualityKey, value);
                QualitySettings.SetQualityLevel(value, true);

                // A quality level can use another render pipeline asset, so the three values are put on the new one.
                ApplyUrp();
                ApplyFrameRate();
            }
        }

        private struct UrpOriginal
        {
            public float RenderScale;
            public int Msaa;
            public float ShadowDistance;
        }

        private static readonly Dictionary<UniversalRenderPipelineAsset, UrpOriginal> Originals = new();

        private static UniversalRenderPipelineAsset Urp
        {
            get
            {
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (asset && !Originals.ContainsKey(asset))
                {
                    Originals[asset] = new UrpOriginal
                    {
                        RenderScale = asset.renderScale,
                        Msaa = asset.msaaSampleCount,
                        ShadowDistance = asset.shadowDistance
                    };
                }

                return asset;
            }
        }

        /// <summary>False when the project does not use URP (the render scale, anti-aliasing and shadow lines are then left out).</summary>
        public static bool HasUrp => Urp;

        /// <summary>0.5 to 1.25. 1 = native resolution.</summary>
        public static float RenderScale
        {
            get => PlayerPrefs.GetFloat(RenderScaleKey, Urp ? Originals[Urp].RenderScale : 1f);
            set
            {
                PlayerPrefs.SetFloat(RenderScaleKey, Mathf.Clamp(Mathf.Round(value * 20f) / 20f, 0.5f, 1.25f));
                ApplyUrp();
            }
        }

        /// <summary>1 (off), 2, 4 or 8.</summary>
        public static int Msaa
        {
            get => PlayerPrefs.GetInt(MsaaKey, Urp ? Originals[Urp].Msaa : 1);
            set
            {
                PlayerPrefs.SetInt(MsaaKey, value);
                ApplyUrp();
            }
        }

        /// <summary>Index into ShadowSteps: a share of the shadow distance the project was made with.</summary>
        public static int ShadowLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(ShadowsKey, ShadowSteps.Length - 1), 0, ShadowSteps.Length - 1);
            set
            {
                PlayerPrefs.SetInt(ShadowsKey, Mathf.Clamp(value, 0, ShadowSteps.Length - 1));
                ApplyUrp();
            }
        }

        private static void ApplyUrp()
        {
            var asset = Urp;
            if (!asset) return;

            var original = Originals[asset];

            if (PlayerPrefs.HasKey(RenderScaleKey)) asset.renderScale = RenderScale;
            if (PlayerPrefs.HasKey(MsaaKey)) asset.msaaSampleCount = Msaa;
            if (PlayerPrefs.HasKey(ShadowsKey)) asset.shadowDistance = original.ShadowDistance * ShadowSteps[ShadowLevel];
        }

        private static void RestoreUrp()
        {
            foreach (var pair in Originals)
            {
                if (!pair.Key) continue;

                pair.Key.renderScale = pair.Value.RenderScale;
                pair.Key.msaaSampleCount = pair.Value.Msaa;
                pair.Key.shadowDistance = pair.Value.ShadowDistance;
            }
        }

        // ---------- saving and start-up ----------

        public static void Save() => PlayerPrefs.Save();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _resolutions = null;
            Originals.Clear();
            Application.quitting -= RestoreUrp;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyAtStart()
        {
#if UNITY_EDITOR
            Application.quitting -= RestoreUrp;
            Application.quitting += RestoreUrp;
#endif

            if (PlayerPrefs.HasKey(VolumeKey)) AudioListener.volume = MasterVolume;
            if (PlayerPrefs.HasKey(QualityKey)) QualitySettings.SetQualityLevel(Quality, true);

            ApplyUrp();

            if (PlayerPrefs.HasKey(VSyncKey) || PlayerPrefs.HasKey(FrameCapKey)) ApplyFrameRate();
            if (PlayerPrefs.HasKey(ModeKey) || PlayerPrefs.HasKey(WidthKey)) ApplyScreen();
        }
    }
}
