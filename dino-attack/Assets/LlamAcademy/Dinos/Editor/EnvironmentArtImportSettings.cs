using System;
using System.IO;
using UnityEditor;

namespace LlamAcademy.Dinos.Editor
{
    internal sealed class EnvironmentArtImportSettings : AssetPostprocessor
    {
        private const string PolyHavenRoot = "Assets/ThirdParty/PolyHaven/EnvironmentGround/";
        private const string CaveRoot = "Assets/ThirdParty/Kenney/ModularCaveKit/";
        private const string CaveColormap = CaveRoot + "Textures/colormap.png";
        private const float CaveModelScale = 1f;

        private void OnPreprocessTexture()
        {
            if (!IsUnderGovernedRoot(assetPath))
            {
                return;
            }

            TextureImporter importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture =
                assetPath.IndexOf("_diff_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(assetPath, CaveColormap, StringComparison.Ordinal);

            if (assetPath.IndexOf("_nor_gl_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
            }
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(CaveRoot, StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(assetPath), ".fbx", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ModelImporter importer = (ModelImporter)assetImporter;
            importer.globalScale = CaveModelScale;
            importer.importAnimation = false;
            importer.isReadable = false;
            importer.importCameras = false;
            importer.importLights = false;
        }

        private static bool IsUnderGovernedRoot(string path)
        {
            return path.StartsWith(PolyHavenRoot, StringComparison.Ordinal) ||
                path.StartsWith(CaveRoot, StringComparison.Ordinal);
        }
    }
}
