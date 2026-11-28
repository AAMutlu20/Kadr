using System.Collections;
using Saving;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Menus
{
    /// <summary>
    /// The main menu scene. Put this on one empty GameObject in an otherwise empty scene (plus a camera, and whatever you
    /// want to see behind the menu: the dark lobby elevator, say). It builds the viewfinder menu, the settings page and the
    /// intro by itself.
    ///
    /// The intro: black, then a camera flash in a tilted square that brightens and dims like a developing photograph. The
    /// square holds whatever is in the middle of the screen, then opens up to the whole screen and the menu appears.
    /// Any key or click skips it.
    ///
    /// The scene to start must be in File > Build Settings.
    /// </summary>
    [RequireComponent(typeof(ViewfinderMenu))]
    public class MainMenu : MonoBehaviour
    {
        [Header("Game")]
        [Tooltip("The scene that starts when you press DESCEND. It has to be in the Build Settings.")]
        [SerializeField] private string gameScene = "Lobby";

        [Header("Intro")]
        [SerializeField] private bool playIntro = true;
        [Tooltip("Optional. The camera flash and the winding of the film.")]
        [SerializeField] private AudioClip flashClip;
        [Tooltip("Optional. Plays on a loop under the menu.")]
        [SerializeField] private AudioClip ambience;
        [SerializeField, Range(0f, 1f)] private float ambienceVolume = 0.5f;

        [Header("Camera (optional)")]
        [Tooltip("Slowly drifts, and leans a little towards the mouse. Empty = the main camera.")]
        [SerializeField] private Camera menuCamera;
        [SerializeField] private bool driftCamera = true;
        [SerializeField, Range(0f, 3f)] private float driftDegrees = 1f;
        [SerializeField, Range(0f, 5f)] private float mouseParallaxDegrees = 2f;

        private ViewfinderMenu _menu;
        private SaveSlotMenu _slots;
        private MenuPage _main;
        private MenuPage _settings;
        private bool _busy;

        // intro
        private Canvas _introCanvas;
        private Image _introFlash;
        private FrameGraphic _blackout;
        private FrameGraphic _border;

        // camera
        private Quaternion _baseRotation;
        private float _driftTime;
        private Vector2 _smoothMouse;

        private const float TiltDegrees = -9f;
        private const float StartHalf = 250f;   // half the side of the little square, in canvas units
        private const float EndHalf = 3200f;    // bigger than the screen

        private void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _menu = GetComponent<ViewfinderMenu>();
            if (!_menu) _menu = gameObject.AddComponent<ViewfinderMenu>();
            BuildPages();

            if (!menuCamera) menuCamera = Camera.main;
            if (menuCamera) _baseRotation = menuCamera.transform.localRotation;

            if (ambience)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.clip = ambience;
                source.loop = true;
                source.volume = ambienceVolume;
                source.spatialBlend = 0f;
                source.Play();
            }

            _menu.Show(_main, false);

            if (playIntro) BuildIntro();
            else _menu.Interactable = true;
        }

        private void Start()
        {
            if (playIntro) StartCoroutine(RunIntro());
        }

        // ---------- pages ----------

        private void BuildPages()
        {
            // The slot menu first: DESCEND opens it.
            _slots = new SaveSlotMenu(_menu, ShowMain, StartSlot);

            _main = new MenuPage { Heading = "ROLL 01" };
            _main.Entries.Add(new MenuEntry { Label = "DESCEND", Sub = "СПУСК", Select = () => _slots.Show() });
            _main.Entries.Add(new MenuEntry { Label = "SETTINGS", Sub = "НАСТРОЙКИ", Select = () => _menu.Show(_settings) });
            _main.Entries.Add(new MenuEntry { Label = "QUIT", Sub = "ВЫХОД", Select = Quit });

            _settings = SettingsMenu.Create(_menu, ShowMain).Page;
        }

        private void ShowMain()
        {
            KadrSettings.Save();
            _menu.Show(_main);
        }

        // ---------- actions ----------

        // Called by the slot menu: the slot to play and whether it starts from scratch.
        private void StartSlot(int slot, bool newGame)
        {
            if (_busy) return;

            if (!Application.CanStreamedLevelBeLoaded(gameScene))
            {
                _menu.Message($"Scene \"{gameScene}\" is not in the Build Settings.");
                Debug.LogWarning($"[MainMenu] Scene \"{gameScene}\" is not in the Build Settings (File > Build Settings).", this);
                return;
            }

            StartCoroutine(Begin(slot, newGame));
        }

        private IEnumerator Begin(int slot, bool newGame)
        {
            _busy = true;
            _menu.Interactable = false;
            KadrSettings.Save();

            yield return new WaitForSecondsRealtime(0.25f); // the shutter flash plays

            // The slot decides where we go: the Lobby, or the floor of a telephone save.
            var plan = SaveSystem.Begin(slot, newGame, gameScene);

            // The loading screen brings its own fade to black, so the menu does not fade out first.
            // SaveGame.ApplyPending runs under the black once the scene is up.
            SceneLoader.Load(plan.Scene, plan.Destination, LoadStyle.Full, SaveGame.ApplyPending,
                "ROLL " + (slot + 1).ToString("00"), "MENU", plan.Brief ?? "REPORT TO THE COMISAR. THE ELEVATOR IS WAITING.");
        }

        private void Quit()
        {
            if (_busy) return;

            StartCoroutine(Leave(() =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }));
        }

        private IEnumerator Leave(System.Action then)
        {
            _busy = true;
            _menu.Interactable = false;
            KadrSettings.Save();

            yield return new WaitForSecondsRealtime(0.25f); // the shutter flash plays
            yield return _menu.FadeOut(0.6f);
            then();
        }

        // ---------- intro ----------

        private void BuildIntro()
        {
            // Above the menu. Everything is black until the flash, so the very first frame is already black.
            _introCanvas = MenuUi.MakeCanvas("IntroCanvas", transform, 30);

            _introFlash = MenuUi.Box("Flash", _introCanvas.transform, Color.white);
            MenuUi.Stretch(_introFlash.rectTransform);

            _blackout = FrameGraphic.Create("Blackout", _introCanvas.transform, Color.black);
            _border = FrameGraphic.Create("Border", _introCanvas.transform, MenuUi.Paper);

            SetIntro(0f, TiltDegrees, 0f, 0f);

            _menu.Interactable = false;
            _menu.ChromeGroup.alpha = 0f;
            _menu.TitleGroup.alpha = 0f;
            _menu.PageGroup.alpha = 0f;
        }

        /// <summary>half = size of the square (0 = all black). The flash is white over the whole screen, but the black frame hides all of it except the square.</summary>
        private void SetIntro(float half, float tilt, float borderAlpha, float flashAlpha)
        {
            var inner = new Vector2(half, half);
            _blackout.Set(new Vector2(EndHalf * 1.6f, EndHalf * 1.6f), 0f, inner, tilt);

            var borderColor = MenuUi.Paper;
            borderColor.a = borderAlpha;
            _border.color = borderColor;
            _border.Set(inner + new Vector2(16f, 16f), tilt, inner, tilt);

            _introFlash.color = new Color(1f, 1f, 1f, flashAlpha);
        }

        private IEnumerator RunIntro()
        {
            const float blackSeconds = 0.9f;
            const float expandAt = 2.7f;
            const float expandSeconds = 0.9f;
            const float totalSeconds = 4.0f;

            var flashed = false;
            var t = 0f;

            while (t < totalSeconds && !SkipPressed(t))
            {
                t += Time.unscaledDeltaTime;

                if (t < blackSeconds)
                {
                    SetIntro(0f, TiltDegrees, 0f, 0f);
                }
                else
                {
                    if (!flashed)
                    {
                        flashed = true;
                        PlayOnce(flashClip);
                    }

                    var local = t - blackSeconds;
                    var exposure = Exposure(local);

                    var half = StartHalf * (1f + 0.05f * Mathf.Clamp01(local / 1.8f)); // the square breathes
                    var tilt = TiltDegrees;
                    var border = 1f;

                    if (t > expandAt)
                    {
                        var k = Mathf.Clamp01((t - expandAt) / expandSeconds);
                        var eased = k * k * (3f - 2f * k);

                        half = Mathf.Lerp(half, EndHalf, eased * eased); // slow start, then it opens fast
                        tilt = Mathf.Lerp(TiltDegrees, 0f, eased);
                        border = 1f - Mathf.Clamp01(k / 0.35f);
                    }

                    SetIntro(half, tilt, border, exposure);
                }

                // The menu develops after the square has opened.
                _menu.ChromeGroup.alpha = Mathf.Clamp01((t - 2.8f) / 0.8f);
                _menu.TitleGroup.alpha = Mathf.Clamp01((t - 3.0f) / 0.7f);
                _menu.PageGroup.alpha = Mathf.Clamp01((t - 3.3f) / 0.6f);

                yield return null;
            }

            FinishIntro();
        }

        /// <summary>
        /// The white of the flash over time: a hard pop, a dip, a second glow and a third, fading out, like a print
        /// coming up in the tray.
        /// </summary>
        private static float Exposure(float t)
        {
            // (time, brightness)
            float[,] keys =
            {
                { 0.00f, 1.00f }, { 0.30f, 0.40f }, { 0.55f, 0.80f },
                { 0.85f, 0.25f }, { 1.05f, 0.50f }, { 1.60f, 0.00f }
            };

            if (t <= 0f) return keys[0, 1];

            for (var i = 0; i < keys.GetLength(0) - 1; i++)
            {
                if (t > keys[i + 1, 0]) continue;

                var k = Mathf.InverseLerp(keys[i, 0], keys[i + 1, 0], t);
                return Mathf.Lerp(keys[i, 1], keys[i + 1, 1], k * k * (3f - 2f * k));
            }

            return 0f;
        }

        private bool SkipPressed(float t)
        {
            if (t < 0.15f) return false;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            return (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                   || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        }

        private void FinishIntro()
        {
            if (_introCanvas) Destroy(_introCanvas.gameObject);
            _menu.ChromeGroup.alpha = 1f;
            _menu.TitleGroup.alpha = 1f;
            _menu.PageGroup.alpha = 1f;
            _menu.Interactable = !_busy;
        }

        private void PlayOnce(AudioClip clip)
        {
            if (!clip) return;

            var source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.PlayOneShot(clip);
            Destroy(source, clip.length + 0.5f);
        }

        // ---------- the camera behind the menu ----------

        private void LateUpdate()
        {
            if (!driftCamera || !menuCamera) return;

            var dt = Time.unscaledDeltaTime;
            _driftTime += dt * 0.12f;

            var pitch = (Mathf.PerlinNoise(_driftTime, 0.3f) - 0.5f) * 2f * driftDegrees;
            var yaw = (Mathf.PerlinNoise(0.7f, _driftTime) - 0.5f) * 2f * driftDegrees * 1.4f;
            var roll = (Mathf.PerlinNoise(_driftTime, 5.1f) - 0.5f) * 2f * driftDegrees * 0.4f;

            var mouse = Mouse.current;
            var m = Vector2.zero;
            if (mouse != null)
            {
                var p = mouse.position.ReadValue();
                m = new Vector2(p.x / Screen.width - 0.5f, p.y / Screen.height - 0.5f) * 2f;
            }

            _smoothMouse = Vector2.Lerp(_smoothMouse, m, 1f - Mathf.Exp(-3f * dt));

            menuCamera.transform.localRotation = _baseRotation *
                Quaternion.Euler(pitch - _smoothMouse.y * mouseParallaxDegrees, yaw + _smoothMouse.x * mouseParallaxDegrees, roll);
        }
    }
}