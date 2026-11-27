using UnityEngine;

namespace Generation
{
    /// <summary>
    /// Marks where a quest item lies in a room (the flashlight). Put it on an empty child of every room prefab that can be
    /// the dead-end room, at floor level on a shelf, a table or the floor. The generator puts the pickup here.
    /// If a room has none, the pickup is placed in the middle of the room and a warning says so.
    /// </summary>
    public class QuestItemSpot : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.25f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.8f);
        }
    }
}