using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Bosses;
using Core;
using Events;
using Generation;
using Items;
using Menus;
using Photography;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Weapons;

namespace Gameplay
{
    /// <summary>
    /// Death and respawn. Put it on the Player (next to Health). It does everything in this order:
    ///
    ///  1. Health.Died: grab the last frame, cut to black (no death animation), freeze time, hide the HUD.
    ///  2. Apply the penalties: every consumable is gone, every key item except the keep-list is gone, and the run's
    ///     paid photos pay only photoPayoutRate of their score. Done at once, so quitting during the debrief saves nothing.
    ///  3. Open the debrief (DeathScreen). CONTINUE or MAIN MENU.
    ///  4. CONTINUE: the film rewinds, then the Lobby scene loads under the black (SceneLoader, minimal style). The
    ///     Player persists (DontDestroyOnLoad), so it is moved to the PlayerSpawn in the Lobby, the album is reset, health
    ///     is full again and RunRestartedEvent is published. Then the black fades away.
    ///
    /// Progression (XP, camera points, camera upgrades, roubles) is never touched.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class DeathFlow : MonoBehaviour
    {
        [Header("Run")]
        [Tooltip("Shown in the debrief. Raise it per floor later.")]
        [SerializeField, Min(1)] private int floor = 1;
        [Tooltip("The scene the player wakes up in. It needs a PlayerSpawn object. Has to be in the Build Settings.")]
        [SerializeField] private string respawnScene = "Lobby";
        [Tooltip("The scene of the main menu, for the MAIN MENU button. It has to be in the Build Settings.")]
        [SerializeField] private string mainMenuScene = "MainMenu";

        [Header("What a death costs")]
        [Tooltip("The share of the paid photos' score that still gets paid. 0.5 = 'incomplete assignment'.")]
        [SerializeField, Range(0f, 1f)] private float photoPayoutRate = 0.5f;
        [Tooltip("Key items that survive a death: pistol, shotgun, camera.")]
        [SerializeField] private ItemDefinition[] keepItems;
        [Tooltip("Kept as well once the first floor is completed (RunProgress.FlashlightPermanent). Also handed back then if it is missing.")]
        [SerializeField] private ItemDefinition flashlightItem;

        [Header("Timing (seconds)")]
        [SerializeField, Min(0f)] private float blackSeconds = 0.8f;
        [SerializeField, Min(0.1f)] private float rewindSeconds = 1.8f;
        [SerializeField, Min(0.1f)] private float fadeInSeconds = 1.0f;

        private enum Choice { None, Continue, MainMenu }

        private Health _health;
        private Inventory _inventory;
        private CameraController _camera;
        private WeaponController _weapons;
        private LevelGenerator _generator;
        private PlayerStamina _stamina;
        private Flashlight _flashlight;

        private RunTracker _tracker;
        private DeathScreen _screen;
        private Canvas _coverCanvas;
        private Image _cover;
        private readonly List<Canvas> _hidden = new();

        private int _deaths;
        private bool _dying;
        private Choice _choice;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _inventory = GetComponent<Inventory>();
            _stamina = GetComponent<PlayerStamina>();

        }

        private void Start()
        {
            // Found here, not in Awake, so every other system has finished its own Awake.
            RefreshReferences();

            _tracker = new RunTracker(transform, _generator);
            _health.Died += OnDied;
        }

        // Whatever lives in the Player stays valid; whatever lives in a scene must be found again after a load.
        private void RefreshReferences()
        {
            if (!_inventory) _inventory = FindAnyObjectByType<Inventory>();
            if (!_camera) _camera = FindAnyObjectByType<CameraController>();
            if (!_weapons) _weapons = FindAnyObjectByType<WeaponController>();
            if (!_flashlight) _flashlight = FindAnyObjectByType<Flashlight>();
            _generator = FindAnyObjectByType<LevelGenerator>();
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        // The Player lives through scene loads, the scene's own objects do not: find the generator again, and start
        // counting a new run (time on the floor starts when the floor does).
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshReferences();
            if (_tracker == null || _dying) return;

            _tracker.Dispose();
            _tracker = new RunTracker(transform, _generator);
        }

        private void OnDestroy()
        {
            if (_health) _health.Died -= OnDied;
            _tracker?.Dispose();
            if (_dying) Time.timeScale = 1f; // never leave the game frozen in the next scene
        }

        private void Update()
        {
            if (!_dying) _tracker?.Tick();
        }

        // ---------- death ----------

        private void OnDied()
        {
            if (_dying) return;
            _dying = true;
            _choice = Choice.None;
            _deaths++;
            RunProgress.RegisterDeath();

            var summary = new RunSummary { Floor = floor, DeathNumber = _deaths };

            // Same frame as the death: what the player was looking at, without the HUD.
            summary.LastFrame = CaptureFrame();
            summary.Cause = CauseOfDeath();
            _tracker.FillSummary(summary);
            summary.FrameNumber = summary.PhotosTaken + 1;

            Freeze();
            ApplyCosts(summary);

            if (GameServices.TryGet(out EventBus bus)) bus.Publish(new PlayerDiedEvent(_deaths));

            StartCoroutine(Sequence(summary));
        }

        private string CauseOfDeath()
        {
            var source = _health.LastDamageSource;
            if (!source) return "UNKNOWN";

            var text = source.name.Replace("(Clone)", "").Replace('_', ' ').Trim();
            text = Regex.Replace(text, "(?<=[a-z0-9])(?=[A-Z])", " ");
            return text.Length == 0 ? "UNKNOWN" : text.ToUpperInvariant();
        }

        private static Texture2D CaptureFrame()
        {
            var cam = Camera.main;
            if (!cam) return null;

            const int width = 960;
            const int height = 540;

            var rt = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previousTarget;

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return image;
        }

