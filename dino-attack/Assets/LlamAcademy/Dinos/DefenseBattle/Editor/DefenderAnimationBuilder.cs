using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LlamAcademy.Dinos.DefenseBattle.Editor
{
    public static class DefenderAnimationBuilder
    {
        private const string Folder = "Assets/ThirdParty/VRoid/SendagayaShino";
        private const string MotionFolder = "Assets/ThirdParty/UnityFemaleMotion";

        // Only updates the player's animation assets and staff attachment. Map geometry is untouched.
        [MenuItem("Dino Attack/Maps/Update Map 4 Character Animation")]
        public static void UpdateCharacter()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play Mode and wait for compilation before updating animation.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scene changes before updating Map 4 animation.");
            var scene = EditorSceneManager.OpenScene(FirstPersonDefenseSceneBuilder.TargetScene);
            var controller = BuildController();
            var player = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FirstPersonDefender>(true)).Single();
            var data = new SerializedObject(player);
            var model = (GameObject)data.FindProperty("CharacterModel").objectReferenceValue;
            var animator = model.GetComponentInChildren<Animator>(true);
            animator.runtimeAnimatorController = controller;
            ConfigureStaff(animator, (Transform)data.FindProperty("Staff").objectReferenceValue);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("MAP4_ANIMATION_UPDATED");
        }

        public static void ConfigureStaff(Animator animator, Transform staff)
        {
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            // Sample a disposable copy, so calibration never saves evaluated bone poses to the scene.
            var preview = UnityEngine.Object.Instantiate(animator.gameObject);
            preview.SetActive(true);
            var previewAnimator = preview.GetComponent<Animator>();
            previewAnimator.Rebind(); previewAnimator.Update(0f);
            var previewHand = previewAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            Quaternion gripRotation = Quaternion.Inverse(previewHand.rotation) * preview.transform.rotation * Quaternion.Euler(8f, 0f, -12f);
            UnityEngine.Object.DestroyImmediate(preview);
            staff.SetParent(hand, false);
            float scale = hand.lossyScale.x;
            staff.localPosition = new Vector3(0.025f, 0.035f, 0f) / scale;
            staff.localRotation = gripRotation;
            staff.localScale = Vector3.one * (1.1f / scale);
        }

        public static RuntimeAnimatorController BuildController()
        {
            var rigImporter = (ModelImporter)AssetImporter.GetAtPath(MotionFolder + "/FemaleRig.fbx");
            rigImporter.animationType = ModelImporterAnimationType.Human;
            rigImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            rigImporter.materialImportMode = ModelImporterMaterialImportMode.None;
            rigImporter.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(MotionFolder + "/FemaleRig.fbx").OfType<Avatar>().Single();
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("Female motion source avatar is invalid.");
            AnimationClip Motion(string name)
            {
                string path = MotionFolder + "/Female" + name + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = true;
                var clips = importer.defaultClipAnimations;
                foreach (var clip in clips)
                {
                    clip.loopTime = true; clip.loopPose = true;
                    clip.lockRootRotation = true; clip.lockRootHeightY = true;
                    clip.keepOriginalOrientation = false; clip.keepOriginalPositionY = false;
                    clip.heightFromFeet = true;
                }
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
            }
            AnimationClip walk = Motion("Walk"), run = Motion("Run"), sprint = Motion("Sprint");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "/ShinoDefender.controller");
            if (controller == null) throw new InvalidOperationException("Map 4 controller must already exist.");
            AnimationClip idle = BuildIdle(), hold = BuildStaffPose(false), castClip = BuildStaffPose(true);
            var state = controller.layers[0].stateMachine.states.Single(s => s.state.name == "Locomotion").state;
            var tree = (BlendTree)state.motion;
            // CharacterController owns displacement. Match clip root velocity to the rendered avatar scale.
            // Companion prefabs are built before Map 4 is opened, so there may
            // be no FirstPersonDefender in the active editor scene. Use a
            // neutral scale in that authoring context; Map 4 still recalculates
            // the exact player scale when its scene is rebuilt.
            var player = UnityEngine.Object.FindFirstObjectByType<FirstPersonDefender>();
            var targetAnimator = player == null ? null : player.GetComponentInChildren<Animator>(true);
            float humanScale = targetAnimator == null
                ? 1f
                : targetAnimator.humanScale * Mathf.Abs(targetAnimator.transform.lossyScale.x);
            float Rate(AnimationClip clip, float speed) => speed / Mathf.Max(0.1f, clip.averageSpeed.magnitude * humanScale);
            if (targetAnimator != null)
            {
                tree.children = new[]
                {
                    new ChildMotion { motion = idle, threshold = 0f, timeScale = 1f },
                    new ChildMotion { motion = walk, threshold = 0.4f, timeScale = Rate(walk, 2.8f) },
                    new ChildMotion { motion = run, threshold = 1f, timeScale = Rate(run, 7f) },
                    new ChildMotion { motion = sprint, threshold = 11f / 7f, timeScale = Rate(sprint, 11f) }
                };
            }
            state.iKOnFeet = false;
            var layers = controller.layers;
            var mask = layers[1].avatarMask;
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,
                    i == (int)AvatarMaskBodyPart.RightArm || i == (int)AvatarMaskBodyPart.RightFingers);
            var machine = layers[1].stateMachine;
            var rest = machine.states.Single(s => s.state.name == "Empty").state;
            var cast = machine.states.Single(s => s.state.name == "Cast").state;
            rest.motion = hold; rest.writeDefaultValues = false;
            cast.motion = castClip; cast.speed = 1f; cast.writeDefaultValues = false;
            foreach (var transition in machine.anyStateTransitions)
            {
                transition.canTransitionToSelf = false;
                transition.hasFixedDuration = true; transition.duration = 0.035f;
            }
            foreach (var transition in cast.transitions)
            {
                transition.hasExitTime = true; transition.exitTime = 0.85f;
                transition.hasFixedDuration = true; transition.duration = 0.045f;
            }
            foreach (var asset in new UnityEngine.Object[] { tree, state, rest, cast, mask, controller }) EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            if (targetAnimator != null)
                Debug.Log($"MAP4_MOTION_RATES scale={humanScale} walk={Rate(walk, 2.8f)} run={Rate(run, 7f)} sprint={Rate(sprint, 11f)}");
            return controller;
        }

        private static AnimationClip BuildIdle()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/ShinoIdle.anim");
            foreach (string side in new[] { "Left", "Right" })
            {
                Set(clip, side + " Upper Leg In-Out", AnimationCurve.Constant(0, clip.length, 0));
                Set(clip, side + " Arm Down-Up", AnimationCurve.Constant(0, clip.length, -0.92f));
                Set(clip, side + " Arm Front-Back", AnimationCurve.Constant(0, clip.length, 0));
                Set(clip, side + " Forearm Stretch", AnimationCurve.Constant(0, clip.length, 0.85f));
                Set(clip, side + " Forearm Twist In-Out", AnimationCurve.Constant(0, clip.length, 0));
            }
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static AnimationClip BuildStaffPose(bool cast)
        {
            string path = Folder + (cast ? "/ShinoStaffCast.anim" : "/ShinoStaffHold.anim");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool created = clip == null;
            if (created) clip = new AnimationClip();
            clip.ClearCurves(); clip.name = cast ? "Staff pulse and recovery" : "Relaxed staff grip";
            clip.frameRate = 60f;
            float duration = cast ? 0.38f : 1f;
            foreach (string muscle in HumanTrait.MuscleName)
            {
                if (!(muscle.StartsWith("Right Arm") || muscle.StartsWith("Right Forearm") ||
                      muscle.StartsWith("Right Hand") || muscle.StartsWith("Right Shoulder") || muscle.StartsWith("RightHand."))) continue;
                float value = muscle switch
                {
                    "Right Arm Down-Up" => -0.7f,
                    "Right Arm Front-Back" => 0f,
                    "Right Arm Twist In-Out" => 0.4f,
                    "Right Forearm Stretch" => 0.1f,
                    _ => muscle.EndsWith("Stretched") ? -0.35f : 0f
                };
                AnimationCurve curve = AnimationCurve.Constant(0, duration, value);
                if (cast && (muscle == "Right Arm Down-Up" || muscle == "Right Forearm Stretch"))
                {
                    float extension = muscle == "Right Arm Down-Up" ? 0.12f : 0.28f;
                    curve = new AnimationCurve(new Keyframe(0, value), new Keyframe(0.07f, value + extension),
                        new Keyframe(0.15f, value + extension * 0.55f), new Keyframe(duration, value));
                    for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
                }
                Set(clip, muscle, curve);
            }
            // Finger curve binding names differ from HumanTrait's display names.
            foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                for (int joint = 1; joint <= 3; joint++)
                    Set(clip, $"RightHand.{finger}.{joint} Stretched", AnimationCurve.Constant(0, duration, finger == "Thumb" ? 0f : -0.55f));
                Set(clip, $"RightHand.{finger}.Spread", AnimationCurve.Constant(0, duration, 0f));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = !cast; settings.stopTime = duration;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created) AssetDatabase.CreateAsset(clip, path); else EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void Set(AnimationClip clip, string property, AnimationCurve curve) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), curve);
    }
}
