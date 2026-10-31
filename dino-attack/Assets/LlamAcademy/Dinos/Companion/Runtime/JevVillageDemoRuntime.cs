using System;
using System.Collections.Generic;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.DefenseBattle;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Simulation;
using LlamAcademy.Dinos.Unit;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Companion
{
    /// <summary>
    /// Runtime setup for the standalone JEV demonstration map. Dinosaurs are
    /// real Unit instances, but their graph and navigation are deliberately
    /// paused so they act as visible, non-aggressive conversation targets.
    /// Building guards are placed beside the village houses and are likewise
    /// held in an idle watch position.
    /// </summary>
    [DefaultExecutionOrder(45)]
    [DisallowMultipleComponent]
    public sealed class JevVillageDemoRuntime : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private FirstPersonDefender Player;
        [SerializeField] private DefenseBattleCoordinator Coordinator;
        [SerializeField] private CompanionController Companion;

        [Header("Random passive dinosaurs")]
        [SerializeField] private DinoSO[] PassiveDinoTypes = Array.Empty<DinoSO>();
        [SerializeField, Min(1)] private int PassiveDinoCount = 8;
        [SerializeField, Min(1f)] private float PassiveSpawnRadius = 26f;
        [SerializeField] private Vector3 SpawnCenter;

        [Header("Building guards")]
        [SerializeField] private GameObject[] GuardPrefabs = Array.Empty<GameObject>();
        [SerializeField, Min(1)] private int GuardCount = 4;
        [SerializeField, Min(1f)] private float GuardSpawnRadius = 13f;

        [Header("Presentation")]
        [SerializeField] private bool ShowOverlay = true;

        private readonly List<Dino> PassiveDinos = new();
        private readonly List<Defender> BuildingGuards = new();
        private GUIStyle OverlayBoxStyle;
        private GUIStyle OverlayLabelStyle;
        private int LayoutSeed;
        private bool Started;

        public IReadOnlyList<Dino> SpawnedPassiveDinos => PassiveDinos;
        public IReadOnlyList<Defender> SpawnedBuildingGuards => BuildingGuards;
        public int Seed => LayoutSeed;

        private void Start()
        {
            if (Started) return;
            Started = true;
            Player ??= FindFirstObjectByType<FirstPersonDefender>();
            Coordinator ??= FindFirstObjectByType<DefenseBattleCoordinator>();
            Companion ??= FindFirstObjectByType<CompanionController>();

            if (Player == null || Coordinator == null || Companion == null)
            {
                Debug.LogError("JEV village demo is missing its player, coordinator, or companion reference.", this);
                return;
            }

            HideNormalSetupPanel();
            LayoutSeed = GameSessionRestartService.Instance == null
                ? Environment.TickCount
                : GameSessionRestartService.Instance.CurrentStartSeed;
            System.Random random = new(LayoutSeed);
            Vector3 center = ResolveVillageCenter();
            SpawnPassiveDinos(random, center);
            SpawnBuildingGuards(random, center);

            Player.BeginDemoMode();
            Companion.BeginDemoBattle(Player, Coordinator, PassiveDinos);
        }

        private Vector3 ResolveVillageCenter()
        {
            RoundManager manager = RoundManager.Instance;
            if (manager != null)
            {
                if (manager.DinoTarget != null) return manager.DinoTarget.position;
                Vector3 sum = Vector3.zero;
                int count = 0;
                foreach (VillageHouse house in manager.SessionHouses)
                {
                    if (house == null) continue;
                    sum += house.transform.position;
                    count++;
                }
                if (count > 0) return sum / count;
            }
            if (Player != null) return Player.transform.position;
            return SpawnCenter;
        }

        private void SpawnPassiveDinos(System.Random random, Vector3 center)
        {
            PassiveDinos.Clear();
            if (PassiveDinoTypes == null || PassiveDinoTypes.Length == 0)
            {
                Debug.LogWarning("JEV village demo has no passive dinosaur types assigned.", this);
                return;
            }

            for (int index = 0; index < PassiveDinoCount; index++)
            {
                DinoSO type = PassiveDinoTypes[random.Next(PassiveDinoTypes.Length)];
                if (type == null || type.Prefab == null) continue;
                if (!TryFindNavPoint(random, center, PassiveSpawnRadius, out Vector3 point)) continue;
                Dino dino = Instantiate(type.Prefab, point, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f)) as Dino;
                if (dino == null) continue;
                dino.name = $"Passive Dino {index + 1:00}";
                // Formal DinoSO prefabs are disabled until DinoSpawner has
                // authorized a deployment. This demo instantiates directly,
                // so enable the component explicitly; Dino.Start restores its
                // authored lit material before we pause its behavior graph.
                dino.UnitType = type;
                dino.enabled = true;
                FreezeUnit(dino);
                PassiveDinos.Add(dino);
            }
        }

        private void SpawnBuildingGuards(System.Random random, Vector3 center)
        {
            BuildingGuards.Clear();
            if (GuardPrefabs == null || GuardPrefabs.Length == 0)
            {
                Debug.LogWarning("JEV village demo has no building guard prefabs assigned.", this);
                return;
            }

            RoundManager manager = RoundManager.Instance;
            for (int index = 0; index < GuardCount; index++)
            {
                Vector3 anchor = center;
                if (manager != null && manager.SessionHouses.Count > 0)
                {
                    VillageHouse house = manager.SessionHouses[index % manager.SessionHouses.Count];
                    if (house != null) anchor = house.transform.position;
                }

                Vector3 point = anchor;
                TryFindNavPoint(random, anchor, GuardSpawnRadius, out point);
                GameObject prefab = GuardPrefabs[random.Next(GuardPrefabs.Length)];
                if (prefab == null) continue;
                GameObject instance = Instantiate(prefab, point, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f));
                Defender guard = instance.GetComponentInChildren<Defender>(true);
                if (guard == null)
                {
                    Destroy(instance);
                    continue;
                }
                instance.name = $"Village Building Guard {index + 1:00}";
                FreezeUnit(guard);
                BuildingGuards.Add(guard);
            }
        }

        private static bool TryFindNavPoint(System.Random random, Vector3 center, float radius, out Vector3 point)
        {
            for (int attempt = 0; attempt < 18; attempt++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float distance = Mathf.Sqrt((float)random.NextDouble()) * radius;
                Vector3 candidate = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            }
            if (NavMesh.SamplePosition(center, out NavMeshHit fallback, 8f, NavMesh.AllAreas))
            {
                point = fallback.position;
                return true;
            }
            point = center;
            return false;
        }

        private static void FreezeUnit(Unit.Unit unit)
        {
            if (unit == null) return;
            NavMeshAgent agent = unit.Agent;
            if (agent != null && agent.enabled)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            BehaviorGraphAgent graph = unit.GetComponent<BehaviorGraphAgent>();
            if (graph != null) graph.enabled = false;
            DinoFixedBehaviorGraphDriver fixedDriver = unit.GetComponent<DinoFixedBehaviorGraphDriver>();
            if (fixedDriver != null) fixedDriver.enabled = false;
            Animator animator = unit.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.SetFloat("Speed", 0f);
        }

        private void FreezeSpawnedUnits()
        {
            foreach (Dino dino in PassiveDinos) FreezeUnit(dino);
            foreach (Defender guard in BuildingGuards) FreezeUnit(guard);
        }

        private void HideNormalSetupPanel()
        {
            DefenseBattleUI setupUi = FindFirstObjectByType<DefenseBattleUI>(FindObjectsInactive.Include);
            if (setupUi != null) setupUi.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!Started) return;
            FreezeSpawnedUnits();
        }

        private void OnGUI()
        {
            if (!ShowOverlay || !Application.isPlaying || !Started) return;
            OverlayBoxStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(14, 14, 10, 10),
                fontSize = 15,
                normal = { textColor = Color.white }
            };
            OverlayLabelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            string behavior = Companion == null ? "未连接" : Companion.BehaviorLabel;
            string text = $"JEV 村庄交互演示\n" +
                          $"布局种子：{LayoutSeed}  · 被动恐龙：{PassiveDinos.Count}  · 建筑守卫：{BuildingGuards.Count}\n" +
                          $"队友状态：{behavior}\n" +
                          "右下角打开对话：试试“待在我身边”“离开，自主防卫”“守住这里”。";
            GUI.Box(new Rect(16f, 16f, 540f, 112f), GUIContent.none, OverlayBoxStyle);
            GUI.Label(new Rect(30f, 27f, 510f, 92f), text, OverlayLabelStyle);
        }
    }
}
