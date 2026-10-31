using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LlamAcademy.Dinos.Companion.Editor
{
    /// <summary>
    /// Imports the Motifect humanoid clips and builds a controller dedicated to
    /// the companion. The player's existing controller remains unchanged.
    /// </summary>
    public static class CompanionMotifectAnimationBuilder
    {
        private const string MotionFolder = "Assets/ThirdParty/MotifectLocomotion/FBX";
        private const string ControllerPath = "Assets/LlamAcademy/Dinos/Companion/CompanionMotifect.controller";
        private const string StaffFolder = "Assets/ThirdParty/VRoid/SendagayaShino";

        [MenuItem("Dino Attack/Companion/Build Motifect Animation Controller")]
        public static void BuildMenu()
        {
            BuildController();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("COMPANION_MOTIFECT_CONTROLLER_OK");
        }

        public static RuntimeAnimatorController BuildController()
        {
            EnsureMotionFolder();
            Avatar sourceAvatar = PrepareSourceAvatar();
            if (sourceAvatar == null || !sourceAvatar.isValid || !sourceAvatar.isHuman)
                throw new InvalidOperationException("Motifect source avatar is not a valid Humanoid avatar.");

            AnimationClip idle = ImportMotion("idle_neutral", sourceAvatar, true);
            AnimationClip walk = ImportMotion("walk_forward", sourceAvatar, true);
            AnimationClip jog = ImportMotion("run_jog", sourceAvatar, true);
            AnimationClip sprint = ImportMotion("run_sprint", sourceAvatar, true);

            if (idle == null || walk == null || jog == null || sprint == null)
                throw new InvalidOperationException("One or more required Motifect FBX clips could not be imported.");

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            else
            {
                foreach (UnityEngine.Object subAsset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath)
                             .Where(asset => asset != controller).ToArray())
                {
                    AssetDatabase.RemoveObjectFromAsset(subAsset);
                }
                controller.layers = Array.Empty<AnimatorControllerLayer>();
                controller.parameters = Array.Empty<AnimatorControllerParameter>();
            }

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Cast", AnimatorControllerParameterType.Trigger);

            AnimatorControllerLayer locomotionLayer = new()
            {
                name = "Base Layer",
                defaultWeight = 1f,
                stateMachine = new AnimatorStateMachine { name = "Companion Locomotion" }
            };
            controller.AddLayer(locomotionLayer);
            AssetDatabase.AddObjectToAsset(locomotionLayer.stateMachine, controller);

            BlendTree tree = new()
            {
                name = "Motifect Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 0.42f);
            tree.AddChild(jog, 1f);
            tree.AddChild(sprint, 1.57f);
            AssetDatabase.AddObjectToAsset(tree, controller);

            AnimatorState locomotion = locomotionLayer.stateMachine.AddState("Locomotion");
            locomotion.motion = tree;
            locomotion.writeDefaultValues = true;
            locomotionLayer.stateMachine.defaultState = locomotion;

            AnimationClip staffHold = AssetDatabase.LoadAssetAtPath<AnimationClip>(StaffFolder + "/ShinoStaffHold.anim");
            AnimationClip staffCast = AssetDatabase.LoadAssetAtPath<AnimationClip>(StaffFolder + "/ShinoStaffCast.anim");
            if (staffHold != null && staffCast != null)
            {
                AnimatorControllerLayer castingLayer = BuildCastingLayer(controller, staffHold, staffCast);
                controller.AddLayer(castingLayer);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimatorControllerLayer BuildCastingLayer(
            AnimatorController controller, AnimationClip staffHold, AnimationClip staffCast)
        {
            AvatarMask mask = new() { name = "Companion Casting Mask" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,
                    i == (int)AvatarMaskBodyPart.RightArm || i == (int)AvatarMaskBodyPart.RightFingers);
            }
            AssetDatabase.AddObjectToAsset(mask, controller);

            AnimatorStateMachine machine = new() { name = "Casting" };
            AssetDatabase.AddObjectToAsset(machine, controller);
            AnimatorState rest = machine.AddState("Empty");
            rest.motion = staffHold;
            rest.writeDefaultValues = false;
            machine.defaultState = rest;
            AnimatorState cast = machine.AddState("Cast");
            cast.motion = staffCast;
            cast.writeDefaultValues = false;

            AnimatorStateTransition enter = machine.AddAnyStateTransition(cast);
            enter.hasExitTime = false;
            enter.duration = 0.035f;
            enter.AddCondition(AnimatorConditionMode.If, 0f, "Cast");
            AnimatorStateTransition exit = cast.AddTransition(rest);
            exit.hasExitTime = true;
            exit.exitTime = 0.85f;
            exit.duration = 0.045f;

            return new AnimatorControllerLayer
            {
                name = "Casting",
                defaultWeight = 1f,
                avatarMask = mask,
                stateMachine = machine
            };
        }

        private static Avatar PrepareSourceAvatar()
        {
            const string path = MotionFolder + "/idle_neutral.fbx";
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                throw new FileNotFoundException("Motifect source FBX is missing.", path);

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            ConfigureClipAnimations(importer, true);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Avatar>()
                .FirstOrDefault(avatar => avatar.isValid && avatar.isHuman);
        }

        private static AnimationClip ImportMotion(string name, Avatar sourceAvatar, bool loop)
        {
            string path = MotionFolder + "/" + name + ".fbx";
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new FileNotFoundException("Motifect motion FBX is missing.", path);

            // The source clip was already imported with the Humanoid avatar above.
            // Reimporting it with sourceAvatar would make Unity retain a stale
            // importer reference and produces a needless warning.
            if (string.Equals(name, "idle_neutral", StringComparison.Ordinal))
            {
                return AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = sourceAvatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            ConfigureClipAnimations(importer, loop);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
        }

        private static void ConfigureClipAnimations(ModelImporter importer, bool loop)
        {
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalOrientation = false;
                clip.keepOriginalPositionY = false;
                clip.heightFromFeet = true;
            }
            importer.clipAnimations = clips;
        }

        private static void EnsureMotionFolder()
        {
            if (!AssetDatabase.IsValidFolder(MotionFolder))
                throw new DirectoryNotFoundException("Motifect FBX folder is missing: " + MotionFolder);
        }
    }
}
