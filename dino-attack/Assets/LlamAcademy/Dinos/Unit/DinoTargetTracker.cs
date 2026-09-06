using System;
using System.Collections.Generic;
using UnityEngine;

namespace LlamAcademy.Dinos.Unit
{
    public sealed class DinoTargetTracker : IDisposable
    {
        private sealed class Entry
        {
            public Entry(IDamageable target, GameObject gameObject)
            {
                Target = target;
                GameObject = gameObject;
            }

            public IDamageable Target { get; }
            public GameObject GameObject { get; }
        }

        private readonly Dictionary<int, Entry> targets = new();

        public event Action Changed;

        public bool HasLiveTarget
        {
            get
            {
                PurgeInvalid();
                return targets.Count > 0;
            }
        }

        public bool Add(IDamageable target)
        {
            if (!DamageableState.IsAlive(target))
            {
                return false;
            }

            GameObject gameObject = target.Transform.gameObject;
            int instanceId = gameObject.GetInstanceID();
            if (targets.ContainsKey(instanceId))
            {
                return false;
            }

            targets.Add(instanceId, new Entry(target, gameObject));
            target.OnDeath += HandleDeath;
            Changed?.Invoke();
            return true;
        }

        public bool Remove(IDamageable target)
        {
            if (target == null)
            {
                return false;
            }

            int instanceId = TryGetInstanceId(target);
            if (instanceId != 0)
            {
                return RemoveById(instanceId, true);
            }

            foreach (KeyValuePair<int, Entry> pair in targets)
            {
                if (ReferenceEquals(pair.Value.Target, target))
                {
                    return RemoveById(pair.Key, true);
                }
            }

            return false;
        }

        public int CopyLiveTargets(List<GameObject> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            PurgeInvalid();
            foreach (Entry entry in targets.Values)
            {
                destination.Add(entry.GameObject);
            }

            return destination.Count;
        }

        public void Clear()
        {
            if (targets.Count == 0)
            {
                return;
            }

            foreach (Entry entry in targets.Values)
            {
                TryUnsubscribe(entry.Target);
            }

            targets.Clear();
            Changed?.Invoke();
        }

        public void Dispose() => Clear();

        private void HandleDeath(IDamageable target) => Remove(target);

        private void PurgeInvalid()
        {
            List<int> invalidIds = null;
            foreach (KeyValuePair<int, Entry> pair in targets)
            {
                if (DamageableState.IsAlive(pair.Value.Target))
                {
                    continue;
                }

                invalidIds ??= new List<int>();
                invalidIds.Add(pair.Key);
            }

            if (invalidIds == null)
            {
                return;
            }

            foreach (int instanceId in invalidIds)
            {
                RemoveById(instanceId, false);
            }

            Changed?.Invoke();
        }

        private bool RemoveById(int instanceId, bool notify)
        {
            if (!targets.TryGetValue(instanceId, out Entry entry))
            {
                return false;
            }

            TryUnsubscribe(entry.Target);
            targets.Remove(instanceId);
            if (notify)
            {
                Changed?.Invoke();
            }

            return true;
        }

        private static int TryGetInstanceId(IDamageable target)
        {
            if (target is UnityEngine.Object unityObject && unityObject == null)
            {
                return 0;
            }

            Transform targetTransform = target.Transform;
            return targetTransform == null ? 0 : targetTransform.gameObject.GetInstanceID();
        }

        private void TryUnsubscribe(IDamageable target)
        {
            if (target == null || target is UnityEngine.Object unityObject && unityObject == null)
            {
                return;
            }

            target.OnDeath -= HandleDeath;
        }
    }
}
