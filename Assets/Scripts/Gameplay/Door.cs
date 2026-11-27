using System.Collections.Generic;
using Core;
using Events;
using UnityEngine;
using UnityEngine.Events;

namespace Gameplay
{
    /// <summary>
    /// A hinged door. Put it on the HINGE object: the door's own pivot has to sit on the hinge, with the panel (mesh and
    /// collider) as a child of it. Do not tick Static on the panel, it moves.
    ///
    ///   Interact key   opens the door away from the player (it swings to the side he is looking at), or closes it.
    ///   Locked         it cannot be opened. The HUD prompt says "Locked" and pressing the key shows a message.
    ///
    /// From other scripts:
    ///   door.Lock() / door.Unlock()        Lock closes an open door first. Unlock leaves it closed.
    ///   door.Close(), door.Open()          Open does nothing while locked.
    ///   Door.UnlockGroup("floor2")         every door with that Group name (the generator, the power, a switch...)
    ///   BossArena has a Doors list: it closes and locks them when the fight starts and unlocks them when it ends.
    ///
    /// The prompt is read by PickupInteractor every frame, so it changes by itself when a door is locked or unlocked
    /// while the player is looking at it.
    /// </summary>
    public class Door : MonoBehaviour, IInteractTarget
    {
        public enum PanelSide { Right, Left }

        [Header("Swing")]
        [Tooltip("Which way the panel runs from the hinge, along the hinge object's own X axis (the red arrow). " +
                 "Select the door: the cyan line in the Scene view shows it.")]
        [SerializeField] private PanelSide panelExtends = PanelSide.Right;
        [SerializeField, Range(30f, 170f)] private float openAngle = 90f;
        [SerializeField, Min(0.1f)] private float openSeconds = 0.9f;

        [Header("Start")]
        [SerializeField] private bool startLocked;
        [SerializeField] private bool startOpen;

        [Header("Group")]
        [Tooltip("Optional name. Door.UnlockGroup(name) / LockGroup(name) act on every door with the same name, for example everything that the floor 2 generator powers.")]
        [SerializeField] private string group;

        [Header("HUD text")]
        [SerializeField] private string openPrompt = "Open";
        [SerializeField] private string closePrompt = "Close";
        [SerializeField] private string lockedPrompt = "Locked";
        [Tooltip("Shown as a notice when the player tries a locked door. Use it to hint at what to do: 'The power is off.'")]
        [SerializeField] private string lockedMessage = "It is locked.";
        [Tooltip("Shown as a notice when the door unlocks. Empty = nothing.")]
        [SerializeField] private string unlockedMessage;

        [Header("Hooks (for sound and effects)")]
        [SerializeField] private UnityEvent onOpening;
        [SerializeField] private UnityEvent onClosing;
        [SerializeField] private UnityEvent onLockedTry;
        [SerializeField] private UnityEvent onUnlocked;

        private static readonly List<Door> All = new();

        private Quaternion _closed;
        private float _t;       // 0 closed, 1 fully open
        private float _targetT;
        private float _sign = 1f;
        private bool _sealed;

        public bool IsLocked { get; private set; }
        public bool IsOpen => _targetT > 0.5f;
        public bool IsMoving => !Mathf.Approximately(_t, _targetT);
        public string Group => group;

        public string Prompt => IsLocked ? lockedPrompt : IsOpen ? closePrompt : openPrompt;

        private float DefaultSign => panelExtends == PanelSide.Right ? 1f : -1f;

        // ---------- the calls your own scripts use ----------

        /// <summary>Opens away from the given viewer (null = the default side). False if the door is locked.</summary>
        public bool Open(Transform from = null)
        {
            if (IsLocked) return false;
            if (_targetT >= 1f) return true;

            // Pick the side only when the door starts from closed. Reversing a half open door keeps its side.
            if (_t <= 0.001f) _sign = SignAwayFrom(from);

            _targetT = 1f;
            onOpening?.Invoke();
            return true;
        }

        public void Close()
        {
            if (_targetT <= 0f) return;

            _targetT = 0f;
            onClosing?.Invoke();
        }

