using System;
using System.Linq;
using System.Reflection;
using LlamAcademy.Dinos.Map;
using NUnit.Framework;
using UnityEngine;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class LayeredBattlefieldLayoutControllerTests
    {
        private GameObject Root;
        private Component Controller;
        private LayeredBattlefieldLayoutConfig Config;

        [SetUp]
        public void SetUp()
        {
            Config = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            Root = new GameObject("LayeredControllerTest");
            Root.SetActive(false);
            Controller = Root.AddComponent(RuntimeType("LlamAcademy.Dinos.Map.Adapters.LayeredBattlefieldLayoutController"));
            SetField(Controller, "ZoneBindings", CreateZoneBindings());
            SetField(Controller, "HouseBindings", CreateCandidateBindings(Config.HouseCandidates.Select(value => (value.StableId, value.Position))));
            SetField(Controller, "WallBindings", CreateCandidateBindings(Config.WallCandidates.Select(value => (value.StableId, value.Position))));
            SetField(Controller, "GroundGuardBindings", CreateCandidateBindings(Config.GroundGuardCandidates.Select(value => (value.StableId, value.Position))));
            SetField(Controller, "TargetBindings", CreateCandidateBindings(Config.TargetCandidates.Select(value => (value.StableId, value.Position))));
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(Root);

        [Test]
        public void TryApply_SucceedsExactlyOnceWithoutSecondMutation()
        {
            Assert.That(TryApply(31, out string firstError), Is.True, firstError);
            var provider = (IBattlefieldLayoutSignatureProvider)Controller;
            Assert.That(provider.TryCapture(out BattlefieldLayoutSignature signature), Is.True);
            string before = signature.CanonicalText;

            Assert.That(TryApply(32, out string secondError), Is.False);
            Assert.That(secondError, Does.Contain("already"));
            Assert.That(provider.TryCapture(out BattlefieldLayoutSignature after), Is.True);
            Assert.That(after.CanonicalText, Is.EqualTo(before));
        }

        [Test]
        public void TryApply_MovesSelectedPeripheralHousesAndCapturesDerivedHouseGuards()
        {
            Assert.That(TryApply(83, out string error), Is.True, error);
            LayeredBattlefieldLayoutSelection selection = (LayeredBattlefieldLayoutSelection)Controller.GetType()
                .GetProperty("AppliedSelection")
                .GetValue(Controller);
            Array bindings = (Array)GetField(Controller, "HouseBindings");

            foreach (SelectedHousePlacement house in selection.Houses)
            {
                object binding = bindings.Cast<object>().Single(value =>
                    (string)value.GetType().GetField("StableId").GetValue(value) == house.StableId);
                GameObject candidate = (GameObject)binding.GetType().GetField("Candidate").GetValue(binding);
                Assert.That(new Vector2(candidate.transform.position.x, candidate.transform.position.z),
                    Is.EqualTo(house.Position), house.StableId);
            }

            var provider = (IBattlefieldLayoutSignatureProvider)Controller;
            Assert.That(provider.TryCapture(out BattlefieldLayoutSignature signature), Is.True);
            Assert.That(signature.Guards.Count(value => value.StableId.StartsWith("house_guard_", StringComparison.Ordinal)),
                Is.EqualTo(6));
        }

        [Test]
        public void TryApply_InvalidPresenterFailsBeforeMutatingLayoutAndCanRetry()
        {
            Array houseBindings = (Array)GetField(Controller, "HouseBindings");
            Vector3[] beforePositions = houseBindings.Cast<object>()
                .Select(BoundCandidate)
                .Select(value => value.transform.position)
                .ToArray();
            bool[] beforeActive = houseBindings.Cast<object>()
                .Select(BoundCandidate)
                .Select(value => value.activeSelf)
                .ToArray();
            SetField(Controller, "ZonePresenter", Root.AddComponent<DinoDeploymentZonePresenter>());

            Assert.That(TryApply(91, out string error), Is.False);
            Assert.That(error, Does.Contain("presentation"));
            Assert.That(houseBindings.Cast<object>().Select(BoundCandidate).Select(value => value.transform.position),
                Is.EqualTo(beforePositions));
            Assert.That(houseBindings.Cast<object>().Select(BoundCandidate).Select(value => value.activeSelf),
                Is.EqualTo(beforeActive));

            SetField(Controller, "ZonePresenter", null);
            Assert.That(TryApply(91, out string retryError), Is.True, retryError);
        }

        [Test]
        public void TryApply_InvalidStableIdFailsClosedAndCanRetryAfterRepair()
        {
            Array bindings = (Array)GetField(Controller, "HouseBindings");
            object first = bindings.GetValue(0);
            FieldInfo id = first.GetType().GetField("StableId");
            string approvedId = (string)id.GetValue(first);
            id.SetValue(first, "unknown_house");

            Assert.That(TryApply(7, out string error), Is.False);
            Assert.That(error, Does.Contain("unknown"));
            id.SetValue(first, approvedId);
            Assert.That(TryApply(7, out string retryError), Is.True, retryError);
        }

        [Test]
        public void TryApply_DuplicateZoneOrMismatchedGeometryFailsClosed()
        {
            Array bindings = (Array)GetField(Controller, "ZoneBindings");
            object first = bindings.GetValue(0);
            FieldInfo id = first.GetType().GetField("ZoneId");
            object approvedId = id.GetValue(first);
            id.SetValue(first, DeploymentZoneId.Z2OpenMeadow);
            Assert.That(TryApply(1, out string duplicateError), Is.False);
            Assert.That(duplicateError, Does.Contain("duplicate").Or.Contain("mismatched"));

            id.SetValue(first, approvedId);
            DinoDeploymentZone zone = (DinoDeploymentZone)first.GetType().GetField("Zone").GetValue(first);
            BattlefieldZoneDefinition expected = Config.ZonePresets[0].Zones[0];
            zone.ConfigureGeometry(expected.Geometry.A + Vector2.right, expected.Geometry.B, expected.Geometry.C, expected.Geometry.D);
            Assert.That(TryApply(1, out string geometryError), Is.False);
            Assert.That(geometryError, Does.Contain("geometry"));
        }

        private Array CreateZoneBindings()
        {
            Type type = RuntimeType("LlamAcademy.Dinos.Map.Adapters.LayeredBattlefieldZoneBinding");
            Array result = Array.CreateInstance(type, 15);
            int index = 0;
            foreach (BattlefieldZonePreset preset in Config.ZonePresets)
            foreach (BattlefieldZoneDefinition definition in preset.Zones)
            {
                object binding = Activator.CreateInstance(type);
                GameObject gameObject = new($"zone_{preset.StableIndex}_{definition.ZoneId}");
                gameObject.transform.SetParent(Root.transform);
                DinoDeploymentZone zone = gameObject.AddComponent<DinoDeploymentZone>();
                typeof(DinoDeploymentZone).GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(zone, definition.ZoneId);
                zone.ConfigureGeometry(definition.Geometry.A, definition.Geometry.B, definition.Geometry.C, definition.Geometry.D);
                type.GetField("PresetIndex").SetValue(binding, preset.StableIndex);
                type.GetField("ZoneId").SetValue(binding, definition.ZoneId);
                type.GetField("Zone").SetValue(binding, zone);
                result.SetValue(binding, index++);
            }
            return result;
        }

        private Array CreateCandidateBindings(System.Collections.Generic.IEnumerable<(string StableId, Vector2 Position)> values)
        {
            Type type = RuntimeType("LlamAcademy.Dinos.Map.Adapters.LayeredBattlefieldCandidateBinding");
            (string StableId, Vector2 Position)[] source = values.ToArray();
            Array result = Array.CreateInstance(type, source.Length);
            for (int index = 0; index < source.Length; index++)
            {
                object binding = Activator.CreateInstance(type);
                GameObject candidate = new GameObject(source[index].StableId);
                candidate.transform.SetParent(Root.transform);
                Vector2 position = source[index].Position;
                candidate.transform.position = new Vector3(position.x, 0f, position.y);
                type.GetField("StableId").SetValue(binding, source[index].StableId);
                type.GetField("Candidate").SetValue(binding, candidate);
                result.SetValue(binding, index);
            }
            return result;
        }

        private bool TryApply(int seed, out string error)
        {
            object[] arguments = { seed, null };
            bool result = (bool)Controller.GetType().GetMethod("TryApply").Invoke(Controller, arguments);
            error = (string)arguments[1];
            return result;
        }

        private static GameObject BoundCandidate(object binding) =>
            (GameObject)binding.GetType().GetField("Candidate").GetValue(binding);

        private static Type RuntimeType(string fullName) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, false)).First(type => type != null);
        private static object GetField(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        private static void SetField(object instance, string name, object value) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }
}
