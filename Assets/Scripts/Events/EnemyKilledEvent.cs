using UnityEngine;

namespace Events
{
    /// <summary>
    /// Published once when an enemy dies. Subscribers: XP (grants experience), quests (kill counters),
    /// audio. EnemyId comes from the enemy's EnemyStats, so "kill 5 crawlers" can filter on it.
    /// </summary>
    public readonly struct EnemyKilledEvent : IGameEvent
    {
        public readonly string EnemyId;
        public readonly Vector3 Position;

        public EnemyKilledEvent(string enemyId, Vector3 position)
        {
            EnemyId = enemyId;
            Position = position;
        }
    }
}