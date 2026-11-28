using Core;
using Events;
using Generation;
using Saving;
using UnityEngine;
using UnityEngine.Events;

namespace Gameplay
{
    /// <summary>
    /// A telephone that saves the game. Look at it and press the Interact key, the same as for a door. It works ONCE per
    /// run: after that it says the line is dead. Put it on the phone object or its root, with a collider on it or a child
    /// so the player's look-ray can hit it.
    ///
    /// A phone save stores the floor (by its seed), where the player stands, health, inventory and photos. Loading the
    /// slot later puts the player back here. Enemies, doors and the boss start fresh. It does not work during the boss
    /// fight, and not when the game was not started from a save slot.
    ///
    /// Hook sound, a light or an animation to On Saved. A phone is recognised again after a load by the floor seed and its
    /// position, so do not move phones around at runtime.
    /// </summary>
    public class SavePhone : MonoBehaviour, IInteractTarget
    {
        [Header("HUD text")]
        [SerializeField] private string prompt = "Call in";
        [SerializeField] private string usedPrompt = "Line is dead";
        [SerializeField] private string usedMessage = "It only rings once.";

        [Header("Hooks")]
        [Tooltip("Sound, a light, an animation: right when the save is done.")]
        [SerializeField] private UnityEvent onSaved;

        private string _key;

        public string Prompt => IsUsed ? usedPrompt : prompt;

        private bool IsUsed => SaveSystem.UsedPhones.Contains(Key);

        // Floor seed and place. A new floor has a new seed, so every phone works again there.
        private string Key
        {
            get
            {
                if (_key != null) return _key;

                var generator = FindAnyObjectByType<LevelGenerator>();
                var seed = generator && generator.Current != null ? generator.Current.Seed : 0;
                var p = transform.position;
                _key = $"{gameObject.scene.name}:{seed}:{Mathf.RoundToInt(p.x)}:{Mathf.RoundToInt(p.y)}:{Mathf.RoundToInt(p.z)}";
                return _key;
            }
        }

        public void Interact(Transform interactor)
        {
            if (IsUsed)
            {
                Notice(usedMessage);
                return;
            }

            var save = SaveGame.Instance;
            if (!save)
            {
                Notice("Nothing to save to.");
                return;
            }

            var saved = save.QuickSave(Key, out var message);
            Notice(message);
            if (saved) onSaved?.Invoke();
        }

        private static void Notice(string message)
        {
            if (!string.IsNullOrEmpty(message) && GameServices.TryGet(out EventBus bus)) bus.Publish(new NoticeEvent(message));
        }
    }
}
