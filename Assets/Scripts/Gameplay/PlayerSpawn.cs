using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// Marks where the player stands when a scene starts: the Lobby (after a death) and the first room of a level (after
    /// the elevator). Put one empty GameObject with this in each scene, where the player should appear. Its forward
    /// direction is the way the player will face. Only one per scene.
    ///
    /// PlayerSpawn.TeleportPlayer() moves the player (who lives through scene loads) to the spawn of the scene that is
    /// loaded now. DeathFlow and LevelTransition call it.
    /// </summary>
    public class PlayerSpawn : MonoBehaviour
    {
        /// <summary>Moves the player to the spawn in the loaded scene. False (and a warning) if there is none.</summary>
        public static bool TeleportPlayer(Transform player = null)
        {
            if (!player)
            {
                var controller = FindAnyObjectByType<PlayerController>();
                if (controller) player = controller.transform;
            }

            if (!player)
            {
                Debug.LogWarning("[PlayerSpawn] No player found to move.");
                return false;
            }

            var spawn = FindAnyObjectByType<PlayerSpawn>();
            if (!spawn)
            {
                Debug.LogWarning("[PlayerSpawn] No PlayerSpawn in the loaded scene. The player stays where it is.");
                return false;
            }

            MoveTo(player, spawn.transform.position, spawn.transform.rotation);
            return true;
        }

        /// <summary>Puts the player somewhere without the CharacterController walking back to the old spot.</summary>
        public static void MoveTo(Transform player, Vector3 position, Quaternion rotation)
        {
            var body = player.GetComponent<CharacterController>();
            var wasEnabled = body && body.enabled;
            if (body) body.enabled = false;

            player.SetPositionAndRotation(position, rotation);

            if (body) body.enabled = wasEnabled;
            Physics.SyncTransforms();
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 1f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.35f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.8f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.9f, transform.forward * 0.8f);
        }
    }
}