using System;
using System.Linq;
using System.Reflection;
using LlamAcademy.Dinos.Training;
using NUnit.Framework;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DinoAttack.Training.Core.Tests
{
    public class DinoTrainingActionCodecTests
    {
        [Test]
        public void DecodeWaitKeepsFixedActionShape()
        {
            Assert.That(DinoTrainingActionCodec.TryDecode(
                new[] { -1f, 0.2f, 0.9f, -1f }, out DecodedDinoTrainingAction action),
                Is.True);

            Assert.That(action.IsWait, Is.True);
            Assert.That(action.ZoneIndex, Is.EqualTo(0));
            Assert.That(action.DinoIndex, Is.EqualTo(-1));
            Assert.That(action.RelativeUv, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void DecodePlacementUsesFrozenBinsAndUnitIntervalCoordinates()
        {
            Assert.That(DinoTrainingActionCodec.TryDecode(
                new[] { 0.2f, 0.6f, -0.2f, 1f }, out DecodedDinoTrainingAction action),
                Is.True);

            Assert.That(action.IsWait, Is.False);
            Assert.That(action.ZoneIndex, Is.EqualTo(3));
            Assert.That(action.DinoIndex, Is.EqualTo(2));
            Assert.That(action.RelativeUv, Is.EqualTo(new Vector2(0.8f, 0.4f)));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void DecodeRejectsNonFiniteActions(float invalid)
        {
            Assert.That(DinoTrainingActionCodec.TryDecode(
                new[] { 1f, 0f, invalid, 0f }, out _), Is.False);
        }

        [Test]
        public void DecodeRejectsWrongShape()
        {
            Assert.That(DinoTrainingActionCodec.TryDecode(new[] { 1f, 0f }, out _), Is.False);
        }

        [Test]
        public void ObservationLayoutRemainsStable()
        {
            Assert.That(DinoTrainingObservationLayout.ProtocolVersion,
                Is.EqualTo("dino_attack_structured_set_v1"));
            Assert.That(DinoTrainingObservationLayout.GlobalShape, Is.EqualTo(new[] { 3 }));
            Assert.That(DinoTrainingObservationLayout.WallShape, Is.EqualTo(new[] { 6, 5 }));
            Assert.That(DinoTrainingObservationLayout.GuardShape, Is.EqualTo(new[] { 8, 7 }));
            Assert.That(DinoTrainingObservationLayout.HouseShape, Is.EqualTo(new[] { 8, 6 }));
            Assert.That(DinoTrainingObservationLayout.DinoShape, Is.EqualTo(new[] { 10, 7 }));
            Assert.That(DinoTrainingObservationLayout.TotalScalarCount, Is.EqualTo(207));
        }

        [Test]
        public void ObservationLayoutShapesAreDefensiveCopies()
        {
            int[] mutated = DinoTrainingObservationLayout.WallShape;
            mutated[0] = int.MaxValue;

            Assert.That(DinoTrainingObservationLayout.WallShape, Is.EqualTo(new[] { 6, 5 }));
        }

        [TestCase(-1f, 0)]
        [TestCase(-0.6f, 1)]
        [TestCase(-0.2f, 2)]
        [TestCase(0.2f, 3)]
        [TestCase(0.6f, 4)]
        [TestCase(1f, 4)]
        public void RegionBinsUseFrozenUpperBoundaryRule(float value, int expected)
        {
            Assert.That(DinoTrainingActionCodec.TryDecode(
                new[] { value, 0f, 0f, 1f }, out DecodedDinoTrainingAction decoded), Is.True);
            Assert.That(decoded.ZoneIndex, Is.EqualTo(expected));
        }

        [Test]
        public void ZoneBinsKeepEveryRepresentableInternalBoundaryStable()
        {
            foreach ((float boundary, int lowerBin) in new[]
                     {
                         (-0.6f, 0),
                         (-0.2f, 1),
                         (0.2f, 2),
                         (0.6f, 3)
                     })
            {
                AssertZone(PreviousFloat(boundary), lowerBin);
                AssertZone(boundary, lowerBin + 1);
                AssertZone(NextFloat(boundary), lowerBin + 1);
            }
        }

        [Test]
        public void DinoChoiceBinsKeepEveryRepresentableInternalBoundaryStable()
        {
            foreach ((float boundary, int lowerBin) in new[]
                     {
                         (-0.5f, 0),
                         (0f, 1),
                         (0.5f, 2)
                     })
            {
                AssertChoice(PreviousFloat(boundary), lowerBin);
                AssertChoice(boundary, lowerBin + 1);
                AssertChoice(NextFloat(boundary), lowerBin + 1);
            }
        }

        private static void AssertZone(float value, int expected)
        {
            Assert.That(DinoTrainingActionCodec.TryDecode(
                new[] { value, 0f, 0f, 1f }, out DecodedDinoTrainingAction decoded), Is.True);
            Assert.That(decoded.ZoneIndex, Is.EqualTo(expected), $"zone input {value:R}");
        }

        private static void AssertChoice(float value, int expected)
        {
            Assert.That(DinoTrainingActionCodec.TryDecode(
                new[] { 0f, 0f, 0f, value }, out DecodedDinoTrainingAction decoded), Is.True,
                $"choice input {value:R}");
            Assert.That(decoded.IsWait, Is.EqualTo(expected == 0), $"choice input {value:R}");
            Assert.That(decoded.DinoIndex, Is.EqualTo(expected == 0 ? -1 : expected - 1), $"choice input {value:R}");
        }

        private static float PreviousFloat(float value)
        {
            if (value == 0f)
            {
                return -float.Epsilon;
            }

            int bits = BitConverter.SingleToInt32Bits(value);
            return BitConverter.Int32BitsToSingle(bits + (value > 0f ? -1 : 1));
        }

        private static float NextFloat(float value)
        {
            if (value == 0f)
            {
                return float.Epsilon;
            }

            int bits = BitConverter.SingleToInt32Bits(value);
            return BitConverter.Int32BitsToSingle(bits + (value > 0f ? 1 : -1));
        }

        [Test]
        public void TrainingPlayerBuildContainsOnlyTheIsolatedTrainingScene()
        {
            string[] scenes = DinoTrainingBuildLayout.CreateSceneList();

            Assert.That(scenes, Is.EqualTo(new[]
            {
                "Assets/LlamAcademy/Dinos/Scenes/DinoAttackTraining.unity"
            }));
        }

        [Test]
        public void TrainingPlayerPathIsOutsideTheUnityProject()
        {
            string projectRoot = Path.Combine("C:\\workspace", "dino-attack");
            string expected = Path.GetFullPath(Path.Combine(
                "C:\\workspace",
                "Builds",
                "DinoAttackTrainingOnly",
                "DinoAttackTraining.exe"));

            Assert.That(DinoTrainingBuildLayout.ResolvePlayerPath(projectRoot), Is.EqualTo(expected));
        }

        [Test]
        public void FormalLaunchOptionsRequireTheExactFlagAndParseOptionalIntegerArguments()
        {
            Type optionsType = RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingLaunchOptions");
            MethodInfo tryParse = optionsType.GetMethod("TryParse", BindingFlags.Static | BindingFlags.Public);
            Assert.That(tryParse, Is.Not.Null);

            object[] validInvocation =
            {
                new[]
                {
                    "game.exe",
                    "--dino-training",
                    "--dino-base-seed", "7",
                    "--dino-environment-index", "2"
                },
                null
            };
            Assert.That(tryParse.Invoke(null, validInvocation), Is.True);
            object options = validInvocation[1];
            Assert.That(optionsType.GetProperty("BaseSeed")?.GetValue(options), Is.EqualTo(7));
            Assert.That(optionsType.GetProperty("EnvironmentIndex")?.GetValue(options), Is.EqualTo(2));
            Assert.That(optionsType.GetProperty("ProcessGeneration")?.GetValue(options), Is.EqualTo(0));

            foreach (string[] invalid in new[]
                     {
                         new[] { "game.exe", "--dino-training", "--dino-training" },
                         new[] { "game.exe", "--dino-training", "--dino-base-seed", "7", "--dino-base-seed", "8" },
                         new[] { "game.exe", "--dino-training", "--dino-base-seed" },
                         new[] { "game.exe", "--dino-training", "--dino-environment-index", "two" },
                         new[] { "game.exe", "--dino-training", "--dino-process-generation", "3", "--dino-process-generation", "4" }
                     })
            {
                object[] invocation = { invalid, null };
                Assert.That(tryParse.Invoke(null, invocation), Is.False, string.Join(" ", invalid));
            }

            object[] inexactInvocation =
            {
                new[] { "game.exe", "--dino-training-extra", "--batchmode", "--nographics" },
                null
            };
            Assert.That(tryParse.Invoke(null, inexactInvocation), Is.False,
                "batch/no-graphics and an inexact flag must remain Human mode");
        }

        [Test]
        public void FormalPlayerBuildContainsMenuAndBothPlayableScenesAtTheTask8OutputPath()
        {
            Type layoutType = RuntimeType("LlamAcademy.Dinos.Training.DinoFormalTrainingBuildLayout");
            string[] scenes = (string[])layoutType.GetMethod("CreateSceneList", BindingFlags.Static | BindingFlags.Public)
                ?.Invoke(null, null);
            Assert.That(scenes, Is.EqualTo(new[]
            {
                "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity",
                "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity",
                "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity"
            }));

            string projectRoot = Path.Combine("C:\\workspace", "dino-attack");
            string expected = Path.GetFullPath(Path.Combine(
                "C:\\workspace",
                "reports",
                "phase8",
                "layered-battlefield",
                "build",
                "DinoAttackDualMapTask8.exe"));
            string actual = (string)layoutType.GetMethod("ResolvePlayerPath", BindingFlags.Static | BindingFlags.Public)
                ?.Invoke(null, new object[] { projectRoot });
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void FormalSceneStoresDinosInFrozenActionOrder()
        {
            EditorSceneManager.OpenScene(
                "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity",
                OpenSceneMode.Single);
            GameObject environment = GameObject.Find("Training/FormalTrainingEnvironment");
            Assert.That(environment, Is.Not.Null,
                "Dinos.unity must contain the installed formal training environment.");

            string[] expected = { "Velociraptor", "Pachycephalosaurus", "TRex" };
            AssertDinoOrder(environment, "LlamAcademy.Dinos.Training.DinoTrainingAgent", expected);
            AssertDinoOrder(environment, "LlamAcademy.Dinos.Training.DinoStructuredObservationBuilder", expected);
        }

        private static void AssertDinoOrder(GameObject environment, string componentTypeName, string[] expected)
        {
            Type componentType = RuntimeType(componentTypeName);
            Component component = environment.GetComponent(componentType);
            Assert.That(component, Is.Not.Null, $"Missing {componentTypeName} on the formal environment.");

            FieldInfo field = componentType.GetField("DinoTypes", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing frozen DinoTypes field on {componentTypeName}.");
            Array dinos = (Array)field.GetValue(component);
            string[] actual = dinos.Cast<UnityEngine.Object>().Select(dino => dino?.name).ToArray();
            Assert.That(actual, Is.EqualTo(expected), componentTypeName);
        }

        private static Type RuntimeType(string fullName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, $"Missing runtime type {fullName}.");
            return type;
        }
    }
}
