using LlamAcademy.Dinos.Map;
using NUnit.Framework;
using System;
using System.Linq;
using UnityEngine;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class OpenTropicalBattlefieldMapCoreTests
    {
        [TestCase(TerrainSurfaceKind.StoneRoad, 1.10f)]
        [TestCase(TerrainSurfaceKind.Grass, 1.00f)]
        [TestCase(TerrainSurfaceKind.Mud, 0.85f)]
        [TestCase(TerrainSurfaceKind.Slope, 0.80f)]
        [TestCase(TerrainSurfaceKind.ShallowWater, 0.70f)]
        public void GetMultiplier_ReturnsApprovedValue(TerrainSurfaceKind surface, float expected)
        {
            Assert.That(TerrainSpeedProfile.GetMultiplier(surface), Is.EqualTo(expected).Within(0.0001f));
        }

        [TestCase(TerrainSurfaceKind.Grass, 1)]
        [TestCase(TerrainSurfaceKind.StoneRoad, 2)]
        [TestCase(TerrainSurfaceKind.Slope, 3)]
        [TestCase(TerrainSurfaceKind.Mud, 4)]
        [TestCase(TerrainSurfaceKind.ShallowWater, 5)]
        public void GetPriority_OrdersSurfaceResolution(TerrainSurfaceKind surface, int expected)
        {
            Assert.That(TerrainSpeedProfile.GetPriority(surface), Is.EqualTo(expected));
        }

        [Test]
        public void DeploymentZoneIds_ExposeTheApprovedFiveZoneContract()
        {
            DeploymentZoneId[] expected =
            {
                DeploymentZoneId.Z1RuinsForecourt,
                DeploymentZoneId.Z2OpenMeadow,
                DeploymentZoneId.Z3RiverTerrace,
                DeploymentZoneId.Z4PalmGrove,
                DeploymentZoneId.Z5RockyShelf
            };

            Assert.That(Enum.GetValues(typeof(DeploymentZoneId)), Is.EqualTo(expected));
        }

        [Test]
        public void SurfaceStack_UsesHighestPriorityAndRestoresPreviousSurface()
        {
            TerrainSurfaceStack stack = new();
            stack.Enter(TerrainSurfaceKind.StoneRoad);
            stack.Enter(TerrainSurfaceKind.ShallowWater);

            Assert.That(stack.Current, Is.EqualTo(TerrainSurfaceKind.ShallowWater));

            stack.Exit(TerrainSurfaceKind.ShallowWater);

            Assert.That(stack.Current, Is.EqualTo(TerrainSurfaceKind.StoneRoad));
        }

        [Test]
        public void SurfaceStack_TracksDuplicateEntriesAndFallsBackToGrass()
        {
            TerrainSurfaceStack stack = new();
            stack.Enter(TerrainSurfaceKind.Mud);
            stack.Enter(TerrainSurfaceKind.Mud);
            stack.Exit(TerrainSurfaceKind.Mud);

            Assert.That(stack.Current, Is.EqualTo(TerrainSurfaceKind.Mud));

            stack.Exit(TerrainSurfaceKind.Mud);
            stack.Exit(TerrainSurfaceKind.Mud);

            Assert.That(stack.Current, Is.EqualTo(TerrainSurfaceKind.Grass));
        }

        [Test]
        public void SurfaceStack_ClearRestoresGrassFallback()
        {
            TerrainSurfaceStack stack = new();
            stack.Enter(TerrainSurfaceKind.Slope);
            stack.Enter(TerrainSurfaceKind.StoneRoad);

            stack.Clear();

            Assert.That(stack.Current, Is.EqualTo(TerrainSurfaceKind.Grass));
        }

        [Test]
        public void ClampTarget_ConstrainsEveryAxisToWorldBounds()
        {
            Bounds worldBounds = new(new Vector3(0f, 10f, 0f), new Vector3(20f, 4f, 30f));

            Vector3 clamped = CameraFramingMath.ClampTarget(new Vector3(20f, 20f, -30f), worldBounds);

            Assert.That(clamped, Is.EqualTo(new Vector3(10f, 12f, -15f)));
        }

        [Test]
        public void ClampZoom_ClampsAtBothConfiguredBounds()
        {
            Assert.That(CameraFramingMath.ClampZoom(0.25f, 0.5f, 2f), Is.EqualTo(0.5f));
            Assert.That(CameraFramingMath.ClampZoom(3f, 0.5f, 2f), Is.EqualTo(2f));
        }

        [Test]
        public void ScaleFollowOffset_PreservesTheHomeOffsetAndAppliesZoom()
        {
            Vector3 homeOffset = new(0f, 22f, 11f);

            Vector3 zoomed = CameraFramingMath.ScaleFollowOffset(homeOffset, 0.5f);

            Assert.That(zoomed, Is.EqualTo(new Vector3(0f, 11f, 5.5f)));
            Assert.That(homeOffset, Is.EqualTo(new Vector3(0f, 22f, 11f)));
        }

        [Test]
        public void PanTarget_AppliesWorldDeltaThenClampsToBattlefieldBounds()
        {
            Bounds bounds = new(new Vector3(-3.5f, -2.5f, -12f), new Vector3(104f, 6f, 82f));

            Vector3 panned = CameraFramingMath.PanTarget(
                new Vector3(-3.5f, -2.5f, -12f),
                new Vector3(100f, 0f, -100f),
                bounds);

            Assert.That(panned, Is.EqualTo(new Vector3(48.5f, -2.5f, -53f)));
        }

        [Test]
        public void NextZoomScale_UsesWheelDeltaAndClampsAtBothLimits()
        {
            Assert.That(CameraFramingMath.NextZoomScale(1f, 10f, 0.1f, 0.45f, 1.35f), Is.EqualTo(0.45f));
            Assert.That(CameraFramingMath.NextZoomScale(1f, -10f, 0.1f, 0.45f, 1.35f), Is.EqualTo(1.35f));
        }

        [Test]
        public void DragTarget_UsesWorldPlaneDeltaWithoutChangingHeight()
        {
            Bounds bounds = new(new Vector3(-3.5f, -2.5f, -12f), new Vector3(104f, 6f, 82f));
            Vector3 targetAtDragStart = new(-3.5f, -2.5f, -12f);

            Vector3 dragged = CameraFramingMath.DragTarget(
                targetAtDragStart,
                new Vector3(4f, 0f, 7f),
                new Vector3(1f, 0f, 2f),
                bounds);

            Assert.That(dragged, Is.EqualTo(new Vector3(-0.5f, -2.5f, -7f)));
        }

        [Test]
        public void IsScreenPositionInside_RejectsCursorOutsideTheGameViewport()
        {
            Assert.That(CameraFramingMath.IsScreenPositionInside(new Vector2(0f, 0f), 1920, 1080), Is.True);
            Assert.That(CameraFramingMath.IsScreenPositionInside(new Vector2(1919.999f, 1079.999f), 1920, 1080), Is.True);
            Assert.That(CameraFramingMath.IsScreenPositionInside(new Vector2(1920f, 500f), 1920, 1080), Is.False);
            Assert.That(CameraFramingMath.IsScreenPositionInside(new Vector2(500f, 1080f), 1920, 1080), Is.False);
            Assert.That(CameraFramingMath.IsScreenPositionInside(new Vector2(-1f, 500f), 1920, 1080), Is.False);
            Assert.That(CameraFramingMath.IsScreenPositionInside(new Vector2(2000f, 500f), 1920, 1080), Is.False);
        }

        [Test]
        public void IsScreenPositionInside_UsesOffsetPixelRectWithExclusiveUpperBounds()
        {
            Rect pixelRect = new(100f, 50f, 800f, 600f);

            Assert.That(CameraFramingMath.IsScreenPositionInside(new Vector2(100f, 50f), pixelRect), Is.True);
            Assert.That(CameraFramingMath.IsScreenPositionInside(new Vector2(899.999f, 649.999f), pixelRect), Is.True);
            Assert.That(CameraFramingMath.IsScreenPositionInside(new Vector2(99.999f, 300f), pixelRect), Is.False);
            Assert.That(CameraFramingMath.IsScreenPositionInside(new Vector2(900f, 300f), pixelRect), Is.False);
            Assert.That(CameraFramingMath.IsScreenPositionInside(new Vector2(500f, 650f), pixelRect), Is.False);
        }

        [Test]
        public void SelectIndex_IsStableForSeedRoundAndWithinRange()
        {
            int first = DefensePresetSelector.SelectIndex(1701, 3, 4);

            Assert.That(first, Is.InRange(0, 3));
            Assert.That(DefensePresetSelector.SelectIndex(1701, 3, 4), Is.EqualTo(first));
        }

        [Test]
        public void SelectIndex_RejectsNonPositivePresetCounts()
        {
            Assert.That(
                () => DefensePresetSelector.SelectIndex(1701, 3, 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(DinoTargetProfileId.Velociraptor, DinoTargetCategory.House, DinoTargetCategory.Defender, DinoTargetCategory.Wall)]
        [TestCase(DinoTargetProfileId.Pachycephalosaurus, DinoTargetCategory.Defender, DinoTargetCategory.House, DinoTargetCategory.Wall)]
        [TestCase(DinoTargetProfileId.TRex, DinoTargetCategory.Wall, DinoTargetCategory.Defender, DinoTargetCategory.House)]
        public void DinoTargetPriorityProfile_UsesApprovedStrictCategoryOrder(
            DinoTargetProfileId profileId,
            DinoTargetCategory first,
            DinoTargetCategory second,
            DinoTargetCategory third)
        {
            Assert.That(
                DinoTargetPriorityProfile.GetCategories(profileId),
                Is.EqualTo(new[] { first, second, third }));
        }

        [Test]
        public void SelectWithoutReplacement_ReturnsThreeUniqueValidSlots()
        {
            int[] selected = DeterministicSlotSelector.SelectWithoutReplacement(6, 3, 1701u);

            Assert.That(selected, Has.Length.EqualTo(3));
            Assert.That(selected.Distinct().Count(), Is.EqualTo(3));
            Assert.That(selected, Has.All.InRange(0, 5));
        }

        [Test]
        public void SelectWithoutReplacement_IsReproducibleAndSeedSensitive()
        {
            int[] first = DeterministicSlotSelector.SelectWithoutReplacement(6, 3, 1701u);
            int[] repeated = DeterministicSlotSelector.SelectWithoutReplacement(6, 3, 1701u);
            int[] differentSeed = DeterministicSlotSelector.SelectWithoutReplacement(6, 3, 1702u);

            Assert.That(repeated, Is.EqualTo(first));
            Assert.That(differentSeed, Is.Not.EqualTo(first));
        }

        [TestCase(0, 1)]
        [TestCase(6, -1)]
        [TestCase(2, 3)]
        public void SelectWithoutReplacement_RejectsInvalidCounts(int count, int take)
        {
            Assert.That(
                () => DeterministicSlotSelector.SelectWithoutReplacement(count, take, 1u),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void GroundGuardSpawnPlanner_SelectsFiveAllowedSeparatedPointsDeterministically()
        {
            Rect bounds = new(-24f, -49f, 44f, 28f);
            static bool IsAllowed(Vector2 point) =>
                !(point.x > -6f && point.x < 4f && point.y > -39f && point.y < -31f);

            Assert.That(GroundGuardSpawnPlanner.TrySelect(
                bounds, 5, 6f, 20260829u, 256, IsAllowed, out Vector2[] first), Is.True);
            Assert.That(GroundGuardSpawnPlanner.TrySelect(
                bounds, 5, 6f, 20260829u, 256, IsAllowed, out Vector2[] repeated), Is.True);

            Assert.That(first, Has.Length.EqualTo(5));
            Assert.That(repeated, Is.EqualTo(first));
            Assert.That(first.All(IsAllowed), Is.True);
            for (int left = 0; left < first.Length; left++)
            for (int right = left + 1; right < first.Length; right++)
            {
                Assert.That(Vector2.Distance(first[left], first[right]), Is.GreaterThanOrEqualTo(6f));
            }
        }

        [Test]
        public void GroundGuardSpawnPlanner_FailsWithoutReturningPartialLayout()
        {
            bool selected = GroundGuardSpawnPlanner.TrySelect(
                new Rect(-1f, -1f, 2f, 2f),
                5,
                6f,
                7u,
                16,
                _ => true,
                out Vector2[] points);

            Assert.That(selected, Is.False);
            Assert.That(points, Is.Empty);
        }

        [Test]
        public void QuadrilateralTryEvaluate_MapsCornersEdgesAndAsymmetricInterior()
        {
            Assert.That(QuadrilateralXZ.TryCreate(
                new Vector2(-2f, -1f),
                new Vector2(3f, 0f),
                new Vector2(5f, 6f),
                new Vector2(-4f, 4f),
                out QuadrilateralXZ quad), Is.True);

            AssertVector2(Resolve(quad, new Vector2(0f, 0f)), new Vector2(-2f, -1f));
            AssertVector2(Resolve(quad, new Vector2(1f, 0f)), new Vector2(3f, 0f));
            AssertVector2(Resolve(quad, new Vector2(1f, 1f)), new Vector2(5f, 6f));
            AssertVector2(Resolve(quad, new Vector2(0f, 1f)), new Vector2(-4f, 4f));
            AssertVector2(Resolve(quad, new Vector2(0.5f, 0f)), new Vector2(0.5f, -0.5f));

            Assert.That(quad.TryEvaluate(new Vector2(0.25f, 0.75f), out Vector2 point), Is.True);
            AssertVector2(point, new Vector2(-1.5f, 3.1875f));
        }

        [TestCase(-0.001f, 0.5f)]
        [TestCase(1.001f, 0.5f)]
        [TestCase(0.5f, -0.001f)]
        [TestCase(0.5f, 1.001f)]
        public void QuadrilateralTryEvaluate_RejectsOutOfRangeCoordinates(float u, float v)
        {
            Assert.That(QuadrilateralXZ.TryCreate(
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
                out QuadrilateralXZ quad), Is.True);

            Assert.That(quad.TryEvaluate(new Vector2(u, v), out _), Is.False);
        }

        [Test]
        public void QuadrilateralTryEvaluate_RejectsNonFiniteCoordinates()
        {
            Assert.That(QuadrilateralXZ.TryCreate(
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
                out QuadrilateralXZ quad), Is.True);

            Assert.That(quad.TryEvaluate(new Vector2(float.NaN, 0.5f), out _), Is.False);
            Assert.That(quad.TryEvaluate(new Vector2(0.5f, float.PositiveInfinity), out _), Is.False);
        }

        [Test]
        public void QuadrilateralTryCreate_RejectsMalformedGeometry()
        {
            Assert.That(QuadrilateralXZ.TryCreate(
                new Vector2(float.NaN, 0f), Vector2.right, Vector2.one, Vector2.up, out _), Is.False);
            Assert.That(QuadrilateralXZ.TryCreate(
                Vector2.zero, Vector2.zero, Vector2.one, Vector2.up, out _), Is.False);
            Assert.That(QuadrilateralXZ.TryCreate(
                Vector2.zero, Vector2.right, new Vector2(2f, 0f), new Vector2(3f, 0f), out _), Is.False);
            Assert.That(QuadrilateralXZ.TryCreate(
                Vector2.zero, new Vector2(2f, 0f), new Vector2(1f, 0.5f), new Vector2(0f, 2f), out _), Is.False);
            Assert.That(QuadrilateralXZ.TryCreate(
                Vector2.zero, Vector2.one, Vector2.right, Vector2.up, out _), Is.False);
        }

        [Test]
        public void QuadrilateralContains_IncludesBoundaryAndRejectsExterior()
        {
            Assert.That(QuadrilateralXZ.TryCreate(
                new Vector2(-2f, -1f), new Vector2(3f, 0f), new Vector2(5f, 6f), new Vector2(-4f, 4f),
                out QuadrilateralXZ quad), Is.True);

            Assert.That(quad.Contains(new Vector2(0f, 2f)), Is.True);
            Assert.That(quad.Contains(new Vector2(0.5f, -0.5f)), Is.True);
            Assert.That(quad.Contains(new Vector2(-2f, -1f)), Is.True);
            Assert.That(quad.Contains(new Vector2(5.1f, 6f)), Is.False);
            Assert.That(quad.Contains(new Vector2(float.NaN, 0f)), Is.False);
        }

        [Test]
        public void QuadrilateralDistanceTo_HandlesInteriorEdgesCornersAndNonFinitePoints()
        {
            Assert.That(QuadrilateralXZ.TryCreate(
                Vector2.zero, new Vector2(2f, 0f), new Vector2(2f, 2f), new Vector2(0f, 2f),
                out QuadrilateralXZ quad), Is.True);

            Assert.That(quad.DistanceTo(Vector2.one), Is.Zero);
            Assert.That(quad.DistanceTo(new Vector2(1f, 0f)), Is.Zero);
            Assert.That(quad.DistanceTo(new Vector2(3.5f, 1f)), Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(quad.DistanceTo(new Vector2(3f, 3f)), Is.EqualTo(Mathf.Sqrt(2f)).Within(0.0001f));
            Assert.That(quad.DistanceTo(new Vector2(float.NaN, 0f)), Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void QuadrilateralOverlaps_DistinguishesAreaOverlapFromTouchingAndSeparation()
        {
            Assert.That(QuadrilateralXZ.TryCreate(
                Vector2.zero, new Vector2(2f, 0f), new Vector2(2f, 2f), new Vector2(0f, 2f),
                out QuadrilateralXZ subject), Is.True);
            Assert.That(QuadrilateralXZ.TryCreate(
                new Vector2(1f, 1f), new Vector2(3f, 1f), new Vector2(3f, 3f), new Vector2(1f, 3f),
                out QuadrilateralXZ overlapping), Is.True);
            Assert.That(QuadrilateralXZ.TryCreate(
                new Vector2(2f, 0f), new Vector2(4f, 0f), new Vector2(4f, 2f), new Vector2(2f, 2f),
                out QuadrilateralXZ touching), Is.True);
            Assert.That(QuadrilateralXZ.TryCreate(
                new Vector2(3f, 0f), new Vector2(5f, 0f), new Vector2(5f, 2f), new Vector2(3f, 2f),
                out QuadrilateralXZ separated), Is.True);

            Assert.That(subject.Overlaps(overlapping), Is.True);
            Assert.That(subject.Overlaps(touching), Is.False);
            Assert.That(subject.Overlaps(separated), Is.False);
        }

        [Test]
        public void QuadrilateralMeshFactory_GroundAndOutlineUseTheSameOrderedCorners()
        {
            QuadrilateralXZ quad = CreateMeshTestQuadrilateral();
            Mesh ground = null;
            Mesh outline = null;
            try
            {
                ground = QuadrilateralMeshFactory.CreateGroundMesh(quad, 0.25f, "Ground");
                outline = QuadrilateralMeshFactory.CreateOutlineMesh(quad, 0.3f, "Outline");

                Assert.That(ground.vertices, Is.EqualTo(new[]
                {
                    new Vector3(0f, 0.25f, 0f),
                    new Vector3(2f, 0.25f, 0f),
                    new Vector3(3f, 0.25f, 2f),
                    new Vector3(-1f, 0.25f, 2f)
                }));
                Assert.That(ground.triangles, Is.EqualTo(new[] { 0, 2, 1, 0, 3, 2 }));
                Assert.That(outline.vertices, Is.EqualTo(new[]
                {
                    new Vector3(0f, 0.3f, 0f),
                    new Vector3(2f, 0.3f, 0f),
                    new Vector3(3f, 0.3f, 2f),
                    new Vector3(-1f, 0.3f, 2f)
                }));
                Assert.That(outline.GetTopology(0), Is.EqualTo(MeshTopology.Lines));
                Assert.That(outline.GetIndices(0), Is.EqualTo(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ground);
                UnityEngine.Object.DestroyImmediate(outline);
            }
        }

        [Test]
        public void QuadrilateralMeshFactory_TriggerPrismIsClosedAndConvexColliderCompatible()
        {
            QuadrilateralXZ quad = CreateMeshTestQuadrilateral();
            Mesh prism = null;
            GameObject owner = null;
            try
            {
                prism = QuadrilateralMeshFactory.CreateTriggerPrism(quad, -0.5f, 1.5f, "Trigger");

                Assert.That(prism.vertexCount, Is.EqualTo(8));
                Assert.That(prism.triangles.Length, Is.EqualTo(36));
                Assert.That(prism.bounds.min.y, Is.EqualTo(-0.5f).Within(0.0001f));
                Assert.That(prism.bounds.max.y, Is.EqualTo(1.5f).Within(0.0001f));

                owner = new GameObject("Prism Collider Test");
                MeshCollider collider = owner.AddComponent<MeshCollider>();
                collider.sharedMesh = prism;
                collider.convex = true;
                collider.isTrigger = true;

                Assert.That(collider.sharedMesh, Is.SameAs(prism));
                Assert.That(collider.convex, Is.True);
                Assert.That(collider.isTrigger, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(prism);
            }
        }

        [Test]
        public void QuadrilateralMeshFactory_SignatureIsStableAndDetectsVertexDrift()
        {
            QuadrilateralXZ quad = CreateMeshTestQuadrilateral();
            Mesh first = null;
            Mesh second = null;
            try
            {
                first = QuadrilateralMeshFactory.CreateGroundMesh(quad, 0.25f, "First");
                second = QuadrilateralMeshFactory.CreateGroundMesh(quad, 0.25f, "Second");

                string firstSignature = QuadrilateralMeshFactory.ComputeSignature(first);
                Assert.That(QuadrilateralMeshFactory.ComputeSignature(second), Is.EqualTo(firstSignature));

                Vector3[] driftedVertices = second.vertices;
                driftedVertices[2] += new Vector3(0.01f, 0f, 0f);
                second.vertices = driftedVertices;
                Assert.That(QuadrilateralMeshFactory.ComputeSignature(second), Is.Not.EqualTo(firstSignature));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        private static void AssertVector2(Vector2 actual, Vector2 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
        }

        private static Vector2 Resolve(QuadrilateralXZ quad, Vector2 uv)
        {
            Assert.That(quad.TryEvaluate(uv, out Vector2 point), Is.True);
            return point;
        }

        private static QuadrilateralXZ CreateMeshTestQuadrilateral()
        {
            Assert.That(QuadrilateralXZ.TryCreate(
                Vector2.zero,
                new Vector2(2f, 0f),
                new Vector2(3f, 2f),
                new Vector2(-1f, 2f),
                out QuadrilateralXZ quad), Is.True);
            return quad;
        }
    }
}
