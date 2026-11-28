using System;
using System.Collections.Generic;
using System.IO;
using Gameplay;
using Generation;
using Items;
using Photography;
using UnityEngine;

namespace Saving
{
    /// <summary>Where a slot sends the player, and what the loading screen says on the way.</summary>
    public readonly struct StartPlan
    {
        public readonly string Scene;
        public readonly string Destination;
        public readonly string Brief; // null = the loading screen picks its own line

        public StartPlan(string scene, string destination, string brief)
        {
            Scene = scene;
            Destination = destination;
            Brief = brief;
        }
    }

    /// <summary>
    /// The save files: three slots, each a folder under Application.persistentDataPath/saves with a save.json and the
    /// PNGs of the photos of a phone save. Everything that needs scene objects (the inventory, the album, the player)
    /// is in SaveGame; this class is files, slots and the statics that survive scene loads.
    ///
    /// A slot keeps two things:
    ///  - progress: roubles, XP, camera upgrades, floors, deaths and the inventory. Written automatically on death and
    ///    when leaving to the main menu or quitting.
    ///  - a checkpoint (optional): written by a telephone. Loading a slot with a checkpoint resumes in that floor at that
    ///    phone; without one it starts in the Lobby. A death ends the run and deletes the checkpoint.
    /// </summary>
    public static class SaveSystem
    {
        public const int SlotCount = 3;
        public const string DefaultLobbyBrief = null;
        private const string ResumeBrief = "THE LINE WENT QUIET. YOU ARE BACK WHERE YOU CALLED FROM.";

        /// <summary>The slot being played, or -1 (testing a scene directly: nothing is saved).</summary>
        public static int ActiveSlot { get; private set; } = -1;

        /// <summary>Set by Begin, taken by SaveGame once the scene is up.</summary>
        private static SaveFile _pending;

        /// <summary>Phones already used in the run being played. Keyed by floor seed and place, so a new floor starts clean.</summary>
        public static readonly HashSet<string> UsedPhones = new();

        /// <summary>True when the slot being played was loaded from a file (not a new game). The testing items are not given then.</summary>
        public static bool LoadedFromSave { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ActiveSlot = -1;
            _pending = null;
            UsedPhones.Clear();
            LoadedFromSave = false;
            LevelGenerator.PendingSeed = null;
        }

        // ---------- files ----------

        private static string Root => Path.Combine(Application.persistentDataPath, "saves");
        private static string SlotDir(int slot) => Path.Combine(Root, "slot" + (slot + 1));
        private static string FilePath(int slot) => Path.Combine(SlotDir(slot), "save.json");

        public static bool Exists(int slot) => slot >= 0 && slot < SlotCount && File.Exists(FilePath(slot));

