using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// What the player keeps for good, as opposed to what belongs to one run: how many floors were completed and how
    /// many times they died. Once floor 1 is done, the flashlight is theirs permanently and a death no longer takes it.
    /// Call CompleteFloor() from whatever ends a floor (the elevator ride up, after the boss).
    /// </summary>
    public static class RunProgress
    {
        public static int FloorsCompleted { get; private set; }
        public static int Deaths { get; private set; }

        public static bool FlashlightPermanent => FloorsCompleted >= 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            FloorsCompleted = 0;
            Deaths = 0;
        }

        /// <summary>For loading a save.</summary>
        public static void Restore(int floorsCompleted, int deaths)
        {
            FloorsCompleted = Mathf.Max(0, floorsCompleted);
            Deaths = Mathf.Max(0, deaths);
        }

        public static void CompleteFloor() => FloorsCompleted++;

        public static void RegisterDeath() => Deaths++;
    }
}