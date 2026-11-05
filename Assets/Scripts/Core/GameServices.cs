using System;
using System.Collections.Generic;

namespace Core
{
    /// <summary>
    /// Service Locator for Kadr's global systems: the EventBus and TODO IAudioService (FMOD).
    /// Gameplay code asks for an interface here instead of holding a reference to a concrete class,
    /// which is what keeps FMOD behind IAudioService and lets tests swap in a fake.
    ///
    /// Rules this class relies on:
    /// - Everything is registered once, by Bootstrap, before the first scene loads. Nothing registers later.
    /// - Main thread only. There are no locks on purpose, as Unity API calls are main-thread anyway.
    /// - The fields are static, so they survive between play sessions when Enter Play Mode runs without a
    ///   domain reload. That is why Bootstrap calls Clear() first.
    /// - Look a service up in OnEnable or Awake and keep the reference. Do not call Get every frame.
    /// </summary>
    public static class GameServices
    {
        private static readonly Dictionary<Type, object> Services = new();

        /// <summary>
        /// Registers a service under T, which should be the interface (IAudioService), not the concrete class.
        /// Registering the same T twice throws: Bootstrap clears first, so a duplicate means two systems
        /// are both trying to own the service, and silently replacing one would hide that.
        /// </summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service), $"Cannot register a null {typeof(T).Name}.");
            }

            if (!Services.TryAdd(typeof(T), service))
            {
                throw new InvalidOperationException(
                    $"{typeof(T).Name} is already registered. Is something other than Bootstrap registering services?");
            }
        }

        /// <summary>
        /// For core game services. Throws exception if it is missing, so the failure points at the
        /// real cause and not at a null reference further on.
        /// </summary>
        public static T Get<T>() where T : class
        {
            if (Services.TryGetValue(typeof(T), out var service))
            {
                return (T)service;
            }

            throw new InvalidOperationException(
                $"{typeof(T).Name} is not registered. Services only exist in Play mode, after Bootstrap has run.");
        }

        /// <summary>
        /// For lookups that may find nothing: unsubscribing in OnDisable while the game is
        /// shutting down, or editor-time tools such as the generator's context menu.
        /// </summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out var found))
            {
                service = (T)found;
                return true;
            }

            service = null;
            return false;
        }

        /// <summary>
        /// Drops every service. Only Bootstrap (at startup) and tests should call this.
        /// </summary>
        public static void Clear()
        {
            Services.Clear();
        }
    }
}