        public static SaveFile Read(int slot)
        {
            if (!Exists(slot)) return null;

            try
            {
                var file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(FilePath(slot)));
                if (file != null && file.progress == null) file.progress = new ProgressSave();
                return file;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] Slot {slot + 1} could not be read: {e.Message}");
                return null;
            }
        }

        public static void Write(int slot, SaveFile file)
        {
            if (slot < 0 || slot >= SlotCount || file == null) return;

            try
            {
                Directory.CreateDirectory(SlotDir(slot));
                file.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

                // Write beside the file first, so a crash halfway never leaves a broken save.
                var path = FilePath(slot);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(file, true));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Could not write slot {slot + 1}: {e.Message}");
            }
        }

        public static void Delete(int slot)
        {
            try
            {
                if (Directory.Exists(SlotDir(slot))) Directory.Delete(SlotDir(slot), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] Could not delete slot {slot + 1}: {e.Message}");
            }
        }

        // ---------- starting a slot ----------

        /// <summary>
        /// Picks the slot, puts the saved progress into the game's statics and works out where to go. A new game wipes the
        /// slot first. Call SaveGame.ApplyPending() once the scene is up to hand over the inventory and the checkpoint.
        /// </summary>
        public static StartPlan Begin(int slot, bool newGame, string lobbyScene)
        {
            ActiveSlot = slot;
            _pending = null;
            UsedPhones.Clear();
            LoadedFromSave = false;
            LevelGenerator.PendingSeed = null;
            WorldState.Prepare(null);

            var lobby = new StartPlan(lobbyScene, "LOBBY", DefaultLobbyBrief);

            var file = newGame ? null : Read(slot);
            if (file == null)
            {
                Delete(slot);
                ResetProgression();
                Write(slot, new SaveFile());
                return lobby;
            }

            ApplyProgress(file.progress);
            _pending = file;
            LoadedFromSave = true;

            var cp = file.hasCheckpoint ? file.checkpoint : null;
            if (cp == null || string.IsNullOrEmpty(cp.scene) || !Application.CanStreamedLevelBeLoaded(cp.scene)) return lobby;

            if (cp.hasSeed) LevelGenerator.PendingSeed = cp.seed;
            WorldState.Prepare(cp); // the spawner places the enemies that were alive, not new ones

            return new StartPlan(cp.scene, "THE FLOOR", ResumeBrief);
        }

        /// <summary>Hands the loaded slot to SaveGame (once).</summary>
        public static SaveFile TakePending()
        {
            var file = _pending;
            _pending = null;
            return file;
        }

        // ---------- the statics ----------

        public static void ResetProgression()
        {
            PlayerWallet.Set(0);
            PlayerXp.Import(0, 0, 0, null);
            CameraProgress.Current.Import(CameraBranch.None, 0, CameraAbility.None, null);
            RunProgress.Restore(0, 0);
        }

        public static void ApplyProgress(ProgressSave p)
        {
            if (p == null)
            {
                ResetProgression();
                return;
            }

            PlayerWallet.Set(p.roubles);
            PlayerXp.Import(p.xpProgress, p.xpPoints, p.xpPointsEarned, p.discovered);
            CameraProgress.Current.Import((CameraBranch)p.cameraBranch, p.cameraTier, (CameraAbility)p.cameraEquipped, p.abilityLevels);
            RunProgress.Restore(p.floorsCompleted, p.deaths);
        }

        public static ProgressSave CaptureProgress()
        {
            var p = new ProgressSave { roubles = PlayerWallet.Roubles };

            PlayerXp.Export(out p.xpProgress, out p.xpPoints, out p.xpPointsEarned, out var discovered);
            p.discovered = new List<string>(discovered);

            var camera = CameraProgress.Current;
            p.cameraBranch = (int)camera.Branch;
            p.cameraTier = camera.BranchTier;
            p.cameraEquipped = (int)camera.Equipped;
            p.abilityLevels = camera.ExportLevels();

            p.floorsCompleted = RunProgress.FloorsCompleted;
            p.deaths = RunProgress.Deaths;
            return p;
        }

        // ---------- the inventory ----------

        public static List<StackSave> CaptureStacks(InventoryGrid grid)
        {
            var list = new List<StackSave>();
            foreach (var stack in grid.Stacks)
            {
                list.Add(new StackSave
                {
                    id = stack.Definition.Id, count = stack.Count, x = stack.X, y = stack.Y, rotated = stack.Rotated
                });
            }

            return list;
        }

        /// <summary>Empties the grid and puts the saved stacks back where they were. Unknown items are skipped with a warning.</summary>
        public static void RestoreStacks(InventoryGrid grid, List<StackSave> stacks)
        {
            foreach (var stack in new List<ItemStack>(grid.Stacks)) grid.RemoveCount(stack, stack.Count);
            if (stacks == null) return;

            var catalog = ItemCatalog.Instance;
            foreach (var saved in stacks)
            {
                var definition = catalog ? catalog.Find(saved.id) : null;
                if (!definition)
                {
                    Debug.LogWarning($"[SaveSystem] Item \"{saved.id}\" is not in the ItemCatalog, skipped.");
                    continue;
                }

                var count = Mathf.Clamp(saved.count, 1, definition.MaxStack);
                var piece = new ItemStack { Definition = definition, Count = count, X = saved.x, Y = saved.y, Rotated = saved.rotated };
                if (!grid.TryPlaceDetached(piece, saved.x, saved.y, saved.rotated)) grid.Add(definition, count);
            }
        }

        // ---------- the photos of a checkpoint ----------

        public static void DeletePhotoFiles(int slot)
        {
            try
            {
                if (!Directory.Exists(SlotDir(slot))) return;
                foreach (var path in Directory.GetFiles(SlotDir(slot), "photo_*.png")) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] Could not clear the photos of slot {slot + 1}: {e.Message}");
            }
        }

        public static List<PhotoSave> CapturePhotos(PhotoAlbum album, int slot)
        {
            var list = new List<PhotoSave>();
            Directory.CreateDirectory(SlotDir(slot));
            DeletePhotoFiles(slot);

            var index = 0;
            foreach (var photo in album.Photos)
            {
                var saved = new PhotoSave
                {
                    file = "photo_" + index + ".png", score = photo.Score, pinned = photo.Pinned, number = photo.Number
                };

                foreach (var s in photo.Subjects)
                {
                    saved.subjects.Add(new SubjectSave
                    {
                        id = s.Id, name = s.Name, rarity = s.Rarity, danger = s.Danger, coverage = s.Coverage,
                        framing = s.Framing, state = s.State, special = s.Special, repeat = s.Repeat, score = s.Score
                    });
                }

                if (photo.Image) File.WriteAllBytes(Path.Combine(SlotDir(slot), saved.file), photo.Image.EncodeToPNG());
                list.Add(saved);
                index++;
            }

            return list;
        }

        public static List<PhotoData> LoadPhotos(CheckpointSave checkpoint, int slot)
        {
            var list = new List<PhotoData>();
            if (checkpoint?.photos == null) return list;

            foreach (var saved in checkpoint.photos)
            {
                var photo = new PhotoData { Score = saved.score, Pinned = saved.pinned, Number = saved.number };

                foreach (var s in saved.subjects)
                {
                    photo.Subjects.Add(new PhotoSubjectScore
                    {
                        Id = s.id, Name = s.name, Rarity = s.rarity, Danger = s.danger, Coverage = s.coverage,
                        Framing = s.framing, State = s.state, Special = s.special, Repeat = s.repeat, Score = s.score
                    });
                }

                var path = Path.Combine(SlotDir(slot), saved.file ?? "");
                if (File.Exists(path))
                {
                    var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (image.LoadImage(File.ReadAllBytes(path))) photo.Image = image;
                    else UnityEngine.Object.Destroy(image);
                }

                list.Add(photo);
            }

            return list;
        }
    }
}