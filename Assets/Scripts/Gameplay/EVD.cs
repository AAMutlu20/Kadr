using System;
using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// The two heavy sliding panels of the freight elevator. Call Open() / Close() from the transition (or from a UnityEvent).
    /// The panels slide sideways along the parent's X axis, and ease in and out so they feel slow and heavy.
    ///
    ///   Slide Distance   how far each panel travels. Default 2.6 puts each panel behind the side columns of the frame.
    ///   Open Seconds     time for a full open or close.
    ///   Start Open       the doors are open when the scene starts.
    ///
    /// Finished fires when a move ends (IsOpen tells which way it went).
    /// The prefab from Kadr > Build Elevator already has this set up.
    /// </summary>
    public class ElevatorDoors : MonoBehaviour
    {
        [SerializeField] private Transform left;
        [SerializeField] private Transform right;
        [SerializeField, Min(0f)] private float slideDistance = 2.6f;
        [SerializeField, Min(0.1f)] private float openSeconds = 4.5f;
        [SerializeField] private bool startOpen;

        private Vector3 _leftClosed;
        private Vector3 _rightClosed;
        private float _amount;
        private float _target;

        public bool IsOpen { get; private set; }
        public bool IsMoving => !Mathf.Approximately(_amount, _target);

        /// <summary>A move has ended. Check IsOpen.</summary>
        public event Action Finished;

        private void Awake()
        {
            if (left) _leftClosed = left.localPosition;
            if (right) _rightClosed = right.localPosition;

            if (!left || !right)
            {
                Debug.LogWarning("[ElevatorDoors] Left or right panel is not assigned.", this);
            }

            _amount = _target = startOpen ? 1f : 0f;
            IsOpen = startOpen;
            Apply();
        }

        public void Open() => _target = 1f;

        public void Close() => _target = 0f;

        private void Update()
        {
            if (!IsMoving) return;

            _amount = Mathf.MoveTowards(_amount, _target, Time.deltaTime / openSeconds);
            Apply();

            if (IsMoving) return;

            IsOpen = _target > 0.5f;
            Finished?.Invoke();
        }

        private void Apply()
        {
            var eased = Mathf.SmoothStep(0f, 1f, _amount) * slideDistance;

            if (left) left.localPosition = _leftClosed + Vector3.left * eased;
            if (right) right.localPosition = _rightClosed + Vector3.right * eased;
        }
    }
}