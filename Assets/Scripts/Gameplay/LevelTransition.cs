using System.Collections;
using Core;
using Events;
using Menus;
using UnityEngine;
using UnityEngine.Events;

namespace Gameplay
{
    /// <summary>
    /// The way down. Look at it, press the Interact key (the same one as for a door), and the loading screen takes over
    /// and brings the player into the next scene. Put it on the elevator's control panel or any object with a collider
    /// that the player can look at. The ray finds it through its parents, so it can sit on the root with the collider on
    /// a child.
    ///
    ///   Target Scene   the scene to load (in the Build Settings), e.g. LevelOne.
    ///   Destination    the big name on the loading screen, e.g. FLOOR 1.
    ///   Doors          optional. If set and open, they close first and the descent starts when they have stopped.
    ///
    /// The scene it loads needs a PlayerSpawn: the Player persists through scene loads, and is moved there when the
    /// loading screen is done. From other scripts: Lock() / Unlock() (the generator, a key card...).
    /// </summary>
    public class LevelTransition : MonoBehaviour, IInteractTarget
    {
        [Header("Where to")]
        [SerializeField] private string targetScene = "LevelOne";
        [SerializeField] private string destination = "FLOOR 1";

        [Header("HUD text")]
        [SerializeField] private string prompt = "Descend";
        [SerializeField] private string lockedPrompt = "Locked";
        [SerializeField] private string lockedMessage = "The elevator is not working.";

        [Header("Optional")]
        [SerializeField] private bool startLocked;
        [Tooltip("The elevator doors. If they are open, they close before the descent.")]
        [SerializeField] private ElevatorDoors doors;
        [Tooltip("For sound and effects, right when the descent starts.")]
        [SerializeField] private UnityEvent onDescend;

        private bool _going;

        public bool IsLocked { get; private set; }

        public string Prompt => IsLocked ? lockedPrompt : prompt;

        public void Lock() => IsLocked = true;

        public void Unlock() => IsLocked = false;

        private void Awake() => IsLocked = startLocked;

        public void Interact(Transform interactor)
        {
            if (_going || SceneLoader.IsBusy) return;

            if (IsLocked)
            {
                if (!string.IsNullOrEmpty(lockedMessage) && GameServices.TryGet(out EventBus bus))
                {
                    bus.Publish(new NoticeEvent(lockedMessage));
                }

                return;
            }

            StartCoroutine(Descend());
        }

        private IEnumerator Descend()
        {
            _going = true;
            onDescend?.Invoke();

            if (doors && (doors.IsOpen || doors.IsMoving))
            {
                doors.Close();
                yield return null;
                while (doors.IsMoving) yield return null;
            }

            // Runs when the new scene is ready, under the black: the player goes to that scene's PlayerSpawn.
            SceneLoader.Load(targetScene, destination, LoadStyle.Full, () => PlayerSpawn.TeleportPlayer());

            // If the load could not start (scene missing from the Build Settings) the elevator works again.
            yield return new WaitForSecondsRealtime(1f);
            _going = false;
        }
    }
}