        /// <summary>Locks the door so it cannot be opened. An open door closes first (closeFirst) or stays open until closed.</summary>
        public void Lock(bool closeFirst = true)
        {
            IsLocked = true;
            if (closeFirst) Close();
        }

        public void Unlock()
        {
            if (_sealed || !IsLocked) return;

            IsLocked = false;
            onUnlocked?.Invoke();
            if (!string.IsNullOrEmpty(unlockedMessage)) Notice(unlockedMessage);
        }

        /// <summary>For spawners (the level generator): sets the group and the starting lock state of a door that was just created.</summary>
        public void Configure(string groupName, bool locked)
        {
            group = groupName;
            IsLocked = locked;
        }

        /// <summary>Locks the door for good: Unlock and UnlockGroup do nothing to it. For doorways that lead nowhere.</summary>
        public void Seal(string message = null)
        {
            _sealed = true;
            IsLocked = true;
            group = null;
            if (!string.IsNullOrEmpty(message)) lockedMessage = message;
        }

        public static void UnlockGroup(string groupName) => SetGroup(groupName, false);
        public static void LockGroup(string groupName) => SetGroup(groupName, true);

        private static void SetGroup(string groupName, bool locked)
        {
            if (string.IsNullOrEmpty(groupName)) return;

            // Copy: an unlock can trigger hooks that change the list.
            foreach (var door in new List<Door>(All))
            {
                if (door.group != groupName) continue;

                if (locked) door.Lock();
                else door.Unlock();
            }
        }

        // ---------- the player ----------

        public void Interact(Transform interactor)
        {
            if (IsLocked)
            {
                onLockedTry?.Invoke();
                Notice(lockedMessage);
                return;
            }

            if (IsOpen) Close();
            else Open(interactor);
        }

        // ---------- the swing ----------

        private void Awake()
        {
            _closed = transform.localRotation;
            IsLocked = startLocked;

            if (startOpen)
            {
                _sign = DefaultSign;
                _t = _targetT = 1f;
                Apply();
            }
        }

        private void OnEnable() => All.Add(this);

        private void OnDisable() => All.Remove(this);

        private void Update()
        {
            if (!IsMoving) return;

            _t = Mathf.MoveTowards(_t, _targetT, Time.deltaTime / openSeconds);
            Apply();
        }

        private void Apply()
        {
            var angle = _sign * openAngle * Mathf.SmoothStep(0f, 1f, _t);
            transform.localRotation = _closed * Quaternion.AngleAxis(angle, Vector3.up);
        }

        /// <summary>
        /// +1 or -1 for the swing so that the free edge goes to the far side from the viewer. The panel runs along the
        /// hinge's X axis, and a positive turn about Y moves +X towards -Z.
        /// </summary>
        private float SignAwayFrom(Transform from)
        {
            if (!from) return DefaultSign;

            var closedWorld = transform.parent ? transform.parent.rotation * _closed : _closed;
            var normal = closedWorld * Vector3.forward;
            var side = Vector3.Dot(normal, from.position - transform.position) >= 0f ? 1f : -1f;

            return side * DefaultSign;
        }

        private static void Notice(string message)
        {
            if (!string.IsNullOrEmpty(message) && GameServices.TryGet(out EventBus bus)) bus.Publish(new NoticeEvent(message));
        }

        private void OnValidate()
        {
            openSeconds = Mathf.Max(0.1f, openSeconds);
        }

        // Cyan: where the panel runs when closed. Yellow: where its free edge ends up when opened from the front (+Z) side.
        private void OnDrawGizmosSelected()
        {
            var origin = transform.position;
            var closedRotation = Application.isPlaying ? (transform.parent ? transform.parent.rotation * _closed : _closed) : transform.rotation;
            var along = closedRotation * Vector3.right * (panelExtends == PanelSide.Right ? 1f : -1f);
            var sign = DefaultSign;
            var opened = closedRotation * Quaternion.AngleAxis(sign * openAngle, Vector3.up) * Vector3.right * (panelExtends == PanelSide.Right ? 1f : -1f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, origin + along);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, origin + opened);
        }
    }
}