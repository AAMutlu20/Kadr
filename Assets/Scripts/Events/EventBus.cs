using System;
using System.Collections.Generic;
using UnityEngine;

namespace Events
{
    public class EventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();

        public bool logEvents = true;

        public void Subscribe<T>(Action<T> handler) where T : IGameEvent
        {
            var key = typeof(T);

            if (!_handlers.TryGetValue(key, out var list))
            {
                list = new List<Delegate>();
                _handlers[key] = list;
            }

            // Ignore duplicate subs
            if (list.Contains(handler))
                return;

            list.Add(handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : IGameEvent
        {
            var key = typeof(T);

            // If nothing to unsub -> return
            if (!_handlers.TryGetValue(key, out var list))
                return;

            list.Remove(handler);

            // Remove empty lists to keep the dictionary clean.
            if (list.Count == 0)
                _handlers.Remove(key);
        }

        public void Publish<T>(T gameEvent) where T : IGameEvent
        {
            var key = typeof(T);

            // Create a snapshot so subs can change during event dispatch
            var snapshot = _handlers.TryGetValue(key, out var list)
                ? new List<Delegate>(list)
                : new List<Delegate>();

            var count = snapshot.Count;

            if (logEvents)
            {
                Debug.Log($"[EventBus] {key.Name} -> {count} listeners");
            }

            foreach (var handler in snapshot)
            {
                try
                {
                    ((Action<T>)handler).Invoke(gameEvent);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}