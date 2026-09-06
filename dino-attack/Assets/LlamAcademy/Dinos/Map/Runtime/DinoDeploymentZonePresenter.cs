using System;
using System.Collections.Generic;
using LlamAcademy.Dinos.Deployment;
using UnityEngine;

namespace LlamAcademy.Dinos.Map
{
    public sealed class DinoDeploymentZonePresenter : MonoBehaviour
    {
        [Serializable]
        public sealed class ZoneBoundaryBinding
        {
            [SerializeField] private DinoDeploymentZone Zone;
            [SerializeField] private Renderer BoundaryRenderer;

            public DinoDeploymentZone DeploymentZone => Zone;
            public Renderer Renderer => BoundaryRenderer;

            public ZoneBoundaryBinding(DinoDeploymentZone zone, Renderer boundaryRenderer)
            {
                Zone = zone;
                BoundaryRenderer = boundaryRenderer;
            }
        }

        [SerializeField] private DinoDeploymentService DeploymentService;
        [SerializeField] private ZoneBoundaryBinding[] Bindings = Array.Empty<ZoneBoundaryBinding>();
        [SerializeField, Range(0f, 1f)] private float IdleIntensity = 0.2f;
        [SerializeField, Range(0f, 1f)] private float HoverIntensity = 1f;
        [SerializeField] private Color BoundaryColor = Color.cyan;

        private MaterialPropertyBlock PropertyBlock;
        private bool IsSelecting;

        public DeploymentHoverInfo? CurrentHover { get; private set; }
        public bool IsConfigurationValid { get; private set; }
        public event Action<DeploymentHoverInfo?> HoverChanged;

        public bool ConfigureZones(
            DinoDeploymentService deploymentService,
            IReadOnlyList<DinoDeploymentZone> zones)
        {
            DeploymentService = deploymentService;
            if (zones == null)
            {
                Bindings = Array.Empty<ZoneBoundaryBinding>();
                return ValidateConfiguration();
            }

            Bindings = new ZoneBoundaryBinding[zones.Count];
            for (int index = 0; index < zones.Count; index++)
            {
                DinoDeploymentZone zone = zones[index];
                Renderer boundary = zone == null
                    ? null
                    : zone.transform.Find("Boundary")?.GetComponent<Renderer>();
                Bindings[index] = new ZoneBoundaryBinding(zone, boundary);
            }

            Hide();
            return ValidateConfiguration();
        }

        private void Awake()
        {
            ValidateConfiguration();
            Hide();
        }

        private void OnValidate()
        {
            if (!ValidateConfiguration())
            {
                Hide();
            }
        }

        public bool ValidateConfiguration()
        {
            IsConfigurationValid = false;
            if (DeploymentService == null || !DeploymentService.ValidateZoneConfiguration())
            {
                HideKnownBoundaries();
                return false;
            }

            IReadOnlyList<DinoDeploymentZone> serviceZones = DeploymentService.ConfiguredZones;
            HashSet<DinoDeploymentZone> expectedZones = new();
            for (int i = 0; i < serviceZones.Count; i++)
            {
                expectedZones.Add(serviceZones[i]);
            }

            if (Bindings == null || Bindings.Length != expectedZones.Count)
            {
                HideKnownBoundaries();
                return false;
            }

            HashSet<DinoDeploymentZone> boundZones = new();
            HashSet<Renderer> boundRenderers = new();
            for (int i = 0; i < Bindings.Length; i++)
            {
                ZoneBoundaryBinding binding = Bindings[i];
                if (binding == null ||
                    binding.DeploymentZone == null ||
                    binding.Renderer == null ||
                    !expectedZones.Contains(binding.DeploymentZone) ||
                    !boundZones.Add(binding.DeploymentZone) ||
                    !boundRenderers.Add(binding.Renderer))
                {
                    HideKnownBoundaries();
                    return false;
                }
            }

            IsConfigurationValid = boundZones.Count == expectedZones.Count;
            if (!IsConfigurationValid)
            {
                HideKnownBoundaries();
            }

            return IsConfigurationValid;
        }

        public void ShowForSelection()
        {
            if (!ValidateConfiguration())
            {
                Hide();
                return;
            }

            IsSelecting = true;
            CurrentHover = null;
            SetAllBoundariesVisible(true);
            ApplyAppearance();
            HoverChanged?.Invoke(null);
        }

        public void UpdateHover(Vector3 position)
        {
            if (!IsSelecting || !ValidateConfiguration())
            {
                if (!IsConfigurationValid)
                {
                    Hide();
                }

                return;
            }

            if (!DeploymentService.TryGetZone(position, out DinoDeploymentZone hoveredZone))
            {
                CurrentHover = null;
                ApplyAppearance();
                HoverChanged?.Invoke(null);
                return;
            }

            ZoneBoundaryBinding binding = FindBinding(hoveredZone);
            if (binding == null)
            {
                Hide();
                return;
            }

            CurrentHover = new DeploymentHoverInfo(binding.DeploymentZone);
            ApplyAppearance();
            HoverChanged?.Invoke(CurrentHover);
        }

        public bool IsHighlighted(DinoDeploymentZone zone) =>
            IsSelecting && CurrentHover.HasValue && CurrentHover.Value.Zone == zone;

        public void SetInteractive(bool isInteractive)
        {
            if (!isInteractive)
            {
                Hide();
            }
        }

        public void Hide()
        {
            IsSelecting = false;
            CurrentHover = null;
            SetAllBoundariesVisible(false);
            HoverChanged?.Invoke(null);
        }

        private void SetAllBoundariesVisible(bool visible)
        {
            if (Bindings == null)
            {
                return;
            }

            for (int i = 0; i < Bindings.Length; i++)
            {
                Renderer renderer = Bindings[i]?.Renderer;
                if (renderer != null)
                {
                    renderer.enabled = visible;
                }
            }
        }

        private void HideKnownBoundaries()
        {
            IsSelecting = false;
            CurrentHover = null;
            SetAllBoundariesVisible(false);
        }

        private ZoneBoundaryBinding FindBinding(DinoDeploymentZone zone)
        {
            for (int i = 0; i < Bindings.Length; i++)
            {
                ZoneBoundaryBinding binding = Bindings[i];
                if (binding != null && binding.DeploymentZone == zone)
                {
                    return binding;
                }
            }

            return null;
        }

        private void ApplyAppearance()
        {
            PropertyBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < Bindings.Length; i++)
            {
                ZoneBoundaryBinding binding = Bindings[i];
                Renderer boundary = binding?.Renderer;
                if (boundary == null)
                {
                    continue;
                }

                float intensity = binding != null && IsHighlighted(binding.DeploymentZone)
                    ? HoverIntensity
                    : IdleIntensity;
                Color color = BoundaryColor * intensity;
                color.a = intensity;
                boundary.GetPropertyBlock(PropertyBlock);
                PropertyBlock.SetColor("_BaseColor", color);
                PropertyBlock.SetColor("_Color", color);
                boundary.SetPropertyBlock(PropertyBlock);
            }
        }
    }
}
