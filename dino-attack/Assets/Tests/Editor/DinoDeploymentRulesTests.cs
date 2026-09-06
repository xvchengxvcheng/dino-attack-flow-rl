using LlamAcademy.Dinos.Deployment;
using LlamAcademy.Dinos.Map;
using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class DinoDeploymentRulesTests
    {
        private readonly List<Object> CreatedObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < CreatedObjects.Count; i++)
            {
                Object.DestroyImmediate(CreatedObjects[i]);
            }

            CreatedObjects.Clear();
        }

        [TestCase(DeploymentPhase.Setup)]
        [TestCase(DeploymentPhase.Running)]
        public void Evaluate_AllowsValidDeploymentDuringInteractivePhases(DeploymentPhase phase)
        {
            DeploymentResult result = DinoDeploymentRules.Evaluate(ValidRequest(phase));

            Assert.That(result.Allowed, Is.True);
            Assert.That(result.FailureReason, Is.EqualTo(DeploymentFailureReason.None));
        }

        [TestCase(DeploymentPhase.Ending)]
        [TestCase(DeploymentPhase.Scoring)]
        [TestCase(DeploymentPhase.EnemyRepairs)]
        [TestCase(DeploymentPhase.Ended)]
        public void Evaluate_RejectsNonInteractivePhases(DeploymentPhase phase)
        {
            DeploymentResult result = DinoDeploymentRules.Evaluate(ValidRequest(phase));

            Assert.That(result.Allowed, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(DeploymentFailureReason.InvalidState));
        }

        [Test]
        public void Evaluate_ReportsOneStableFailureReasonWithoutMutatingFood()
        {
            DeploymentRequest request = new(
                DeploymentPhase.Running,
                isKnownDino: true,
                foodAvailable: 9,
                foodCost: 10,
                worldPosition: new Vector3(2f, 0f, 3f),
                isInsideBounds: false,
                hasGround: false,
                hasNavMesh: false,
                hasOverlap: true,
                isBlockedByUi: true);

            DeploymentResult result = DinoDeploymentRules.Evaluate(request);

            Assert.That(result.FailureReason, Is.EqualTo(DeploymentFailureReason.InsufficientFood));
            Assert.That(request.FoodAvailable, Is.EqualTo(9));
        }

        [TestCase(false, true, true, false, false, DeploymentFailureReason.OutsideBounds)]
        [TestCase(true, false, true, false, false, DeploymentFailureReason.NoGround)]
        [TestCase(true, true, false, false, false, DeploymentFailureReason.NoNavMesh)]
        [TestCase(true, true, true, true, false, DeploymentFailureReason.Overlap)]
        [TestCase(true, true, true, false, true, DeploymentFailureReason.BlockedByUI)]
        public void Evaluate_ReportsSpatialAndInputFailures(
            bool insideBounds,
            bool hasGround,
            bool hasNavMesh,
            bool hasOverlap,
            bool blockedByUi,
            DeploymentFailureReason expected)
        {
            DeploymentRequest request = new(
                DeploymentPhase.Setup,
                isKnownDino: true,
                foodAvailable: 100,
                foodCost: 10,
                worldPosition: Vector3.zero,
                isInsideBounds: insideBounds,
                hasGround: hasGround,
                hasNavMesh: hasNavMesh,
                hasOverlap: hasOverlap,
                isBlockedByUi: blockedByUi);

            Assert.That(DinoDeploymentRules.Evaluate(request).FailureReason, Is.EqualTo(expected));
        }

        [Test]
        public void Evaluate_RejectsUnknownDinoBeforeCheckingCost()
        {
            DeploymentRequest request = new(
                DeploymentPhase.Setup,
                isKnownDino: false,
                foodAvailable: 0,
                foodCost: 10,
                worldPosition: Vector3.zero,
                isInsideBounds: true,
                hasGround: true,
                hasNavMesh: true,
                hasOverlap: false,
                isBlockedByUi: false);

            Assert.That(
                DinoDeploymentRules.Evaluate(request).FailureReason,
                Is.EqualTo(DeploymentFailureReason.UnknownType));
        }

        [Test]
        public void ContainsPosition_UsesApprovedZoneUnionWhenZonesExist()
        {
            Bounds fallback = new(Vector3.zero, new Vector3(100f, 2f, 100f));
            Bounds[] zones =
            {
                new(new Vector3(-10f, 0f, 0f), new Vector3(4f, 2f, 4f)),
                new(new Vector3(10f, 0f, 0f), new Vector3(4f, 2f, 4f))
            };

            Assert.That(DinoDeploymentZoneLayout.ContainsPosition(fallback, zones, new Vector3(-10f, 20f, 1f), allowLegacyFallback: false), Is.True);
            Assert.That(DinoDeploymentZoneLayout.ContainsPosition(fallback, zones, new Vector3(0f, 0f, 0f), allowLegacyFallback: false), Is.False);
        }

        [Test]
        public void ContainsPosition_AcceptsFiveZoneCentersAndRejectsNorthGapAndRemovedRainforest()
        {
            Bounds fallback = new(Vector3.zero, new Vector3(200f, 2f, 200f));
            Bounds[] zones = FiveApprovedBounds();

            foreach (Bounds zone in zones)
            {
                Assert.That(
                    DinoDeploymentZoneLayout.ContainsPosition(fallback, zones, zone.center, allowLegacyFallback: false),
                    Is.True,
                    $"Expected {zone.center} to remain in its approved zone.");
            }

            Assert.That(
                DinoDeploymentZoneLayout.ContainsPosition(fallback, zones, new Vector3(0f, 0f, 30f), allowLegacyFallback: false),
                Is.False,
                "The narrow northern backdrop is not a sixth deployment zone.");
            Assert.That(
                DinoDeploymentZoneLayout.ContainsPosition(fallback, zones, new Vector3(-30f, 0f, 30f), allowLegacyFallback: false),
                Is.False,
                "The removed rainforest clearing must remain unavailable.");
        }

        [Test]
        public void ContainsPosition_RejectsEmptyExplicitZonesUnlessLegacyFallbackIsExplicitlyEnabled()
        {
            Bounds fallback = new(Vector3.zero, new Vector3(10f, 2f, 10f));

            Assert.That(
                DinoDeploymentZoneLayout.ContainsPosition(fallback, null, new Vector3(5f, 99f, 5f), allowLegacyFallback: false),
                Is.False);
            Assert.That(
                DinoDeploymentZoneLayout.ContainsPosition(fallback, System.Array.Empty<Bounds>(), new Vector3(5f, 0f, 5f), allowLegacyFallback: false),
                Is.False);
            Assert.That(
                DinoDeploymentZoneLayout.ContainsPosition(fallback, System.Array.Empty<Bounds>(), new Vector3(5f, 0f, 5f), allowLegacyFallback: true),
                Is.True);
        }

        [Test]
        public void DeploymentZone_UsesQuadrilateralContainmentAndIgnoresHeight()
        {
            DinoDeploymentZone zone = CreateZone(DeploymentZoneId.Z1RuinsForecourt, "Z1", new Vector3(-10f, 0f, 0f));

            Assert.That(zone.Contains(new Vector3(-10f, 500f, 0f)), Is.True);
            Assert.That(zone.Contains(new Vector3(-6f, 0f, 4f)), Is.False,
                "This point is inside the legacy BoxCollider AABB but outside the quadrilateral.");
        }

        [Test]
        public void DeploymentZone_ResolvesStrictRelativeCoordinatesThroughTransform()
        {
            DinoDeploymentZone zone = CreateZone(DeploymentZoneId.Z1RuinsForecourt, "Z1", new Vector3(10f, 2f, -5f));
            zone.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            Assert.That(zone.TryResolveRelative(new Vector2(0.25f, 0.75f), out Vector3 resolved), Is.True);
            Vector3 expected = zone.transform.TransformPoint(new Vector3(-1.0625f, 0f, 1.5625f));
            AssertVector3(resolved, expected);
            Assert.That(zone.Contains(resolved), Is.True);
            Assert.That(zone.TryResolveRelative(new Vector2(-0.001f, 0.5f), out _), Is.False);
            Assert.That(zone.TryResolveRelative(new Vector2(float.NaN, 0.5f), out _), Is.False);
        }

        [Test]
        public void DeploymentZone_ExposesOrderedWorldVertices()
        {
            DinoDeploymentZone zone = CreateZone(DeploymentZoneId.Z1RuinsForecourt, "Z1", new Vector3(3f, 1f, -7f));

            Assert.That(zone.IsGeometryValid, Is.True);
            Assert.That(zone.WorldVertices.Count, Is.EqualTo(4));
            AssertVector3(zone.WorldVertices[0], new Vector3(-1f, 1f, -11f));
            AssertVector3(zone.WorldVertices[1], new Vector3(7f, 1f, -9f));
            AssertVector3(zone.WorldVertices[2], new Vector3(6f, 1f, -3f));
            AssertVector3(zone.WorldVertices[3], new Vector3(1f, 1f, -4f));
        }

        [Test]
        public void ValidateZoneConfiguration_RejectsInvalidZoneGeometry()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();
            zones[2].ConfigureGeometry(Vector2.zero, Vector2.right, new Vector2(2f, 0f), new Vector2(3f, 0f));

            DinoDeploymentService service = CreateService(zones);

            Assert.That(zones[2].IsGeometryValid, Is.False);
            Assert.That(service.ValidateZoneConfiguration(), Is.False);
            Assert.That(service.TryGetZone(zones[0].transform.position, out _), Is.False);
        }

        [Test]
        public void TryGetZone_FailsClosedForAnInvalidNonEmptyConfiguration()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();
            DinoDeploymentService service = CreateService(zones[0], zones[0], zones[2], zones[3], zones[4]);

            Assert.That(service.ValidateZoneConfiguration(), Is.False);
            Assert.That(service.TryGetZone(zones[0].transform.position, out _), Is.False);
        }

        [Test]
        public void ConfiguredZones_ExposesAReadOnlyCanonicalServiceCollection()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();
            DinoDeploymentService service = CreateService(zones);

            System.Collections.Generic.IReadOnlyList<DinoDeploymentZone> configured = service.ConfiguredZones;

            Assert.That(configured, Is.Not.TypeOf<DinoDeploymentZone[]>());
            Assert.That(configured.Count, Is.EqualTo(5));
            Assert.That(configured[0], Is.SameAs(zones[0]));
            Assert.That(configured[1], Is.SameAs(zones[1]));
        }

        [Test]
        public void ValidateZoneConfiguration_AcceptsExactlyTheFiveApprovedUniqueIds()
        {
            DinoDeploymentService service = CreateService(CreateFiveZones());

            Assert.That(service.ValidateZoneConfiguration(), Is.True);
        }

        [TestCase(0)]
        [TestCase(4)]
        [TestCase(6)]
        public void ValidateZoneConfiguration_RejectsAnyCountOtherThanFive(int count)
        {
            DinoDeploymentZone[] legalZones = CreateFiveZones();
            DinoDeploymentZone[] configured = new DinoDeploymentZone[count];
            for (int i = 0; i < count && i < legalZones.Length; i++)
            {
                configured[i] = legalZones[i];
            }

            if (count == 6)
            {
                configured[5] = CreateZone(DeploymentZoneId.Z1RuinsForecourt, "Sixth", new Vector3(40f, 0f, 0f));
            }

            Assert.That(CreateService(configured).ValidateZoneConfiguration(), Is.False);
        }

        [Test]
        public void ValidateZoneConfiguration_RejectsDuplicateAndMissingIds()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();
            SetZoneId(zones[1], DeploymentZoneId.Z1RuinsForecourt);

            Assert.That(CreateService(zones).ValidateZoneConfiguration(), Is.False);
        }

        [Test]
        public void ValidateZoneConfiguration_RejectsIllegalCastIds()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();
            SetZoneId(zones[3], (DeploymentZoneId)999);

            Assert.That(CreateService(zones).ValidateZoneConfiguration(), Is.False);
        }

        [Test]
        public void ValidateZoneConfiguration_RejectsNullAndDuplicateReferences()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();

            Assert.That(CreateService(zones[0], null, zones[2], zones[3], zones[4]).ValidateZoneConfiguration(), Is.False);
            Assert.That(CreateService(zones[0], zones[0], zones[2], zones[3], zones[4]).ValidateZoneConfiguration(), Is.False);
        }

        [Test]
        public void LegacyFallback_IsAvailableOnlyForAnEmptyZoneArray()
        {
            DinoDeploymentService service = CreateService();
            BoxCollider legacyBounds = CreateTracked("Legacy Bounds").AddComponent<BoxCollider>();
            legacyBounds.size = new Vector3(20f, 2f, 20f);
            SetPrivateField(service, "PlacementBounds", legacyBounds);
            SetPrivateField(service, "UseLegacyPlacementBounds", true);

            Assert.That(InvokeIsInsideApprovedZone(service, Vector3.zero), Is.True);

            DinoDeploymentZone[] zones = CreateFiveZones();
            SetPrivateField(service, "PlacementZones", new[] { zones[0], zones[1], zones[2], zones[3] });

            Assert.That(InvokeIsInsideApprovedZone(service, Vector3.zero), Is.False);
        }

        [Test]
        public void DeploymentHoverInfo_RetainsTheSameZoneInstance()
        {
            DinoDeploymentZone zone = CreateZone(DeploymentZoneId.Z3RiverTerrace, "Z3", new Vector3(0f, 0f, -20f));

            DeploymentHoverInfo info = new(zone);

            Assert.That(info.Zone, Is.SameAs(zone));
        }

        [Test]
        public void ZonePresenter_ShowsAllBoundariesHighlightsHoveredZoneAndClearsWhenHidden()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();
            Renderer[] boundaries = new Renderer[zones.Length];
            for (int i = 0; i < zones.Length; i++)
            {
                boundaries[i] = zones[i].GetComponent<Renderer>();
            }

            DinoDeploymentService service = CreateService(zones);
            DinoDeploymentZonePresenter presenter = CreatePresenter(
                service,
                CreateBindings(zones, boundaries));

            presenter.ShowForSelection();
            presenter.UpdateHover(zones[2].transform.position);

            Assert.That(boundaries, Has.All.Property(nameof(Renderer.enabled)).True);
            Assert.That(presenter.CurrentHover.HasValue, Is.True);
            Assert.That(presenter.CurrentHover.Value.Zone, Is.SameAs(zones[2]));
            Assert.That(presenter.IsHighlighted(zones[2]), Is.True);
            Assert.That(presenter.IsHighlighted(zones[1]), Is.False);

            presenter.SetInteractive(false);

            Assert.That(boundaries, Has.All.Property(nameof(Renderer.enabled)).False);
            Assert.That(presenter.CurrentHover.HasValue, Is.False);
        }

        [Test]
        public void ZonePresenter_UsesExactBindingWhenBindingsAreOutOfOrder()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();
            Renderer[] boundaries = GetBoundaries(zones);
            DinoDeploymentService service = CreateService(zones);
            DinoDeploymentZonePresenter.ZoneBoundaryBinding[] bindings =
            {
                new(zones[4], boundaries[4]),
                new(zones[2], boundaries[2]),
                new(zones[0], boundaries[0]),
                new(zones[3], boundaries[3]),
                new(zones[1], boundaries[1])
            };
            DinoDeploymentZonePresenter presenter = CreatePresenter(service, bindings);

            presenter.ShowForSelection();
            presenter.UpdateHover(zones[2].transform.position);

            Assert.That(presenter.CurrentHover.Value.Zone, Is.SameAs(zones[2]));
            Assert.That(GetBoundaryAlpha(boundaries[2]), Is.EqualTo(1f).Within(0.001f));
            Assert.That(GetBoundaryAlpha(boundaries[0]), Is.EqualTo(0.2f).Within(0.001f));
        }

        [Test]
        public void ZonePresenter_RejectsNullAndDuplicateZoneBindings()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();
            Renderer[] boundaries = GetBoundaries(zones);
            DinoDeploymentService service = CreateService(zones);
            DinoDeploymentZonePresenter.ZoneBoundaryBinding[] bindings =
            {
                new(zones[0], boundaries[0]),
                new(null, boundaries[1]),
                new(zones[2], boundaries[2]),
                new(zones[2], boundaries[3]),
                new(zones[4], boundaries[4])
            };
            DinoDeploymentZonePresenter presenter = CreatePresenter(service, bindings);

            presenter.ShowForSelection();
            presenter.UpdateHover(zones[0].transform.position);

            Assert.That(presenter.IsConfigurationValid, Is.False);
            Assert.That(boundaries, Has.All.Property(nameof(Renderer.enabled)).False);
            Assert.That(presenter.CurrentHover.HasValue, Is.False);
        }

        [Test]
        public void ZonePresenter_RejectsMissingOrNonServiceZoneBindings()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();
            Renderer[] boundaries = GetBoundaries(zones);
            DinoDeploymentZone externalZone = CreateZone(DeploymentZoneId.Z1RuinsForecourt, "External", new Vector3(40f, 0f, 0f));
            DinoDeploymentService service = CreateService(zones);
            DinoDeploymentZonePresenter.ZoneBoundaryBinding[] bindings =
            {
                new(zones[0], boundaries[0]),
                new(zones[1], boundaries[1]),
                new(zones[2], boundaries[2]),
                new(zones[3], boundaries[3]),
                new(externalZone, boundaries[4])
            };
            DinoDeploymentZonePresenter presenter = CreatePresenter(service, bindings);

            presenter.ShowForSelection();

            Assert.That(presenter.IsConfigurationValid, Is.False);
            Assert.That(boundaries, Has.All.Property(nameof(Renderer.enabled)).False);
        }

        [Test]
        public void ZonePresenter_RejectsDuplicateOrMissingRenderersAndInvalidServiceZones()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();
            Renderer[] boundaries = GetBoundaries(zones);
            DinoDeploymentService service = CreateService(zones);
            DinoDeploymentZonePresenter.ZoneBoundaryBinding[] duplicateRendererBindings =
            {
                new(zones[0], boundaries[0]),
                new(zones[1], boundaries[0]),
                new(zones[2], boundaries[2]),
                new(zones[3], boundaries[3]),
                new(zones[4], null)
            };
            DinoDeploymentZonePresenter duplicateRendererPresenter = CreatePresenter(service, duplicateRendererBindings);

            duplicateRendererPresenter.ShowForSelection();

            Assert.That(duplicateRendererPresenter.IsConfigurationValid, Is.False);
            Assert.That(boundaries[0].enabled, Is.False);
            Assert.That(boundaries[2].enabled, Is.False);
            Assert.That(boundaries[3].enabled, Is.False);

            DinoDeploymentService invalidService = CreateService(zones);
            SetPrivateField(invalidService, "PlacementZones", new DinoDeploymentZone[] { zones[0], null, zones[2], zones[3], zones[4] });
            DinoDeploymentZonePresenter invalidServicePresenter = CreatePresenter(
                invalidService,
                CreateBindings(zones, boundaries));

            invalidServicePresenter.ShowForSelection();

            Assert.That(invalidServicePresenter.IsConfigurationValid, Is.False);
            Assert.That(boundaries, Has.All.Property(nameof(Renderer.enabled)).False);
        }

        [Test]
        public void ZonePresenter_HidesBoundariesWhenTheServiceIdContractIsInvalid()
        {
            DinoDeploymentZone[] zones = CreateFiveZones();
            Renderer[] boundaries = GetBoundaries(zones);
            SetZoneId(zones[1], DeploymentZoneId.Z1RuinsForecourt);
            DinoDeploymentZonePresenter presenter = CreatePresenter(CreateService(zones), CreateBindings(zones, boundaries));

            presenter.ShowForSelection();
            presenter.UpdateHover(zones[0].transform.position);

            Assert.That(presenter.IsConfigurationValid, Is.False);
            Assert.That(boundaries, Has.All.Property(nameof(Renderer.enabled)).False);
            Assert.That(presenter.CurrentHover.HasValue, Is.False);
        }

        private static Bounds[] FiveApprovedBounds() => new Bounds[]
        {
            new(new Vector3(-20f, 0f, 0f), new Vector3(8f, 2f, 8f)),
            new(new Vector3(-10f, 0f, -12f), new Vector3(12f, 2f, 10f)),
            new(new Vector3(0f, 0f, -26f), new Vector3(18f, 2f, 10f)),
            new(new Vector3(12f, 0f, -12f), new Vector3(10f, 2f, 10f)),
            new(new Vector3(20f, 0f, 0f), new Vector3(8f, 2f, 8f))
        };

        private DinoDeploymentZone CreateZone(DeploymentZoneId id, string name, Vector3 center)
        {
            GameObject zoneObject = CreateTracked(name);
            BoxCollider collider = zoneObject.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(8f, 2f, 8f);
            zoneObject.transform.position = center;
            Physics.SyncTransforms();
            zoneObject.AddComponent<MeshRenderer>();
            DinoDeploymentZone zone = zoneObject.AddComponent<DinoDeploymentZone>();
            zone.ConfigureGeometry(
                new Vector2(-4f, -4f),
                new Vector2(4f, -2f),
                new Vector2(3f, 4f),
                new Vector2(-2f, 3f));
            SetZoneId(zone, id);
            return zone;
        }

        private DinoDeploymentZone[] CreateFiveZones() => new[]
        {
            CreateZone(DeploymentZoneId.Z1RuinsForecourt, "Z1", new Vector3(-20f, 0f, 0f)),
            CreateZone(DeploymentZoneId.Z2OpenMeadow, "Z2", new Vector3(-10f, 0f, -10f)),
            CreateZone(DeploymentZoneId.Z3RiverTerrace, "Z3", new Vector3(0f, 0f, -20f)),
            CreateZone(DeploymentZoneId.Z4PalmGrove, "Z4", new Vector3(10f, 0f, -10f)),
            CreateZone(DeploymentZoneId.Z5RockyShelf, "Z5", new Vector3(20f, 0f, 0f))
        };

        private static Renderer[] GetBoundaries(DinoDeploymentZone[] zones)
        {
            Renderer[] boundaries = new Renderer[zones.Length];
            for (int i = 0; i < zones.Length; i++)
            {
                boundaries[i] = zones[i].GetComponent<Renderer>();
            }

            return boundaries;
        }

        private DinoDeploymentService CreateService(params DinoDeploymentZone[] zones)
        {
            DinoDeploymentService service = CreateTracked("Service").AddComponent<DinoDeploymentService>();
            SetPrivateField(service, "PlacementZones", zones);
            return service;
        }

        private DinoDeploymentZonePresenter CreatePresenter(
            DinoDeploymentService service,
            DinoDeploymentZonePresenter.ZoneBoundaryBinding[] bindings)
        {
            DinoDeploymentZonePresenter presenter = CreateTracked("Presenter").AddComponent<DinoDeploymentZonePresenter>();
            SetPrivateField(presenter, "DeploymentService", service);
            SetPrivateField(presenter, "Bindings", bindings);
            return presenter;
        }

        private static DinoDeploymentZonePresenter.ZoneBoundaryBinding[] CreateBindings(
            DinoDeploymentZone[] zones,
            Renderer[] boundaries)
        {
            DinoDeploymentZonePresenter.ZoneBoundaryBinding[] bindings =
                new DinoDeploymentZonePresenter.ZoneBoundaryBinding[zones.Length];
            for (int i = 0; i < zones.Length; i++)
            {
                bindings[i] = new DinoDeploymentZonePresenter.ZoneBoundaryBinding(zones[i], boundaries[i]);
            }

            return bindings;
        }

        private static float GetBoundaryAlpha(Renderer renderer)
        {
            MaterialPropertyBlock block = new();
            renderer.GetPropertyBlock(block);
            return block.GetColor("_BaseColor").a;
        }

        private GameObject CreateTracked(string name)
        {
            GameObject gameObject = new(name);
            CreatedObjects.Add(gameObject);
            return gameObject;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected serialized field {fieldName}.");
            field.SetValue(target, value);
        }

        private static void SetZoneId(DinoDeploymentZone zone, DeploymentZoneId id) =>
            SetPrivateField(zone, "<Id>k__BackingField", id);

        private static bool InvokeIsInsideApprovedZone(DinoDeploymentService service, Vector3 position)
        {
            MethodInfo method = typeof(DinoDeploymentService).GetMethod(
                "IsInsideApprovedZone",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (bool)method.Invoke(service, new object[] { position });
        }

        private static void AssertVector3(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }

        private static DeploymentRequest ValidRequest(DeploymentPhase phase) => new(
            phase,
            isKnownDino: true,
            foodAvailable: 100,
            foodCost: 10,
            worldPosition: new Vector3(2f, 0f, 3f),
            isInsideBounds: true,
            hasGround: true,
            hasNavMesh: true,
            hasOverlap: false,
            isBlockedByUi: false);
    }
}
