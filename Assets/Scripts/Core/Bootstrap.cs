using Events;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// Creates and registers the game's services before any scene object wakes up.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialise()
        {
            // Start clean, in case static data survived from the last play session.
            GameServices.Clear();

            GameServices.Register(new EventBus());
        }
    }
}