        private void Freeze()
        {
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (_weapons) _weapons.InputEnabled = false;

            // Every other overlay goes dark: the HUD, the inventory, the photo viewfinder, the white flash of a boss.
            _hidden.Clear();
            foreach (var canvas in FindObjectsByType<Canvas>())
            {
                if (!canvas.enabled || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) continue;
                canvas.enabled = false;
                _hidden.Add(canvas);
            }

            // The black cover sits under the debrief (the viewfinder is at 10).
            _coverCanvas = MenuUi.MakeCanvas("DeathCover", transform, 9);
            _cover = MenuUi.Box("Black", _coverCanvas.transform, Color.black);
            MenuUi.Stretch(_cover.rectTransform);
        }

        private void ApplyCosts(RunSummary summary)
        {
            // Photos: the paid ones pay a share. The album itself is only wiped on respawn.
            var album = _camera ? _camera.Album : null;
            if (album != null)
            {
                var total = album.EstimatedPayout();
                summary.PayoutRate = photoPayoutRate;
                summary.PayoutTotal = Mathf.RoundToInt(total);
                summary.Filed = Mathf.RoundToInt(total * photoPayoutRate);
                summary.Forfeited = Mathf.Max(0, summary.PayoutTotal - summary.Filed);
                PlayerWallet.Add(summary.Filed);
            }

            summary.Balance = PlayerWallet.Roubles;

            if (!_inventory) return;

            // Consumables: all of them.
            foreach (var stack in new List<ItemStack>(_inventory.Consumables.Stacks))
            {
                summary.ConsumablesLost += stack.Count;
                _inventory.Consumables.RemoveCount(stack, stack.Count);
            }

            // Key items: everything except the keep-list (and the flashlight once it is permanent).
            var keep = new HashSet<ItemDefinition>();
            if (keepItems != null)
            {
                foreach (var item in keepItems)
                {
                    if (item) keep.Add(item);
                }
            }

            if (flashlightItem && RunProgress.FlashlightPermanent) keep.Add(flashlightItem);

            foreach (var stack in new List<ItemStack>(_inventory.Permanent.Stacks))
            {
                if (keep.Contains(stack.Definition)) continue;

                summary.KeyItemsLost.Add(stack.Definition.DisplayName);
                _inventory.Permanent.RemoveCount(stack, stack.Count);
            }
        }

        // ---------- the sequence ----------

        private IEnumerator Sequence(RunSummary summary)
        {
            yield return new WaitForSecondsRealtime(blackSeconds); // silence and black

            var go = new GameObject("DeathScreen", typeof(ViewfinderMenu), typeof(DeathScreen));
            go.transform.SetParent(transform, false);
            _screen = go.GetComponent<DeathScreen>();
            _screen.Open(summary, () => _choice = Choice.Continue, () => _choice = Choice.MainMenu);

            while (_choice == Choice.None) yield return null;

            if (_choice == Choice.MainMenu)
            {
                yield return _screen.Leave(0.6f);
                Time.timeScale = 1f;
                _dying = false;

                if (Application.CanStreamedLevelBeLoaded(mainMenuScene))
                {
                    SceneManager.LoadScene(mainMenuScene);
                    Destroy(gameObject); // the Player persists through scene loads, the main menu must not get it
                }
                else
                {
                    Debug.LogWarning($"[DeathFlow] Scene \"{mainMenuScene}\" is not in the Build Settings.", this);
                }

                yield break;
            }

            yield return _screen.Rewind(rewindSeconds);

            // The Lobby loads under the black. Time stays frozen until the Lobby is set up.
            if (Application.CanStreamedLevelBeLoaded(respawnScene))
            {
                yield return SceneLoader.LoadAndHold(respawnScene, "LOBBY", LoadStyle.Minimal);
            }
            else
            {
                Debug.LogError($"[DeathFlow] Scene \"{respawnScene}\" is not in the Build Settings. Respawning in place.", this);
            }

            Respawn();

            _screen.Close();
            Destroy(_screen.gameObject);
            _screen = null;
            Destroy(_coverCanvas.gameObject); // the loading screen's black is over everything now

            foreach (var canvas in _hidden)
            {
                if (canvas) canvas.enabled = true; // the ones of the old scene are gone, the Player's own come back
            }

            _hidden.Clear();

            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (_weapons) _weapons.InputEnabled = true;

            yield return SceneLoader.Release(fadeInSeconds);
            _dying = false;
        }

        private void Respawn()
        {
            // The pictures of the old run are not needed any more.
            _camera?.Album.Reset();

            PlayerSpawn.TeleportPlayer(transform);

            _health.ResetToFull();
            if (_stamina) _stamina.Restore(1000000f);
            if (_flashlight) _flashlight.Recharge();
            if (_weapons) _weapons.ResetLoadout();

            // Buffs and healing are timed effects that must not carry over. Both come back by themselves when needed.
            var buffs = GetComponent<PlayerBuffs>();
            if (buffs) Destroy(buffs);
            var healer = GetComponent<HealOverTime>();
            if (healer) Destroy(healer);
            var status = GetComponent<PlayerStatus>();
            if (status) status.SetBlind(0f);

            if (_inventory && flashlightItem && RunProgress.FlashlightPermanent
                && _inventory.GridFor(flashlightItem.Category).Find(flashlightItem) == null)
            {
                _inventory.TryAdd(flashlightItem);
            }

            _tracker?.Dispose();
            _tracker = new RunTracker(transform, _generator);

            if (GameServices.TryGet(out EventBus bus)) bus.Publish(new RunRestartedEvent(_deaths));
        }
    }
}