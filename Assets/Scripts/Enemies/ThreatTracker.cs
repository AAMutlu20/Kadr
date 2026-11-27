using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// How many threats are left on the floor: every enemy that is alive plus the ones that are still going to appear.
    /// The cage terminal reads it. The boss is not counted (it is not an EnemyController).
    ///
    /// Spawners and ambush triggers that create enemies later must call AddPending when they are armed and
    /// RemovePending when they have spawned them (or been cancelled). Otherwise the terminal says "no threats" while an
    /// ambush is still waiting in a room.
    /// </summary>
    public static class ThreatTracker
    {
        public static int Pending { get; private set; }

        /// <summary>Enemies alive right now.</summary>
        public static int Alive
        {
            get
            {
                var alive = 0;
                foreach (var enemy in EnemyController.All)
                {
                    if (enemy && !enemy.IsDead) alive++;
                }

                return alive;
            }
        }

        public static int Count => Alive + Pending;

        public static void AddPending(int count = 1) => Pending += Mathf.Max(0, count);

        public static void RemovePending(int count = 1) => Pending = Mathf.Max(0, Pending - Mathf.Max(0, count));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Pending = 0;
    }
}