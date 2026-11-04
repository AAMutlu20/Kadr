using System.Collections.Generic;
using UnityEngine;

namespace Generation
{
    /// <summary>
    /// Pure math for attaching one room to another, so the generator
    /// can test a candidate placement BEFORE instantiating anything.
    ///
    /// Usage:
    ///   RoomSnap.ComputePlacement(parentSocket, candidatePrefab, candidateSocketIndex, out pos, out rot);
    ///   Bounds box = RoomSnap.GetPlacedBounds(candidatePrefab, pos, rot);
    ///   if (!RoomSnap.OverlapsAny(box, placedBoxes)) { Instantiate(candidatePrefab, pos, rot); }
    ///
    /// For rooms that are not instantiated yet, use GetSocketWorldPose to get a placed room's socket pose
    /// and the ComputePlacement overload that takes a position and a forward direction.
    /// </summary>
    public static class RoomSnap
    {
        /// <summary>
        /// Shrinks each box by this much per side before testing, so rooms whose walls merely touch
        /// at a doorway do not count as overlapping.
        /// </summary>
        private const float TouchTolerance = 0.05f;

        /// <summary>
        /// Convenience overload for a parent socket that exists in the scene.
        /// </summary>
        public static void ComputePlacement(
            Socket parentSocket,
            RoomInstance candidatePrefab,
            int candidateSocketIndex,
            out Vector3 position,
            out Quaternion rotation)
        {
            ComputePlacement(
                parentSocket.WorldPosition,
                parentSocket.WorldForward,
                candidatePrefab,
                candidateSocketIndex,
                out position,
                out rotation);
        }

        /// <summary>
        /// Works out where the candidate room's root must go so that its socket meets the parent socket.
        /// The two socket points coincide and the two sockets face each other.
        /// </summary>
        public static void ComputePlacement(
            Vector3 parentSocketPosition,
            Vector3 parentSocketForward,
            RoomInstance candidatePrefab,
            int candidateSocketIndex,
            out Vector3 position,
            out Quaternion rotation)
        {
            var candidateSocket = candidatePrefab.Sockets[candidateSocketIndex];

            // The candidate socket's pose relative to its own room root (prefab space).
            var prefabRoot = candidatePrefab.transform;
            var localPos = prefabRoot.InverseTransformPoint(candidateSocket.transform.position);
            var localRot = Quaternion.Inverse(prefabRoot.rotation) * candidateSocket.transform.rotation;

            // The candidate socket must face the opposite way to the parent socket (flat, Y rotation only).
            var flatForward = parentSocketForward;
            flatForward.y = 0f;
            var desiredSocketRot = Quaternion.LookRotation(-flatForward.normalized, Vector3.up);

            // Root rotation such that rootRot * localRot == desiredSocketRot.
            rotation = desiredSocketRot * Quaternion.Inverse(localRot);
            rotation = SnapYaw(rotation);

            // Root position such that rootPos + rootRot * localPos == parent socket position.
            position = parentSocketPosition - rotation * localPos;
            position = RoundVector(position, 0.01f);
        }

        /// <summary>
        /// World position and facing of a prefab's socket if the room were placed at position/rotation.
        /// </summary>
        public static void GetSocketWorldPose(
            RoomInstance prefab,
            int socketIndex,
            Vector3 position,
            Quaternion rotation,
            out Vector3 socketPosition,
            out Vector3 socketForward)
        {
            var socket = prefab.Sockets[socketIndex];
            var prefabRoot = prefab.transform;

            var localPos = prefabRoot.InverseTransformPoint(socket.transform.position);
            var localForward = prefabRoot.InverseTransformDirection(socket.transform.forward);

            socketPosition = position + rotation * localPos;
            socketForward = rotation * localForward;
        }

        /// <summary>
        /// The candidate room's bounds box as an axis-aligned world box, if it were placed at pos/rot.
        /// </summary>
        public static Bounds GetPlacedBounds(RoomInstance prefab, Vector3 position, Quaternion rotation)
        {
            var box = prefab.Bounds;

            // Box local space -> prefab root space -> placed world space.
            var boxToPrefab = prefab.transform.worldToLocalMatrix * box.transform.localToWorldMatrix;
            var prefabToWorld = Matrix4x4.TRS(position, rotation, Vector3.one);
            var m = prefabToWorld * boxToPrefab;

            var half = box.size * 0.5f;
            var result =
                new Bounds(m.MultiplyPoint3x4(box.center + new Vector3(-half.x, -half.y, -half.z)), Vector3.zero);

            for (var i = 1; i < 8; i++)
            {
                var corner = box.center + new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);
                result.Encapsulate(m.MultiplyPoint3x4(corner));
            }

            return result;
        }

        /// <summary>
        /// True if the box overlaps any box in the list, ignoring faces that only touch.
        /// </summary>
        public static bool OverlapsAny(Bounds candidate, IReadOnlyList<Bounds> placed)
        {
            var shrunk = candidate;
            shrunk.Expand(-TouchTolerance * 2f);

            foreach (var placedBox in placed)
            {
                // Copy first: Bounds is a struct, and a foreach variable cannot be changed in place.
                var other = placedBox;
                other.Expand(-TouchTolerance * 2f);

                if (shrunk.Intersects(other))
                {
                    return true;
                }
            }

            return false;
        }

        // Rooms only ever rotate in 90 degree steps, so clean up float drift after the maths.
        private static Quaternion SnapYaw(Quaternion q)
        {
            var yaw = q.eulerAngles.y;
            var snapped = Mathf.Round(yaw / 90f) * 90f;
            return Quaternion.Euler(0f, snapped, 0f);
        }

        private static Vector3 RoundVector(Vector3 v, float step)
        {
            return new Vector3(
                Mathf.Round(v.x / step) * step,
                Mathf.Round(v.y / step) * step,
                Mathf.Round(v.z / step) * step);
        }
    }
}