using System.Collections;
using System.Collections.Generic;
using Gameplay;
using Saving;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Weapons;

namespace Menus
{
    /// <summary>
    /// Esc pauses the game: time stops, the HUD goes away and the viewfinder menu comes over the frozen picture with
    /// RESUME, SETTINGS, MAIN MENU and QUIT GAME. MAIN MENU and QUIT GAME ask first. Esc inside the pause menu goes back
    /// one page, and on the first page it resumes.
    ///
    /// Put it on the Player. It never opens while something else has the game paused already (the inventory, the death
    /// debrief, any overlay that sets Time.timeScale to 0), while a scene is loading, or when the player is dead.
    ///
    /// MAIN MENU destroys the Player: it lives through scene loads, and the main menu scene must not get it.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        [Tooltip("The scene of the main menu. It has to be in the Build Settings.")]
        [SerializeField] private string mainMenuScene = "MainMenu";

        private const string Hint = "UP / DOWN  SELECT      ENTER  CONFIRM      ESC  BACK";

        private Health _health;
        private WeaponController _weapons;
        private PlayerController _player;

        private ViewfinderMenu _menu;
        private SettingsMenu _settings;
        private MenuPage _main;
        private readonly List<Canvas> _hidden = new();

        private bool _open;
        private bool _leaving;
        private bool _weaponsWereOn;
        private float _previousScale = 1f;
        private int _openFrame = -1;
        private int _closedFrame = -1;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _player = GetComponent<PlayerController>();
        }

        private void Start()
        {
            _weapons = FindAnyObjectByType<WeaponController>();
        }

        private void OnDestroy()
        {
            // Never leave the game frozen if the Player goes away while paused.
            if (_open) Time.timeScale = 1f;
        }

        private void Update()
        {
            if (_open || _leaving) return;

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

            // The same key press that just closed the menu must not open it again.
            if (Time.frameCount == _closedFrame) return;

            // Something else already has the game paused (inventory, debrief), a scene is loading, or we are dead.
            if (Time.timeScale == 0f || SceneLoader.IsBusy || (_health && _health.IsDead)) return;

            Open();
        }

        // ---------- open and close ----------

        private void Open()
        {
            _open = true;
            _openFrame = Time.frameCount;

            _previousScale = Time.timeScale;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (!_weapons) _weapons = FindAnyObjectByType<WeaponController>();
            _weaponsWereOn = _weapons && _weapons.InputEnabled;
            if (_weapons) _weapons.InputEnabled = false;

            // The player's own input (mouse look, walking) is frozen the same way the inventory does it.
            if (_player) _player.InputEnabled = false;

            // The HUD and every other overlay go away while the viewfinder is up. Done before the menu exists.
            _hidden.Clear();
            foreach (var canvas in FindObjectsByType<Canvas>())
            {
                if (!canvas.enabled || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
                canvas.enabled = false;
                _hidden.Add(canvas);
            }

            var go = new GameObject("PauseMenu", typeof(ViewfinderMenu));
            go.transform.SetParent(transform, false);
            _menu = go.GetComponent<ViewfinderMenu>();

            BuildPages();
            _menu.Show(_main);
        }

        private void Close()
        {
            if (!_open) return;

            KadrSettings.Save();

            foreach (var canvas in _hidden)
            {
                if (canvas) canvas.enabled = true;
            }

            _hidden.Clear();

            if (_menu) Destroy(_menu.gameObject);
            _menu = null;
            _settings = null;

            Time.timeScale = _previousScale > 0f ? _previousScale : 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (_weapons && _weaponsWereOn) _weapons.InputEnabled = true;

            // A dead player keeps input off, so closing must not switch anything back on.
            var dead = _health && _health.IsDead;
            if (_player && !dead) _player.InputEnabled = true;

            _open = false;
            _closedFrame = Time.frameCount;
        }

        // ---------- pages ----------

        private void BuildPages()
        {
            _main = new MenuPage { Heading = "ROLL 01  ·  PAUSED", Compact = true, Hint = Hint, Back = Resume };
            _main.Entries.Add(new MenuEntry { Label = "RESUME", Select = Resume });
            _main.Entries.Add(new MenuEntry { Label = "SETTINGS", Select = () => _menu.Show(_settings.Page) });
            _main.Entries.Add(new MenuEntry
            {
                Label = "MAIN MENU",
                Select = () => _menu.Show(Confirm("LEAVE THE RUN?", "MAIN MENU", TryMainMenu))
            });
            _main.Entries.Add(new MenuEntry
            {
                Label = "QUIT GAME",
                Select = () => _menu.Show(Confirm("QUIT THE GAME?", "QUIT GAME", () => StartCoroutine(Leave(QuitGame))))
            });

            _settings = SettingsMenu.Create(_menu, ShowMain);
        }

        private void ShowMain() => _menu.Show(_main);

        // Cancel comes first, so the focus starts on the safe choice.
        private MenuPage Confirm(string heading, string yesLabel, System.Action yes)
        {
            var page = new MenuPage { Heading = heading, Compact = true, Hint = Hint, Back = ShowMain };
            page.Entries.Add(new MenuEntry { Label = "CANCEL", Select = ShowMain });
            page.Entries.Add(new MenuEntry { Label = yesLabel, Select = yes });
            return page;
        }

        private void Resume()
        {
            // The Esc that opened the menu is still "pressed this frame" for the menu's own input.
            if (Time.frameCount == _openFrame) return;
            Close();
        }

        // ---------- leaving ----------

        private IEnumerator Leave(System.Action then)
        {
            _leaving = true;
            _menu.Interactable = false;
            KadrSettings.Save();
            SaveGame.AutoSave(false); // leaving or quitting keeps the progress

            yield return _menu.FadeOut(0.6f);
            then();
        }

        private void TryMainMenu()
        {
            if (!Application.CanStreamedLevelBeLoaded(mainMenuScene))
            {
                _menu.Message($"Scene \"{mainMenuScene}\" is not in the Build Settings.");
                Debug.LogError($"[PauseMenu] Scene \"{mainMenuScene}\" is not in the Build Settings.", this);
                return;
            }

            StartCoroutine(Leave(GoToMainMenu));
        }

        private void GoToMainMenu()
        {
            Time.timeScale = 1f;
            _open = false;

            SceneManager.LoadScene(mainMenuScene);
            Destroy(gameObject); // the Player persists through scene loads, the main menu must not get it
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}