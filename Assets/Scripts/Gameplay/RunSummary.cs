using System.Collections.Generic;
using UnityEngine;

namespace Gameplay
{
    /// <summary>One room on the debrief's route schematic. X and Y are 0..1 inside the schematic.</summary>
    public sealed class RouteNode
    {
        public int Id;
        public int Parent = -1;
        public bool OnCriticalPath;
        public bool IsBoss;
        public bool Visited;
        public bool DeathRoom;
        public float X;
        public float Y;
    }

    /// <summary>Everything the debrief screen shows. A plain snapshot, taken at the moment of death.</summary>
    public sealed class RunSummary
    {
        public int Floor = 1;
        public int DeathNumber = 1;
        public float Seconds;
        public string Cause = "UNKNOWN";

        public int Kills;
        public int PhotosTaken;
        public int PhotosKept;
        public int XpGained;
        public int RoomsVisited;
        public int RoomsTotal;

        /// <summary>Roubles the paid photos were worth in total, and the split after the penalty.</summary>
        public int PayoutTotal;
        public int Filed;
        public int Forfeited;
        public float PayoutRate = 0.5f;
        public int Balance;

        public int ConsumablesLost;
        public readonly List<string> KeyItemsLost = new();

        public readonly List<RouteNode> Route = new();

        /// <summary>The last frame before the black. Destroyed by the debrief when it closes.</summary>
        public Texture2D LastFrame;
        public int FrameNumber;

        public string TimeText
        {
            get
            {
                var total = Mathf.FloorToInt(Seconds);
                return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
            }
        }
    }
}