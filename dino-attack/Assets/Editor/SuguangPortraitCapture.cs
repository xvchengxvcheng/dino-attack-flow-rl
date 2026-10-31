using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Renders the actual companion model in an isolated preview scene.</summary>
public static class SuguangPortraitCapture
{
    public const string PortraitPath = "Assets/LlamAcademy/Dinos/UI/Textures/Suguang Portrait.png";

    [MenuItem("Tools/Dino Attack/Capture Suguang Portrait")]
    public static void Generate()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Capture the portrait in Edit Mode.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LlamAcademy/Dinos/Companion/Companion.prefab");
        Transform source = prefab != null ? prefab.transform.Find("Companion Shino Character") : null;
        if (source == null) throw new InvalidOperationException("The companion model is missing.");
        var scene = EditorSceneManager.NewPreviewScene();
        var target = RenderTexture.GetTemporary(768, 1024, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(768, 1024, TextureFormat.RGB24, false);
        try
        {
            var model = UnityEngine.Object.Instantiate(source.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(model, scene);
            model.SetActive(true);
            model.transform.position = Vector3.zero;
            model.transform.rotation = Quaternion.Euler(0f, -8f, 0f);
            var animator = model.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0f);
                var clip = animator.runtimeAnimatorController?.animationClips.FirstOrDefault();
                if (clip != null) clip.SampleAnimation(model, 1.2f);
                animator.enabled = false;
            }
            var cameraObject = new GameObject("Suguang Portrait Camera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.075f, 0.15f);
            camera.orthographic = true;
            camera.orthographicSize = 1.28f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.transform.position = new Vector3(0f, 1.18f, 5f);
            camera.transform.LookAt(new Vector3(0f, 1.18f, 0f));
            camera.targetTexture = target;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            // Built-in companion materials are unlit, so their authored colors are retained.
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, 768f, 1024f), 0, 0);
            image.Apply();
            File.WriteAllBytes(PortraitPath, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        AssetDatabase.ImportAsset(PortraitPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(PortraitPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }
}
