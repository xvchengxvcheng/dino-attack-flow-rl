using System.Collections.Generic;
using UnityEngine;

namespace LlamAcademy.Dinos.Unit
{
    [RequireComponent(typeof(Collider))]
    public class AttackRadius : MonoBehaviour
    {
        private sealed class Overlap
        {
            public Overlap(IDamageable target)
            {
                Target = target;
                Count = 1;
            }

            public IDamageable Target { get; }
            public int Count { get; set; }
        }

        public delegate void TargetEvent(IDamageable target);

        public TargetEvent OnTargetEnter;
        public TargetEvent OnTargetExit;

        private readonly Dictionary<int, Overlap> overlaps = new();

        public float Radius => GetComponent<SphereCollider>()?.radius ?? 0f;

        public bool ConfigureRadius(float radius)
        {
            if (radius <= 0f || !float.IsFinite(radius) || !TryGetComponent(out SphereCollider sphere))
            {
                return false;
            }

            sphere.radius = radius;
            return true;
        }

        public int ReplayCurrentTargets(TargetEvent receiver)
        {
            if (receiver == null)
            {
                return 0;
            }

            List<int> invalidIds = null;
            List<IDamageable> liveTargets = new(overlaps.Count);
            foreach (KeyValuePair<int, Overlap> pair in overlaps)
            {
                if (DamageableState.IsAlive(pair.Value.Target))
                {
                    liveTargets.Add(pair.Value.Target);
                    continue;
                }

                invalidIds ??= new List<int>();
                invalidIds.Add(pair.Key);
            }

            if (invalidIds != null)
            {
                foreach (int instanceId in invalidIds)
                {
                    RemoveById(instanceId, true);
                }
            }

            foreach (IDamageable target in liveTargets)
            {
                receiver(target);
            }

            return liveTargets.Count;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!TryGetDamageable(other, out IDamageable damageable)
                || !DamageableState.IsAlive(damageable))
            {
                return;
            }

            int instanceId = damageable.Transform.gameObject.GetInstanceID();
            if (overlaps.TryGetValue(instanceId, out Overlap overlap))
            {
                overlap.Count++;
                return;
            }

            overlaps.Add(instanceId, new Overlap(damageable));
            damageable.OnDeath += Damageable_OnDeath;
            OnTargetEnter?.Invoke(damageable);
        }

        private void Damageable_OnDeath(IDamageable damageable)
        {
            Remove(damageable, true);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!TryGetDamageable(other, out IDamageable damageable))
            {
                return;
            }

            int instanceId = TryGetInstanceId(damageable);
            if (instanceId == 0 || !overlaps.TryGetValue(instanceId, out Overlap overlap))
            {
                return;
            }

            overlap.Count--;
            if (overlap.Count <= 0)
            {
                RemoveById(instanceId, true);
            }
        }

        private void OnDisable()
        {
            foreach (Overlap overlap in overlaps.Values)
            {
                TryUnsubscribe(overlap.Target);
            }

            overlaps.Clear();
        }

        private bool Remove(IDamageable target, bool notify)
        {
            int instanceId = TryGetInstanceId(target);
            if (instanceId != 0)
            {
                return RemoveById(instanceId, notify);
            }

            foreach (KeyValuePair<int, Overlap> pair in overlaps)
            {
                if (ReferenceEquals(pair.Value.Target, target))
                {
                    return RemoveById(pair.Key, notify);
                }
            }

            return false;
        }

        private bool RemoveById(int instanceId, bool notify)
        {
            if (!overlaps.TryGetValue(instanceId, out Overlap overlap))
            {
                return false;
            }

            TryUnsubscribe(overlap.Target);
            overlaps.Remove(instanceId);
            if (notify)
            {
                OnTargetExit?.Invoke(overlap.Target);
            }

            return true;
        }

        private static bool TryGetDamageable(Collider other, out IDamageable damageable)
        {
            damageable = null;
            if (other == null)
            {
                return false;
            }

            if (other.TryGetComponent(out damageable))
            {
                return true;
            }

            foreach (MonoBehaviour behaviour in other.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (behaviour is IDamageable candidate)
                {
                    damageable = candidate;
                    return true;
                }
            }

            return false;
        }

        private static int TryGetInstanceId(IDamageable target)
        {
            if (target == null || target is Object unityObject && unityObject == null)
            {
                return 0;
            }

            Transform targetTransform = target.Transform;
            return targetTransform == null ? 0 : targetTransform.gameObject.GetInstanceID();
        }

        private void TryUnsubscribe(IDamageable target)
        {
            if (target == null || target is Object unityObject && unityObject == null)
            {
                return;
            }

            target.OnDeath -= Damageable_OnDeath;
        }
    }
}
