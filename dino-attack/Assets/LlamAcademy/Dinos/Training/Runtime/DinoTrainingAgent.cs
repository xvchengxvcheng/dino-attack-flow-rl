using System;
using System.Collections;
using System.Collections.Generic;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Deployment;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Unit;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace LlamAcademy.Dinos.Training
{
    [DefaultExecutionOrder(100)]
    public sealed class DinoTrainingAgent : Agent
    {
        [Header("Environment")]
        [SerializeField] private DinoTrainingArea TrainingArea;
        [SerializeField] private DinoTrainingSceneReloader SceneReloader;
        [SerializeField] private DinoTrainingScenarioRandomizer ScenarioRandomizer;
        [SerializeField] private DinoSO[] DinoTypes = Array.Empty<DinoSO>();
        [SerializeField] private DinoDeploymentZone[] Zones = Array.Empty<DinoDeploymentZone>();

        [Header("Normalization")]
        [SerializeField, Min(1f)] private float FoodScale = 5000f;

        private readonly DinoTrainingRewardLedger RewardLedger = new();
        private readonly List<VillageHouse> SubscribedHouses = new();
        private bool EpisodeConcluded;
        private bool Subscribed;
        public bool LastActionWasValid { get; private set; } = true;
        public float FoodNormalizationScale => FoodScale;
        public float LastCompletedEpisodeReward => RewardLedger.LastConcludedReward;

        public bool AcceptsDecisions =>
            !EpisodeConcluded &&
            (SceneReloader == null || !SceneReloader.IsReloadPending);

        public void Configure(
            DinoTrainingArea trainingArea,
            DinoTrainingSceneReloader sceneReloader,
            DinoTrainingScenarioRandomizer scenarioRandomizer,
            DinoSO[] dinoTypes)
        {
            TrainingArea = trainingArea;
            SceneReloader = sceneReloader;
            ScenarioRandomizer = scenarioRandomizer;
            DinoTypes = dinoTypes ?? Array.Empty<DinoSO>();
        }

        public void ConfigureZones(DinoDeploymentZone[] zones)
        {
            Zones = zones ?? Array.Empty<DinoDeploymentZone>();
        }

        public override void Initialize()
        {
            base.Initialize();
            MaxStep = 0;
        }

        private IEnumerator Start()
        {
            yield return null;
            SubscribeToGameEvents();
        }

        public override void OnEpisodeBegin()
        {
            if (SceneReloader != null && SceneReloader.IsReloadPending)
            {
                EpisodeConcluded = true;
                return;
            }

            EpisodeConcluded = false;
            LastActionWasValid = true;
            RewardLedger.Reset();
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            // Formal observations are produced exclusively by DinoStructuredObservationSensorComponent.
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            ActionSegment<float> continuous = actions.ContinuousActions;
            float[] actionValues = new float[continuous.Length];
            for (int i = 0; i < continuous.Length; i++)
            {
                actionValues[i] = continuous[i];
            }

            if (!DinoTrainingActionCodec.TryDecode(actionValues, out DecodedDinoTrainingAction decoded))
            {
                RejectAction();
                return;
            }

            if (!decoded.ShouldPlace)
            {
                LastActionWasValid = true;
                AddLedgerReward(RewardLedger.RecordWait());
                return;
            }

            if (decoded.DinoIndex < 0 || decoded.DinoIndex >= DinoTypes.Length ||
                DinoSpawner.Instance == null || RoundManager.Instance == null ||
                !TryResolvePlacement(decoded.ZoneIndex, decoded.RelativeUv, out Vector3 position))
            {
                RejectAction();
                return;
            }

            DinoSO dino = DinoTypes[decoded.DinoIndex];
            if (!DinoSpawner.Instance.TryDeploy(dino, position, false, out DeploymentResult _, out _))
            {
                RejectAction();
                return;
            }

            LastActionWasValid = true;
            AddLedgerReward(RewardLedger.RecordAcceptedDeployment());

            if (RoundManager.Instance.State == GameState.Setup)
            {
                RoundManager.Instance.StartRound();
            }
        }

        private bool TryResolvePlacement(int zoneIndex, Vector2 relativeUv, out Vector3 position)
        {
            position = default;
            if (Zones == null || Zones.Length != 5 || zoneIndex < 0 || zoneIndex >= Zones.Length)
            {
                return false;
            }

            for (int index = 0; index < Zones.Length; index++)
            {
                DinoDeploymentZone zone = Zones[index];
                if (zone == null || !zone.IsGeometryValid || zone.Id != (DeploymentZoneId)index)
                {
                    return false;
                }
            }

            return Zones[zoneIndex].TryResolveRelative(relativeUv, out position);
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            ActionSegment<float> continuous = actionsOut.ContinuousActions;
            for (int i = 0; i < continuous.Length; i++)
            {
                continuous[i] = 0f;
            }
        }

        private void RejectAction()
        {
            LastActionWasValid = false;
            AddLedgerReward(RewardLedger.RecordRejectedDeployment());
        }

        private void SubscribeToGameEvents()
        {
            if (Subscribed || RoundManager.Instance == null || EnemyAIController.Instance == null)
            {
                return;
            }

            RoundManager.Instance.OnGameStateChange += OnGameStateChange;
            EnemyAIController.Instance.OnDefenderDeath += OnDefenderDeath;
            EnemyAIController.Instance.OnWallDeath += OnWallDeath;
            foreach (VillageHouse house in RoundManager.Instance.SessionHouses)
            {
                if (house == null)
                {
                    continue;
                }

                house.OnDeath -= OnHouseDeath;
                house.OnDeath += OnHouseDeath;
                SubscribedHouses.Add(house);
            }
            Subscribed = true;
        }

        private void OnGameStateChange(GameState oldState, GameState newState)
        {
            if (newState == GameState.Ending && !EpisodeConcluded)
            {
                bool won = RoundManager.Instance != null && RoundManager.Instance.PlayerWon;
                ConcludeEpisode(won, false);
            }
        }

        public void InterruptEpisodeForInfrastructure()
        {
            ConcludeEpisode(false, true);
        }

        private void OnDefenderDeath(Defender defender)
        {
            if (!EpisodeConcluded && defender != null &&
                RoundManager.Instance != null && RoundManager.Instance.State == GameState.Running)
            {
                AddLedgerReward(RewardLedger.RecordDefenderDeath(defender.GetInstanceID()));
            }
        }

        private void OnWallDeath(Wall wall)
        {
            if (!EpisodeConcluded && wall != null &&
                RoundManager.Instance != null && RoundManager.Instance.State == GameState.Running)
            {
                AddLedgerReward(RewardLedger.RecordWallDestroyed(wall.GetInstanceID()));
            }
        }

        private void OnHouseDeath(IDamageable damageable)
        {
            if (!EpisodeConcluded && damageable is VillageHouse house &&
                RoundManager.Instance != null && RoundManager.Instance.State == GameState.Running)
            {
                AddLedgerReward(RewardLedger.RecordHouseDestroyed(house.StableHouseId));
            }
        }

        private void ConcludeEpisode(bool won, bool interrupted)
        {
            if (EpisodeConcluded)
            {
                return;
            }

            EpisodeConcluded = true;
            if (interrupted)
            {
                RewardLedger.ConcludeInterrupted();
                EpisodeInterrupted();
            }
            else
            {
                int finalMeat = won && DinoSpawner.Instance != null
                    ? DinoSpawner.Instance.ResourcesToSpend
                    : 0;
                AddLedgerReward(RewardLedger.ConcludeNatural(won, finalMeat));
                EndEpisode();
            }
            SceneReloader?.ScheduleReload();
        }

        private void AddLedgerReward(float reward)
        {
            if (reward != 0f)
            {
                AddReward(reward);
            }
        }

        private void OnDestroy()
        {
            if (!Subscribed)
            {
                return;
            }

            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.OnGameStateChange -= OnGameStateChange;
            }
            if (EnemyAIController.Instance != null)
            {
                EnemyAIController.Instance.OnDefenderDeath -= OnDefenderDeath;
                EnemyAIController.Instance.OnWallDeath -= OnWallDeath;
            }
            foreach (VillageHouse house in SubscribedHouses)
            {
                if (house != null)
                {
                    house.OnDeath -= OnHouseDeath;
                }
            }
            SubscribedHouses.Clear();
        }
    }
}
