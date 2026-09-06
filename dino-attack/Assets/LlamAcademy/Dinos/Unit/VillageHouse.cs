using System;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.UI;
using LlamAcademy.Dinos.Utility;
using UnityEngine;

namespace LlamAcademy.Dinos.Unit
{
    [DisallowMultipleComponent]
    public sealed class VillageHouse : MonoBehaviour, IDamageable
    {
        [field: SerializeField, Min(0)] public int StableHouseId { get; private set; }
        [field: SerializeField] public HouseTier Tier { get; private set; } = HouseTier.Tier1;
        [field: SerializeField] public int MaxHealth { get; set; }
        [field: SerializeField] public int Health { get; set; }
        [field: SerializeField] public UnitSO UnitType { get; private set; }
        [field: SerializeField] public bool IsDestroyed { get; private set; }

        [SerializeField] private GameObject IntactVisualRoot;
        [SerializeField] private GameObject BeamRoot;
        [SerializeField] private Collider OccupancyCollider;
        [SerializeField] private DinoTargetMetadata TargetMetadata;
        [SerializeField] private HealthBar HealthBar;

        public Transform Transform => transform;
        public GameObject IntactVisual => IntactVisualRoot;
        public GameObject DestroyedBeamRoot => BeamRoot;
        public Collider Occupancy => OccupancyCollider;

        public event IDamageable.TakeDamageEvent OnTakeDamage;
        public event IDamageable.DeathEvent OnDeath;

        private void Awake()
        {
            RepairLayeredBattlefieldPresentation();
            ResetToIntactState();
        }

        private void Start()
        {
            if (HealthBar == null)
            {
                HealthBar = GetComponentInChildren<HealthBar>(true);
            }
            if (HealthBar != null && HealthBarCanvas.Instance != null)
            {
                HealthBar.SetProgress(1f);
                HealthBarCanvas.Instance.Register(HealthBar, this);
            }
        }

        public void Configure(
            int stableHouseId,
            HouseTier tier,
            UnitSO unitType,
            GameObject intactVisualRoot,
            GameObject beamRoot,
            Collider occupancyCollider,
            DinoTargetMetadata targetMetadata)
        {
            StableHouseId = Mathf.Max(0, stableHouseId);
            Tier = tier;
            UnitType = unitType;
            IntactVisualRoot = intactVisualRoot;
            BeamRoot = beamRoot;
            OccupancyCollider = occupancyCollider;
            TargetMetadata = targetMetadata;
            ResetToIntactState();
        }

        public void TakeDamage(int damage)
        {
            if (IsDestroyed || damage <= 0)
            {
                return;
            }

            int appliedDamage = Mathf.Min(damage, Health);
            Health -= appliedDamage;
            if (HealthBar != null)
            {
                HealthBar.SetProgress(MaxHealth > 0 ? (float)Health / MaxHealth : 0f);
            }
            OnTakeDamage?.Invoke(this, appliedDamage);
            if (Health == 0)
            {
                Die();
            }
        }

        public void Die()
        {
            if (IsDestroyed)
            {
                return;
            }

            IsDestroyed = true;
            Health = 0;
            if (TargetMetadata != null)
            {
                TargetMetadata.enabled = false;
            }

            if (IntactVisualRoot != null)
            {
                IntactVisualRoot.SetActive(false);
            }

            if (BeamRoot != null)
            {
                BeamRoot.SetActive(true);
            }

            if (OccupancyCollider != null)
            {
                OccupancyCollider.enabled = true;
            }

            OnDeath?.Invoke(this);
        }

        private void OnDestroy()
        {
            if (HealthBarCanvas.Instance != null)
            {
                HealthBarCanvas.Instance.Unregister(this, false);
            }
        }

        private void ResetToIntactState()
        {
            MaxHealth = HouseTierProfile.GetMaxHealth(Tier);
            Health = MaxHealth;
            if (HealthBar != null)
            {
                HealthBar.SetProgress(1f);
            }
            IsDestroyed = false;
            if (TargetMetadata != null)
            {
                TargetMetadata.enabled = true;
            }

            if (IntactVisualRoot != null)
            {
                IntactVisualRoot.SetActive(true);
            }

            if (BeamRoot != null)
            {
                BeamRoot.SetActive(false);
            }

            if (OccupancyCollider != null)
            {
                OccupancyCollider.enabled = true;
            }
        }

        private void RepairLayeredBattlefieldPresentation()
        {
            Transform localHut = null;
            for (int index = 0; index < transform.childCount; index++)
            {
                Transform child = transform.GetChild(index);
                if (child.name.StartsWith("Hut Visual", StringComparison.Ordinal))
                {
                    localHut = child;
                    break;
                }
            }

            if (localHut == null)
            {
                return;
            }

            IntactVisualRoot = localHut.gameObject;
            if (BeamRoot == null || BeamRoot.transform.IsChildOf(transform))
            {
                return;
            }

            GameObject localBeams = Instantiate(BeamRoot);
            localBeams.name = "Destroyed House Beams Runtime";
            localBeams.transform.SetParent(transform, true);
            float groundY = OccupancyCollider != null
                ? OccupancyCollider.bounds.min.y
                : transform.position.y;
            localBeams.transform.SetPositionAndRotation(
                new Vector3(transform.position.x, groundY + 0.18f, transform.position.z),
                transform.rotation);
            foreach (Collider collider in localBeams.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            BeamRoot = localBeams;
        }
    }
}
