using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LlamAcademy.Dinos.EnvironmentArt.Tests.Editor
{
    public sealed class EnvironmentArtAssetGovernanceTests
    {
        private const string PolyHavenRoot = "Assets/ThirdParty/PolyHaven";
        private const string CaveRoot = "Assets/ThirdParty/Kenney/ModularCaveKit";
        private const string ManifestPath = "Assets/ThirdParty/environment-art-import-manifest.json";

        private static readonly ExpectedEntry[] ExpectedEntries =
        {
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/Dirt/dirt_diff_1k.jpg", "6CE6CA7E28ED046F7F93FA60DCB29F8CF42CD5A539ADC5676675ACCCFA34EFD3", "diffuse", "https://polyhaven.com/a/dirt"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/Dirt/dirt_nor_gl_1k.exr", "99417D12A135BCFE2F55B427E1F6D344D697FBA039517CA9A246E589538CD5A7", "normal", "https://polyhaven.com/a/dirt"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/Dirt/dirt_rough_1k.exr", "70553C97D17AB33E3AB60D28887EBEAFAE282C798632051F830EB107FED393B3", "roughness", "https://polyhaven.com/a/dirt"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/Sand03/sand_03_diff_1k.jpg", "E72144F9D7B81BDB7BBDB34222B53431738DCEF79346DD89412D2ABD541E4D30", "diffuse", "https://polyhaven.com/a/sand_03"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/Sand03/sand_03_nor_gl_1k.exr", "0C626210023806F83B2DB15ECD20C58AC1F26B3DCF82CFDC6A1CCEE0D1B10B90", "normal", "https://polyhaven.com/a/sand_03"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/Sand03/sand_03_rough_1k.exr", "5BBB3EABB0557FE5D682C1FC5B435B47756D47E601258EE0FBA589D13C4CCC4A", "roughness", "https://polyhaven.com/a/sand_03"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/BrownMud03/brown_mud_03_diff_1k.jpg", "6DA4FBD6FC5D21044DCBDBD96E098FB464ED544B42C982E72CE8CCD1D53686C5", "diffuse", "https://polyhaven.com/a/brown_mud_03"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/BrownMud03/brown_mud_03_nor_gl_1k.exr", "ABC909FC472448972046682BF02DA06D30125D2CA5299B2FCEA1DDCDE04BD7E0", "normal", "https://polyhaven.com/a/brown_mud_03"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/BrownMud03/brown_mud_03_spec_1k.png", "E569CF5420DCF6593500749D7735C7A65ED25E58C54F9AAEC225A2353C923979", "specular", "https://polyhaven.com/a/brown_mud_03"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/ForestGround01/forrest_ground_01_diff_1k.jpg", "3DD6875CB3908E022A3C45EBBFFA5E84C670FF2691FBBB6DC9EA4BFF88523800", "diffuse", "https://polyhaven.com/a/forrest_ground_01"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/ForestGround01/forrest_ground_01_nor_gl_1k.exr", "45094E02CC685C1B97CF615061685781ABDBDE133076F6B144078550CFED7F8D", "normal", "https://polyhaven.com/a/forrest_ground_01"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/ForestGround01/forrest_ground_01_rough_1k.jpg", "30D8B56A03D7B12DA16F58011B675662A40E58E1CDC768119A5F03968140058C", "roughness", "https://polyhaven.com/a/forrest_ground_01"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/MarbleRock03/marble_rock_03_diff_1k.jpg", "9758AB77F6279D1EB5CDB85F37485962A2830EDFF69AD47F5BA37582FD5018FD", "diffuse", "https://polyhaven.com/a/marble_rock_03"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/MarbleRock03/marble_rock_03_nor_gl_1k.exr", "3F2A00B1E969EFD94AFCA50EBFD7057CD5638DB7A3201A31A5DC3388FCD5FBB8", "normal", "https://polyhaven.com/a/marble_rock_03"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/EnvironmentGround/MarbleRock03/marble_rock_03_rough_1k.exr", "B4E8D9E551603AE6E02DD524BA45916ECC47D974A6E8AA73866B898D28098C51", "roughness", "https://polyhaven.com/a/marble_rock_03"),
            new ExpectedEntry("Assets/ThirdParty/PolyHaven/License.txt", "A2010F343487D3F7618AFFE54F789F5487602331C0A8D03F49E9A7C547CF0499", "license", "https://creativecommons.org/publicdomain/zero/1.0/legalcode.txt"),
            new ExpectedEntry("Assets/ThirdParty/Kenney/ModularCaveKit/Models/template-wall.fbx", "51D7571EFF95E4099124ADEFB095A18BA2FBE080BE67DD9D67E6D6DA23FEF671", "rim-model", "https://kenney.nl/assets/modular-cave-kit"),
            new ExpectedEntry("Assets/ThirdParty/Kenney/ModularCaveKit/Models/template-wall-half.fbx", "FD4B08BC44CE111C55D929477133D603EC988C7CACAAFD9C491702E4AA259C05", "rim-model", "https://kenney.nl/assets/modular-cave-kit"),
            new ExpectedEntry("Assets/ThirdParty/Kenney/ModularCaveKit/Models/template-wall-corner.fbx", "A1B21241886AE2EC2218CB5689FC5A684C2C06E67531F0F3EC13CDBF8CAB5B06", "rim-model", "https://kenney.nl/assets/modular-cave-kit"),
            new ExpectedEntry("Assets/ThirdParty/Kenney/ModularCaveKit/Models/template-wall-top.fbx", "5974EF5C9ABA80F170461544A6D22FFF72DEA10B799AE4EFE95B3D0507DE8D09", "rim-model", "https://kenney.nl/assets/modular-cave-kit"),
            new ExpectedEntry("Assets/ThirdParty/Kenney/ModularCaveKit/Models/template-wall-detail-a.fbx", "81BF7A65A421A9B3D9282E756DA14004102B1786CBA8519DCB6CC38108B20B7F", "rim-model", "https://kenney.nl/assets/modular-cave-kit"),
            new ExpectedEntry("Assets/ThirdParty/Kenney/ModularCaveKit/Models/template-corner.fbx", "D64E72A5A99470DA116BEABCC01E74294A082A07426D6304861CA89409AA8119", "rim-model", "https://kenney.nl/assets/modular-cave-kit"),
            new ExpectedEntry("Assets/ThirdParty/Kenney/ModularCaveKit/Models/gate-rock.fbx", "8FA32C95C9C10F87B6B359A10E868A4DB4D4E31C4D8CCDDDA0B7628040D7DF7B", "rim-model", "https://kenney.nl/assets/modular-cave-kit"),
            new ExpectedEntry("Assets/ThirdParty/Kenney/ModularCaveKit/Textures/colormap.png", "4A19ED0E790C888E0C8097BEF5178F9316A5CBC93B17FCA66C1A4660A85078B9", "colormap", "https://kenney.nl/assets/modular-cave-kit"),
            new ExpectedEntry("Assets/ThirdParty/Kenney/ModularCaveKit/License.txt", "0889F6CF5C972B42DE634C2F1F8BEC37D92E8D65ACF28830AFB159C04E3C6954", "license", "https://kenney.nl/assets/modular-cave-kit")
        };

        private static readonly IReadOnlyDictionary<string, int> ExpectedRoleCounts =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "colormap", 1 },
                { "diffuse", 5 },
                { "license", 2 },
                { "normal", 5 },
                { "rim-model", 7 },
                { "roughness", 4 },
                { "specular", 1 }
            };

        [Test]
        public void EnvironmentArtAssets_AreSelectiveLicensedAndImportedDeterministically()
        {
            Assert.That(Directory.Exists(PolyHavenRoot), Is.True, "The governed Poly Haven root is missing.");
            Assert.That(Directory.Exists(CaveRoot), Is.True, "The governed Cave Kit root is missing.");
            Assert.That(File.Exists(ManifestPath), Is.True, "The environment-art manifest is missing.");

            AssertForbiddenFilesAreAbsent(PolyHavenRoot);
            AssertForbiddenFilesAreAbsent(CaveRoot);

            EnvironmentArtManifest manifest =
                JsonUtility.FromJson<EnvironmentArtManifest>(File.ReadAllText(ManifestPath));
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.entries, Is.Not.Null);
            Assert.That(manifest.entries.Length, Is.EqualTo(ExpectedEntries.Length),
                "The manifest must exactly match the Task 1 approved allowlist.");
            Assert.That(manifest.entries.Select(entry => entry.destination).Distinct().Count(),
                Is.EqualTo(manifest.entries.Length), "Manifest destinations must be unique.");

            string[] actualDestinations = manifest.entries.Select(entry => entry.destination)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            string[] expectedDestinations = ExpectedEntries.Select(entry => entry.Destination)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            Assert.That(actualDestinations, Is.EqualTo(expectedDestinations),
                "Manifest destinations drifted from the Task 1 approved allowlist.");

            foreach (ExpectedEntry expected in ExpectedEntries)
            {
                EnvironmentArtManifestEntry entry = manifest.entries.Single(candidate =>
                    string.Equals(candidate.destination, expected.Destination, StringComparison.Ordinal));
                Assert.That(entry.sourceSha256, Is.EqualTo(expected.SourceSha256), expected.Destination);
                Assert.That(entry.role, Is.EqualTo(expected.Role), expected.Destination);
                Assert.That(entry.license, Is.EqualTo(expected.License), expected.Destination);
                Assert.That(entry.active, Is.EqualTo(expected.Active), expected.Destination);
                Assert.That(entry.sourceUrl, Is.EqualTo(expected.SourceUrl), expected.Destination);
                Assert.That(Uri.TryCreate(entry.sourceUrl, UriKind.Absolute, out Uri sourceUri) &&
                    sourceUri.Scheme == Uri.UriSchemeHttps, Is.True, expected.Destination);
                Assert.That(Path.IsPathRooted(entry.source), Is.True, expected.Destination);
                Assert.That(SourceHashMatches(entry), Is.True,
                    $"Source file is missing or its hash drifted: {entry.source}");
                Assert.That(File.Exists(entry.destination), Is.True, expected.Destination);
                Assert.That(ComputeSha256(entry.destination), Is.EqualTo(expected.SourceSha256),
                    $"Imported bytes drifted from the approved source: {expected.Destination}");
            }

            Dictionary<string, int> actualRoleCounts = manifest.entries
                .GroupBy(entry => entry.role, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            Assert.That(actualRoleCounts.Keys.OrderBy(role => role, StringComparer.Ordinal),
                Is.EqualTo(ExpectedRoleCounts.Keys.OrderBy(role => role, StringComparer.Ordinal)));
            foreach (KeyValuePair<string, int> expectedRoleCount in ExpectedRoleCounts)
            {
                Assert.That(actualRoleCounts[expectedRoleCount.Key], Is.EqualTo(expectedRoleCount.Value),
                    $"Unexpected count for role {expectedRoleCount.Key}.");
            }

            string[] governedFiles = Directory.GetFiles(PolyHavenRoot, "*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(CaveRoot, "*", SearchOption.AllDirectories))
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Select(NormalizePath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.That(governedFiles, Is.EqualTo(expectedDestinations),
                "Governed roots contain an unlisted or missing file.");

            EnvironmentArtManifestEntry[] models = manifest.entries
                .Where(entry => entry.role == "rim-model")
                .ToArray();
            Assert.That(models.Select(entry => Path.GetExtension(entry.destination)),
                Has.All.EqualTo(".fbx").IgnoreCase, "Only the approved FBX representation may be imported.");
            Assert.That(models.Select(entry => Path.GetFileNameWithoutExtension(entry.destination)).Distinct().Count(),
                Is.EqualTo(models.Length), "Duplicate model representations are forbidden.");

            foreach (EnvironmentArtManifestEntry entry in manifest.entries)
            {
                Assert.That(AssetDatabase.LoadMainAssetAtPath(entry.destination), Is.Not.Null,
                    $"AssetDatabase could not resolve {entry.destination}.");

                if (entry.role == "rim-model")
                {
                    AssertModelImporter(entry);
                }
                else if (IsTextureRole(entry.role))
                {
                    AssertTextureImporter(entry);
                }
            }
        }

        private static void AssertForbiddenFilesAreAbsent(string root)
        {
            Assert.That(Directory.GetFiles(root, "*.blend", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(root, "*.obj", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(root, "*.glb", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(root, "*.mtl", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(root, "*disp*", SearchOption.AllDirectories), Is.Empty);
        }

        private static void AssertModelImporter(EnvironmentArtManifestEntry entry)
        {
            ModelImporter importer = AssetImporter.GetAtPath(entry.destination) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.globalScale, Is.EqualTo(1f));
        }

        private static void AssertTextureImporter(EnvironmentArtManifestEntry entry)
        {
            TextureImporter importer = AssetImporter.GetAtPath(entry.destination) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.maxTextureSize, Is.EqualTo(1024));
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.CompressedHQ));

            switch (entry.role)
            {
                case "diffuse":
                case "colormap":
                    Assert.That(importer.sRGBTexture, Is.True);
                    Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default));
                    break;
                case "normal":
                    Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.NormalMap));
                    Assert.That(importer.sRGBTexture, Is.False);
                    break;
                case "roughness":
                case "specular":
                    Assert.That(importer.sRGBTexture, Is.False);
                    Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default));
                    break;
            }
        }

        private static bool IsTextureRole(string role)
        {
            return role == "diffuse" || role == "normal" || role == "roughness" ||
                role == "specular" || role == "colormap";
        }

        private static bool SourceHashMatches(EnvironmentArtManifestEntry entry)
        {
            return File.Exists(entry.source) &&
                string.Equals(ComputeSha256(entry.source), entry.sourceSha256, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private sealed class ExpectedEntry
        {
            public ExpectedEntry(string destination, string sourceSha256, string role, string sourceUrl)
            {
                Destination = destination;
                SourceSha256 = sourceSha256;
                Role = role;
                SourceUrl = sourceUrl;
            }

            public string Destination { get; }
            public string SourceSha256 { get; }
            public string Role { get; }
            public string SourceUrl { get; }
            public string License => "CC0-1.0";
            public bool Active => true;
        }

        [Serializable]
        private sealed class EnvironmentArtManifest
        {
            public EnvironmentArtManifestEntry[] entries;
        }

        [Serializable]
        private sealed class EnvironmentArtManifestEntry
        {
            public string source;
            public string sourceSha256;
            public string destination;
            public string sourceUrl;
            public string license;
            public string role;
            public bool active;
        }
    }
}
