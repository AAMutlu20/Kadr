using UnityEngine;

namespace Events
{
    /// <summary>
    /// Published by Health whenever an entity's hit points change, including once at start.
    /// Listeners tell entities apart by comparing Entity with the object they care about, for example the
    /// health GUI only reacts to the player. Delta is negative for damage and positive for healing, which
    /// lets a damage flash or a floating number react without keeping the previous value itself.
    /// </summary>
    public readonly struct HealthChangedEvent : IGameEvent
    {
        public readonly GameObject Entity;
        public readonly float Current;
        public readonly float Max;
        public readonly float Delta;

        public HealthChangedEvent(GameObject entity, float current, float max, float delta)
        {
            Entity = entity;
            Current = current;
            Max = max;
            Delta = delta;
        }
    }
}