using System.IO;
using NUnit.Framework;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class LayeredBattlefieldSceneContractTests
    {
        private const string LayeredScenePath =
            "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity";
        private const string MapSelectScenePath =
            "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity";

        [Test]
        public void ApprovedTask4ScenesExistAtUniquePaths()
        {
            Assert.That(File.Exists(LayeredScenePath), Is.True,
                $"Task 4 must create {LayeredScenePath} without replacing Dinos.unity.");
            Assert.That(File.Exists(MapSelectScenePath), Is.True,
                $"Task 4 must create {MapSelectScenePath} without replacing Dinos.unity.");
            Assert.That(Path.GetFullPath(LayeredScenePath),
                Is.Not.EqualTo(Path.GetFullPath("Assets/LlamAcademy/Dinos/Scenes/Dinos.unity")));
            Assert.That(Path.GetFullPath(MapSelectScenePath), Is.Not.EqualTo(Path.GetFullPath(LayeredScenePath)));
        }
    }
}
