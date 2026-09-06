using System;
using System.Linq;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Enemy.Defense;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Unit;
using UnityEditor;
using UnityEngine;

namespace LlamAcademy.Dinos.Editor
{
    public static class DinoTargetProfileAssetSetup
    {
        private const string ProfilePropertyName = "<TargetPriorityProfile>k__BackingField";

        [MenuItem("Tools/Dino Attack/Apply Target Priority Profiles")]
        public static void Apply()
        {
            ApplyProfile(
                "Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor Attack Config.asset",
                0);
            ApplyProfile(
                "Assets/LlamAcademy/Dinos/Dinos/Pachycephalosaurus/Pachycephalosaur Attack Config.asset",
                1);
            ApplyProfile(
                "Assets/LlamAcademy/Dinos/Dinos/TRex/TRex Attack Config.asset",
                2);

            AssetDatabase.SaveAssets();
            Debug.Log("Applied target priority profiles to the three dinosaur attack configs.");
        }

        [MenuItem("Tools/Dino Attack/Run Combined Gameplay Smoke")]
        public static void RunCombinedGameplaySmoke()
        {
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException("Combined gameplay smoke requires Play Mode.");
            }

            SessionDefenseLayoutController layout = UnityEngine.Object.FindFirstObjectByType<SessionDefenseLayoutController>(FindObjectsInactive.Include);
            VillageHouse[] houses = UnityEngine.Object.FindObjectsByType<VillageHouse>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Wall[] walls = UnityEngine.Object.FindObjectsByType<Wall>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(wall => wall.name.StartsWith("Session Wall", StringComparison.Ordinal)).ToArray();
            Defender[] guards = UnityEngine.Object.FindObjectsByType<Defender>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(guard => guard.name.StartsWith("Fixed Archer", StringComparison.Ordinal)).ToArray();

            if (layout == null
                || layout.ActiveWallSlotIndices.Count != 3
                || layout.ActiveWallSlotIndices.Distinct().Count() != 3
                || walls.Length != 3
                || guards.Length != 3
                || houses.Length != 8
                || GameSessionRestartService.Instance == null
                || RoundManager.Instance == null
                || DinoSpawner.Instance == null)
            {
                throw new InvalidOperationException(
                    $"Runtime wiring mismatch: layout={layout != null}, indices={layout?.ActiveWallSlotIndices.Count ?? 0}, " +
                    $"walls={walls.Length}, guards={guards.Length}, houses={houses.Length}.");
            }

            if (walls.Any(wall => wall.Health != wall.MaxHealth || wall.Health <= 0)
                || guards.Any(guard => guard.Health != guard.MaxHealth || guard.Health <= 0))
            {
                throw new InvalidOperationException("Session walls or fixed archers did not start at full health.");
            }

            RoundManager.Instance.StartRound();
            int foodBeforeHouse = DinoSpawner.Instance.ResourcesToSpend;
            VillageHouse house = houses.OrderBy(candidate => candidate.StableHouseId).First();
            house.TakeDamage(int.MaxValue);
            int foodAfterFirstHit = DinoSpawner.Instance.ResourcesToSpend;
            house.TakeDamage(int.MaxValue);
            if (!house.IsDestroyed
                || house.IntactVisual.activeSelf
                || !house.DestroyedBeamRoot.activeSelf
                || house.DestroyedBeamRoot.transform.childCount is < 3 or > 5
                || !house.Occupancy.enabled
                || house.GetComponent<DinoTargetMetadata>().enabled
                || foodAfterFirstHit - foodBeforeHouse != HouseRewardLedger.MeatPerHouse
                || DinoSpawner.Instance.ResourcesToSpend != foodAfterFirstHit)
            {
                throw new InvalidOperationException("House death conversion or exactly-once meat reward failed.");
            }

            Wall destroyedWall = walls.OrderBy(wall => wall.name).First();
            WallGuardPost post = layout.GetComponentsInChildren<WallGuardPost>(true)
                .First(candidate => candidate.Wall == destroyedWall);
            destroyedWall.TakeDamage(int.MaxValue);
            if (destroyedWall.Health != 0 || post.Guard != null && post.Guard.Health > 0)
            {
                throw new InvalidOperationException("Wall death did not retire its fixed archer.");
            }

            Debug.Log(
                $"Combined gameplay smoke passed: seed={layout.StartSeed}; slots={string.Join(",", layout.ActiveWallSlotIndices)}; " +
                $"walls=3; guards=3; houses=8; houseReward=100; beams={house.DestroyedBeamRoot.transform.childCount}; " +
                "occupancy=retained; wallGuardRetired=true; reloadService=ready.");
        }

        private static void ApplyProfile(string assetPath, int enumValueIndex)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
            {
                throw new InvalidOperationException($"Missing attack config asset: {assetPath}");
            }

            SerializedObject serializedObject = new(asset);
            SerializedProperty profileProperty = serializedObject.FindProperty(ProfilePropertyName);
            if (profileProperty == null)
            {
                throw new InvalidOperationException(
                    $"Attack config at {assetPath} does not expose {ProfilePropertyName}.");
            }

            profileProperty.enumValueIndex = enumValueIndex;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }
    }
}
