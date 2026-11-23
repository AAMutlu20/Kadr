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

        [Tooltip("Logs what the ray hits first whenever that changes. Turn on if the prompt never shows up.")]
        [SerializeField] private bool debugRay;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private Health _health;
        private string _shownPrompt; // null = nothing shown
        private Collider _lastHit;

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

            IPickupTarget target = null;
            if (!blocked)
            {
                var first = FirstHit();
                if (debugRay && first != _lastHit)
                {
                    _lastHit = first;
                    Debug.Log($"[PickupInteractor] ray hits: {(first ? first.name : "nothing")}", this);
                }

                if (first) target = first.GetComponentInParent<IPickupTarget>();
            }

            // Only publish when something changed, not every frame.
            var prompt = target != null ? target.Prompt : null;
            if (prompt != _shownPrompt)
            {
                _shownPrompt = prompt;
                Publish(new InteractPromptEvent(prompt != null, prompt ?? ""));
            }

            if (target != null && KadrControls.Pressed(GameAction.Interact) && !target.TryCollect(inventory))
            {
                Publish(new NoticeEvent("Inventory full"));
            }
        }

        /// <summary>The nearest collider in reach that is not part of the player (its own capsule or gun must not block the ray).</summary>
        private Collider FirstHit()
        {
            var count = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, Hits, range, mask,
                QueryTriggerInteraction.Collide);

            Collider best = null;
            var bestDistance = float.MaxValue;
            var self = transform.root;

            for (var i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.distance >= bestDistance || hit.collider.transform.IsChildOf(self)) continue;

                bestDistance = hit.distance;
                best = hit.collider;
            }

            return best;
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
