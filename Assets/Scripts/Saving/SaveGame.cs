using System.Collections.Generic;
using Core;
using Events;
using Gameplay;
using Generation;
using Items;
using Photography;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Saving
{
    /// <summary>
    /// The player's side of saving. Put it on the Player (with Health and Inventory). It reads the scene objects that
    /// SaveSystem cannot reach (inventory, photo album, position, health) and does the three kinds of save:
    ///
    ///   SaveGame.AutoSave(runEnded)   progress only. DeathFlow calls it after a death (runEnded = true: the run is over,
    ///                                 so the telephone checkpoint is deleted), the pause menu when leaving or quitting.
    ///   SaveGame.Instance.QuickSave   a telephone: progress and a checkpoint (floor seed, position, health, inventory,
    ///                                 photos). See SavePhone.
    ///   SaveGame.ApplyPending()       after loading a slot, once the scene is built and the screen is still black:
    ///                                 puts the inventory back and, for a checkpoint, the photos, health and position.
    ///
    /// Nothing is saved when no slot is active (a scene started directly in the editor).
    /// </summary>
    public class SaveGame : MonoBehaviour
    {
        public static SaveGame Instance { get; private set; }

        private Inventory _inventory;
        private Health _health;
        private CameraController _camera;
        private void Awake()
        {
            Instance = this;
            _health = GetComponent<Health>();
            _inventory = GetComponent<Inventory>();
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _camera = null;

            // A phone in a scene without a generated floor (the Lobby) has no floor seed to tell one visit from the next,
            // so it works again every time you arrive. Phones on a generated floor are keyed by the floor's seed.
            var prefix = scene.name + ":0:";
            SaveSystem.UsedPhones.RemoveWhere(key => key.StartsWith(prefix));
        }

        private void Resolve()
        {
            if (!_inventory) _inventory = FindAnyObjectByType<Inventory>();
            if (!_camera) _camera = FindAnyObjectByType<CameraController>();
        }

        // ---------- saving ----------

        private ProgressSave BuildProgress()
        {
            var progress = SaveSystem.CaptureProgress();
            if (_inventory)
            {
                progress.consumables = SaveSystem.CaptureStacks(_inventory.Consumables);
                progress.permanent = SaveSystem.CaptureStacks(_inventory.Permanent);
            }

            return progress;
        }

        /// <summary>Progress only. runEnded = the player died: the run is over and its checkpoint is deleted.</summary>
        public static void AutoSave(bool runEnded)
        {
            if (Instance) Instance.DoAutoSave(runEnded);
        }

        private void DoAutoSave(bool runEnded)
        {
            var slot = SaveSystem.ActiveSlot;
            if (slot < 0) return;

            Resolve();
            var file = SaveSystem.Read(slot) ?? new SaveFile();
            file.progress = BuildProgress();

            if (runEnded)
            {
                file.hasCheckpoint = false;
                file.checkpoint = null;
                SaveSystem.DeletePhotoFiles(slot);
            }

            SaveSystem.Write(slot, file);
        }

        /// <summary>A telephone save. phoneKey marks that phone as used. False with a message when it cannot save now.</summary>
        public bool QuickSave(string phoneKey, out string message)
        {
            var slot = SaveSystem.ActiveSlot;
            if (slot < 0)
            {
                message = "No save slot. Start the game from the main menu.";
                return false;
            }

            Resolve();
            SaveSystem.UsedPhones.Add(phoneKey);

            var file = SaveSystem.Read(slot) ?? new SaveFile();
            file.progress = BuildProgress();

            var generator = FindAnyObjectByType<LevelGenerator>();
            var position = transform.position;
            var checkpoint = new CheckpointSave
            {
                scene = SceneManager.GetActiveScene().name,
                hasSeed = generator && generator.Current != null,
                seed = generator && generator.Current != null ? generator.Current.Seed : 0,
                x = position.x, y = position.y, z = position.z,
                yaw = transform.eulerAngles.y,
                health = _health ? _health.Current : 0f,
                consumables = file.progress.consumables,
                permanent = file.progress.permanent,
                usedPhones = new List<string>(SaveSystem.UsedPhones)
            };

            var album = _camera ? _camera.Album : null;
            if (album != null)
            {
                checkpoint.photos = SaveSystem.CapturePhotos(album, slot);
                foreach (var pair in album.ShotsOfType) checkpoint.shots.Add(new ShotCount { id = pair.Key, shots = pair.Value });
                checkpoint.shotsTaken = album.ShotsTaken;
            }

            WorldState.Capture(checkpoint);

            file.checkpoint = checkpoint;
            file.hasCheckpoint = true;
            SaveSystem.Write(slot, file);

            message = "Progress saved.";
            return true;
        }

        // ---------- loading ----------

        /// <summary>Call when the scene is ready and the screen is still black. Does nothing if no slot is waiting.</summary>
        public static void ApplyPending()
        {
            if (Instance) Instance.Apply();
        }

        private void Apply()
        {
            var file = SaveSystem.TakePending();
            if (file == null) return;

            Resolve();

            var checkpoint = file.hasCheckpoint ? file.checkpoint : null;
            if (checkpoint != null && checkpoint.scene != SceneManager.GetActiveScene().name) checkpoint = null;

            // The inventory: a checkpoint carries its own, otherwise the one from the last automatic save.
            if (_inventory)
            {
                SaveSystem.RestoreStacks(_inventory.Consumables, checkpoint != null ? checkpoint.consumables : file.progress.consumables);
                SaveSystem.RestoreStacks(_inventory.Permanent, checkpoint != null ? checkpoint.permanent : file.progress.permanent);
            }

            if (checkpoint == null) return;

            if (_camera)
            {
                var shots = new Dictionary<string, int>();
                foreach (var entry in checkpoint.shots) shots[entry.id] = entry.shots;
                _camera.Album.Restore(SaveSystem.LoadPhotos(checkpoint, SaveSystem.ActiveSlot), shots, checkpoint.shotsTaken);
            }

            if (_health) _health.Restore(checkpoint.health);

            PlayerSpawn.MoveTo(transform, new Vector3(checkpoint.x, checkpoint.y, checkpoint.z), Quaternion.Euler(0f, checkpoint.yaw, 0f));

            // Phones already used in this run (after the scene load, which clears the Lobby's).
            if (checkpoint.usedPhones != null)
            {
                foreach (var key in checkpoint.usedPhones) SaveSystem.UsedPhones.Add(key);
            }

            WorldState.Apply(checkpoint);
        }
    }
}