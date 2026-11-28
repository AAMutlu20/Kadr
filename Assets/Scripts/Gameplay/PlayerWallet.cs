using System;
using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// The player's roubles. Static, like PlayerXp: it survives deaths and scene loads for as long as the game runs.
    /// Saving it to disk comes with the save system. The Comisar's shop will spend from here.
    /// </summary>
    public static class PlayerWallet
    {
        public static int Roubles { get; private set; }

        /// <summary>Raised with the new balance after every change.</summary>
        public static event Action<int> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Roubles = 0;
            Changed = null;
        }

        /// <summary>For loading a save.</summary>
        public static void Set(int roubles)
        {
            Roubles = Mathf.Max(0, roubles);
            Changed?.Invoke(Roubles);
        }

        public static void Add(int amount)
        {
            if (amount <= 0) return;

            Roubles += amount;
            Changed?.Invoke(Roubles);
        }

        public static bool TrySpend(int amount)
        {
            if (amount < 0 || amount > Roubles) return false;

            Roubles -= amount;
            Changed?.Invoke(Roubles);
            return true;
        }
    }
}