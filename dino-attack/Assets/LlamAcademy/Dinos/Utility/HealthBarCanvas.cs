using System.Collections.Generic;
using LlamAcademy.Dinos.UI;
using LlamAcademy.Dinos.Unit;
using UnityEngine;

namespace LlamAcademy.Dinos.Utility
{
    [RequireComponent(typeof(Canvas))]
    public class HealthBarCanvas : MonoBehaviour
    {
        public static HealthBarCanvas Instance { get; private set; }

        private readonly Dictionary<IDamageable, HealthBarEntry> HealthBars = new();

        private void Awake()
        {
            if (Instance != null)
            {
                Debug.LogError($"Multiple Health Bar Canvases in scene! Destroying this one ({name})!");
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void Update()
        {
            List<IDamageable> stale = null;
            foreach (KeyValuePair<IDamageable, HealthBarEntry> keyValuePair in HealthBars)
            {
                IDamageable damageable = keyValuePair.Key;
                HealthBarEntry entry = keyValuePair.Value;
                if (damageable == null || entry.HealthBar == null || damageable.Transform == null)
                {
                    stale ??= new List<IDamageable>();
                    stale.Add(damageable);
                }
                else
                {
                    Transform followTarget = entry.FollowTarget == null ? damageable.Transform : entry.FollowTarget;
                    Vector3 offset = entry.UseHealthBarOffset ? entry.HealthBar.FollowOffset : Vector3.zero;
                    entry.HealthBar.transform.position = followTarget.position + offset;
                }
            }

            if (stale != null)
            {
                foreach (IDamageable damageable in stale)
                {
                    if (HealthBars.Remove(damageable, out HealthBarEntry entry) && entry.HealthBar != null)
                    {
                        Destroy(entry.HealthBar.gameObject);
                    }
                }
            }
        }

        public void Register(HealthBar healthBar, IDamageable damageable)
        {
            if (healthBar == null || damageable == null)
            {
                return;
            }

            if (HealthBars.TryGetValue(damageable, out HealthBarEntry registered) &&
                registered.HealthBar != null && registered.HealthBar != healthBar)
            {
                registered.HealthBar.gameObject.SetActive(false);
            }
            HealthBars[damageable] = new HealthBarEntry(healthBar, damageable.Transform, true);
            healthBar.gameObject.SetActive(true);
            healthBar.transform.SetParent(transform);
            healthBar.transform.localRotation = Quaternion.identity;
            damageable.OnDeath -= HandleDamageableDeath;
            damageable.OnDeath += HandleDamageableDeath;
        }

        public bool SetFollowTarget(IDamageable damageable, Transform followTarget)
        {
            if (damageable == null || !HealthBars.TryGetValue(damageable, out HealthBarEntry entry))
            {
                return false;
            }

            if (followTarget == null)
            {
                entry.FollowTarget = damageable.Transform;
                entry.UseHealthBarOffset = true;
            }
            else
            {
                entry.FollowTarget = followTarget;
                entry.UseHealthBarOffset = false;
            }
            return true;
        }

        public void Unregister(IDamageable damageable, bool disableHealthBar)
        {
            if (damageable == null)
            {
                return;
            }

            damageable.OnDeath -= HandleDamageableDeath;
            if (HealthBars.Remove(damageable, out HealthBarEntry entry) &&
                disableHealthBar && entry.HealthBar != null)
            {
                entry.HealthBar.gameObject.SetActive(false);
            }
        }

        private void HandleDamageableDeath(IDamageable damageable)
        {
            damageable.OnDeath -= HandleDamageableDeath;
            if (HealthBars.Remove(damageable, out HealthBarEntry entry))
            {
                if (entry.HealthBar.OnDeathBehavior == HealthBar.DeathBehavior.Destroy)
                {
                    Destroy(entry.HealthBar.gameObject);
                }
                else if (entry.HealthBar.OnDeathBehavior == HealthBar.DeathBehavior.Disable)
                {
                    entry.HealthBar.gameObject.SetActive(false);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (IDamageable damageable in HealthBars.Keys)
            {
                if (damageable != null)
                {
                    damageable.OnDeath -= HandleDamageableDeath;
                }
            }
            HealthBars.Clear();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private sealed class HealthBarEntry
        {
            public readonly HealthBar HealthBar;
            public Transform FollowTarget;
            public bool UseHealthBarOffset;

            public HealthBarEntry(HealthBar healthBar, Transform followTarget, bool useHealthBarOffset)
            {
                HealthBar = healthBar;
                FollowTarget = followTarget;
                UseHealthBarOffset = useHealthBarOffset;
            }
        }
    }
}
