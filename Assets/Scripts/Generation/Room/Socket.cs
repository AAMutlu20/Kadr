using UnityEngine;

namespace Generation
{
    /// <summary>
    /// A connection point on a room. Used on an empty GameObject placed at the centre of a
    /// doorway, on the outer face of the wall, with its Z direction pointing OUT of the room.
    /// The generator only reads the position and forward direction.
    /// </summary>
    public class Socket : MonoBehaviour
    {
        /// <summary>Set by the generator at runtime once another room is connected.</summary>
        public bool IsConnected { get; set; }

        public Vector3 WorldPosition => transform.position;

        /// <summary>Direction pointing out of the room.</summary>
        public Vector3 WorldForward => transform.forward;

        private void OnDrawGizmos()
        {
            // Draw in the socket's local space so the outline rotates with it.
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = IsConnected ? Color.red : Color.green;

            // Doorway outline: DoorWidth x DoorHeight
            var size = new Vector3(RoomConstants.DoorWidth, RoomConstants.DoorHeight, 0.1f);
            var center = new Vector3(0f, RoomConstants.DoorHeight * 0.5f, 0f);
            Gizmos.DrawWireCube(center, size);

            // Facing direction and exact socket point.
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 3f);
            Gizmos.DrawSphere(Vector3.zero, 0.2f);

            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}