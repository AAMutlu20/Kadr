using Generation;

namespace Events
{
    /// <summary>
    /// Published when the player walks into a room.
    /// Subscribers:
    /// enemy spawners
    /// the ambient audio
    /// coordinator
    /// the minimap/GUI
    /// 
    /// NodeId is the room's node in the level graph.
    /// </summary>
    public readonly struct RoomEnteredEvent : IGameEvent
    {
        public readonly int NodeId;
        public readonly EncounterType Encounter;

        public RoomEnteredEvent(int nodeId, EncounterType encounter)
        {
            NodeId = nodeId;
            Encounter = encounter;
        }
    }
}