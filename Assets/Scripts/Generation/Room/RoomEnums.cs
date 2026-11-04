using System;

namespace Generation
{
    /// <summary>
    /// What kind of encounter a room hosts. Used by the tagging pass and the room resolver.
    /// </summary>
    public enum EncounterType
    {
        Safe,
        Combat,
        Ambush,
        Patrol,
        Elite,
        Boss,
        Transit
    }

    /// <summary>
    /// Rough size class of a room, used when resolving a graph node to a prefab.
    /// </summary>
    public enum SizeClass
    {
        Small,
        Medium,
        Large,
        XL
    }

    /// <summary>
    /// Which levels a room may appear on. Combining values allows several.
    /// </summary>
    [Flags]
    public enum DepthMask
    {
        None = 0,
        Depth1 = 1,
        Depth2 = 2,
        Depth3 = 4,
        All = Depth1 | Depth2 | Depth3
    }
}