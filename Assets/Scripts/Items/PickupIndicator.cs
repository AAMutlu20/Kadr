using Core;
using Events;
using Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Items
{
    /// <summary>
    /// On the Player. Every frame a short ray goes from the middle of the screen; if it hits an ItemPickup the
    /// prompt is published, and E collects it. Nothing here knows about UI: it only publishes events.
    ///
    /// Off while the game is paused (inventory open) and while the player is dead.
    /// </summary>
    public class PickupInteractor : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private Inventory inventory;
        [SerializeField] private float range = 2.5f;
        [Tooltip("What the ray can hit. Leave on Everything so walls block it; exclude the Player layer if you have one.")]
        [SerializeField] private LayerMask mask = ~0;

        private Health _health;
        private string _shownPrompt; // null = nothing shown

        private void Awake()
        {
            if (!inventory) inventory = GetComponentInParent<Inventory>();
            if (!cam) cam = Camera.main;
            _health = GetComponentInParent<Health>();
        }

        private void Update()
        {
            if (!cam || !inventory) return;

            var blocked = Time.timeScale == 0f || inventory.IsOpen || (_health && _health.IsDead);

            ItemPickup target = null;
            if (!blocked && Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, range, mask,
                    QueryTriggerInteraction.Collide))
            {
                target = hit.collider.GetComponentInParent<ItemPickup>();
            }

            // Only publish when something changed, not every frame.
            var prompt = target ? target.Prompt : null;
            if (prompt != _shownPrompt)
            {
                _shownPrompt = prompt;
                Publish(new InteractPromptEvent(prompt != null, prompt ?? ""));
            }

            var kb = Keyboard.current;
            if (target && kb != null && kb.eKey.wasPressedThisFrame && !target.TryCollect(inventory))
            {
                Publish(new NoticeEvent("Inventory full"));
            }
        }

        private void OnDisable()
        {
            if (_shownPrompt == null) return;

            _shownPrompt = null;
            Publish(new InteractPromptEvent(false, ""));
        }

        private static void Publish<T>(T evt) where T : struct, IGameEvent
        {
            if (GameServices.TryGet(out EventBus bus)) bus.Publish(evt);
        }
    }
}