using System;
using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// What the spawner may place on a floor and how much of it. One asset per floor (or per depth): assign it to the
    /// EnemySpawner. Tune the floor here without touching code.
    ///
    ///   Enemies          the prefabs it can pick from, each with a weight (2 = picked twice as often as 1)
    ///   Combat Min/Max   enemies in a Combat room before the size and floor limits are applied
    ///   Area Per Enemy   a room never gets more than (floor area / this) enemies, so small rooms are not crowded
    /// </summary>
    [CreateAssetMenu(fileName = "EnemySpawnTable", menuName = "Kadr/Enemy Spawn Table")]
    public class EnemySpawnTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("An enemy prefab (needs an EnemyController).")]
            public EnemyController prefab;
            [Min(0f)] public float weight = 1f;
        }

        [SerializeField] private Entry[] enemies = Array.Empty<Entry>();

        [Header("Combat rooms")]
        [SerializeField, Min(0)] private int combatMin = 2;
        [SerializeField, Min(0)] private int combatMax = 4;
        [Tooltip("Square metres of floor one enemy needs. A room of 200 m2 with 80 here holds at most 2.")]
        [SerializeField, Min(1f)] private float areaPerEnemy = 80f;

        public Entry[] Enemies => enemies;
        public int CombatMin => combatMin;
        public int CombatMax => Mathf.Max(combatMin, combatMax);
        public float AreaPerEnemy => areaPerEnemy;
    }
}