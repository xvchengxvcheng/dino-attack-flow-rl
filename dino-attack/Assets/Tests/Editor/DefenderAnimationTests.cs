using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class DefenderAnimationTests
    {
        private GameObject Model;
        private Animator Animator;
        private Camera Camera;
        private readonly string Report = Path.GetFullPath("../reports/phase8/map4-animation-fix-20260911");

        [SetUp]
        public void LoadActualCharacter()
        {
            var scene = EditorSceneManager.OpenScene("Assets/LlamAcademy/Dinos/Scenes/FirstPersonDefenseBattlefield.unity");
            Component player = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true))
                .Single(c => c != null && c.GetType().Name == "FirstPersonDefender");
            var source = (GameObject)new SerializedObject(player).FindProperty("CharacterModel").objectReferenceValue;
            Model = Object.Instantiate(source);
            Model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Model.SetActive(true);
            foreach (Transform t in Model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            Animator = Model.GetComponentInChildren<Animator>();
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Animator.Rebind();
            Animator.Update(0f);
            Camera = new GameObject("Animation test camera", typeof(Camera)).GetComponent<Camera>();
            Camera.cullingMask = 1 << 31;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.16f, 0.2f, 0.25f);
            Camera.fieldOfView = 35;
            Camera.transform.position = new Vector3(4f, 2.2f, 6.5f);
            Camera.transform.LookAt(Vector3.up * 1.35f);
            Directory.CreateDirectory(Report);
        }

        [TearDown]
        public void Cleanup()
        {
            if (Camera != null) Object.DestroyImmediate(Camera.gameObject);
            if (Model != null) Object.DestroyImmediate(Model);
        }

        [UnityTest]
        public IEnumerator CaptureActualControllerCycles()
        {
            var data = new StringBuilder();
            foreach (AnimationClip clip in Animator.runtimeAnimatorController.animationClips)
                data.AppendLine($"{clip.name}: length={clip.length:F4}, averageSpeed={clip.averageSpeed}, looping={clip.isLooping}");
            foreach (string mode in new[] { "idle", "walk", "run", "sprint", "cast", "run-cast" })
            {
                Animator.Rebind(); Animator.Update(0f);
                Animator.SetFloat("Speed", mode == "walk" ? 0.4f : mode == "sprint" ? 11f / 7f : mode.Contains("run") ? 1f : 0f);
                for (int i = 0; i < 30; i++) Animator.Update(1f / 60f);
                Texture2D sheet = new(1440, 960, TextureFormat.RGB24, false);
                RenderTexture target = RenderTexture.GetTemporary(360, 480, 24);
                Camera.targetTexture = target;
                for (int frame = 0; frame < 108; frame++)
                {
                    if (mode.Contains("cast") && frame % 27 == 0) Animator.SetTrigger("Cast");
                    Animator.Update(1f / 60f);
                    yield return null;
                    if (frame % 13 != 0 || frame / 13 >= 8) continue;
                    int index = frame / 13;
                    Camera.Render();
                    var previous = RenderTexture.active; RenderTexture.active = target;
                    Texture2D sample = new(360, 480, TextureFormat.RGB24, false);
                    sample.ReadPixels(new Rect(0, 0, 360, 480), 0, 0); sample.Apply();
                    sheet.SetPixels(index % 4 * 360, (1 - index / 4) * 480, 360, 480, sample.GetPixels());
                    Object.DestroyImmediate(sample); RenderTexture.active = previous;
                    Vector3 local(Transform t) => t.position - Model.transform.position;
                    data.AppendLine($"{mode} {frame}: rightHand={local(Animator.GetBoneTransform(HumanBodyBones.RightHand))}, " +
                        $"leftFoot={local(Animator.GetBoneTransform(HumanBodyBones.LeftFoot))}, rightFoot={local(Animator.GetBoneTransform(HumanBodyBones.RightFoot))}, " +
                        $"castState={Animator.GetCurrentAnimatorStateInfo(1).shortNameHash} phase={Animator.GetCurrentAnimatorStateInfo(1).normalizedTime:F3}");
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(Report, mode + "-cycle.png"), sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet); Camera.targetTexture = null; RenderTexture.ReleaseTemporary(target);
            }
            File.WriteAllText(Path.Combine(Report, "animation-samples.txt"), data.ToString());
        }

        [Test]
        public void LocomotionStrideMatchesControllerSpeed()
        {
            Animator.applyRootMotion = true;
            foreach (float speed in new[] { 2.8f, 7f, 11f })
            {
                Animator.Rebind(); Animator.Update(0f); Animator.SetFloat("Speed", speed / 7f);
                for (int i = 0; i < 60; i++) Animator.Update(1f / 60f);
                Vector3 displacement = Vector3.zero;
                for (int i = 0; i < 120; i++) { Animator.Update(1f / 60f); displacement += Animator.deltaPosition; }
                displacement.y = 0;
                Assert.That(displacement.magnitude / 2f, Is.EqualTo(speed).Within(speed * 0.08f),
                    $"Retargeted stride rate must match movement at {speed} units/s instead of sliding.");
            }
        }

        [Test]
        public void CastRecoversBeforeNextShotAndReturnsToRestPose()
        {
            for (int i = 0; i < 60; i++) Animator.Update(1f / 60f);
            Transform hand = Animator.GetBoneTransform(HumanBodyBones.RightHand);
            Quaternion rest = hand.rotation;
            Transform staff = Model.GetComponentsInChildren<Transform>().Single(t => t.name == "Arcane Staff");
            Assert.That(Vector3.Dot(staff.up, Vector3.up), Is.GreaterThan(0.8f), "Staff should be held upright, not across the body like a bow.");
            Animator.SetTrigger("Cast");
            for (int i = 0; i < 26; i++) Animator.Update(1f / 60f);
            Assert.That(Animator.GetCurrentAnimatorStateInfo(1).IsName("Cast") && !Animator.IsInTransition(1), Is.False,
                "A 0.45s staff shot must recover before the next shot, instead of restarting a long ritual.");
            for (int i = 0; i < 90; i++) Animator.Update(1f / 60f);
            Assert.That(Quaternion.Angle(rest, hand.rotation), Is.LessThan(12f), "The hand must return to its held-staff pose.");
        }
    }
}
