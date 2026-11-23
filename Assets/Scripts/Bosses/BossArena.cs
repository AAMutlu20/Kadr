using Core;
using Events;
using Gameplay;
using UnityEngine;

namespace Bosses
{
    /// <summary>
    /// The boss room's trigger. When the player walks in, the boss wakes up, the "close" objects (doors, barriers) are
    /// switched on and the "open" objects are switched off. The boss cannot be killed, so the fight ends when your own
    /// game calls End: put a BossArenaExit on the elevator trigger, or call End from a generator script.
    ///
    /// Needs a Collider with Is Trigger ticked, covering the doorway or the whole room.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BossArena : MonoBehaviour
    {
        [SerializeField] private BossController boss;
        [Tooltip("Switched ON when the fight starts and OFF when it ends: doors, barriers, a gate behind the player.")]
        [SerializeField] private GameObject[] closeOnStart;
        [Tooltip("Switched OFF when the fight starts and ON when it ends: an open door, a light.")]
        [SerializeField] private GameObject[] openOnStart;
        [Tooltip("The boss stays asleep again when the fight ends. Off = he is left hunting.")]
        [SerializeField] private bool bossSleepsAtEnd = true;

        private bool _started;
        private bool _ended;

        public bool Started => _started;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            SetObjects(closeOnStart, false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_started || _ended) return;
            if (!other.GetComponentInParent<PlayerController>()) return;

            Begin();
        }

        public void Begin()
        {
            if (_started || _ended) return;

            _started = true;
            SetObjects(closeOnStart, true);
            SetObjects(openOnStart, false);
            if (boss) boss.Wake();

            Publish(true);
        }

        /// <summary>The player got away (or finished the objective). Opens everything again.</summary>
        public void End()
        {
            if (!_started || _ended) return;

            _ended = true;
            SetObjects(closeOnStart, false);
            SetObjects(openOnStart, true);
            if (boss && bossSleepsAtEnd) boss.Sleep();

            Publish(false);
        }

        private static void SetObjects(GameObject[] objects, bool active)
        {
            if (objects == null) return;

            foreach (var go in objects)
            {
                if (go) go.SetActive(active);
            }
        }

        private static void Publish(bool started)
        {
            if (GameServices.TryGet(out EventBus bus)) bus.Publish(new BossEncounterEvent(started));
        }
    }

    /// <summary>Put on a trigger (the elevator): when the player enters it, the boss fight ends.</summary>
    [RequireComponent(typeof(Collider))]
    public class BossArenaExit : MonoBehaviour
    {
        [SerializeField] private BossArena arena;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (arena && arena.Started && other.GetComponentInParent<PlayerController>()) arena.End();
        }
    }
}