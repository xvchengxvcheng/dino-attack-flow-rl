using System;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Deployment;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Training;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace LlamAcademy.Dinos.Player
{
    [DefaultExecutionOrder(10)]
    public class DinoSpawner : MonoBehaviour
    {
        [SerializeField] private int AvailableResources;
        [SerializeField] private ResourceSO FoodResource;
        public int ResourcesToSpend
        {
            get => FoodResource.Amount;
            set => FoodResource.Amount = value;
        }

        public static DinoSpawner Instance { get; private set; }
        public delegate void SpawnDinoEvent(Unit.Unit spawnedDino);
        public event SpawnDinoEvent OnSpawnDino;
        public delegate void DinoDeathEvent(Unit.Unit deadDino);
        public event DinoDeathEvent OnDinoDeath;
        public event Action<DeploymentResult> OnDeploymentEvaluated;

        private DinoSO SpawnDino;
        public bool PlayerInputEnabled { get; private set; } = true;

        [SerializeField]
        private Camera Camera;
        [SerializeField]
        private LayerMask GroundLayer;

        [SerializeField]
        private PlaceDinoVisualization Visualization;
        [SerializeField]
        private DinoDeploymentService DeploymentService;

        private void Awake()
        {
            if (Instance != null)
            {
                Debug.LogError($"Multiple RoundManagers detected. Deleting the second one {name}");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ResourcesToSpend = 0;
        }

        private void Start()
        {
            RoundManager.Instance.OnGameStateChange += Instance_OnGameStateChange;
        }

        private void Instance_OnGameStateChange(GameState oldState, GameState newState)
        {
            if (!IsInteractiveState(newState))
            {
                Visualization.ChangeDino(null);
            }
            else
            {
                Visualization.ChangeDino(SpawnDino);
            }
        }

        public void Update()
        {
            if (!PlayerInputEnabled)
            {
                return;
            }

            if (RoundManager.Instance != null && IsInteractiveState(RoundManager.Instance.State) &&
                Camera != null && Mouse.current != null)
            {
                if (Physics.Raycast(
                           Camera.ScreenPointToRay(Mouse.current.position.ReadValue()),
                           out RaycastHit hit,
                           float.MaxValue,
                           GroundLayer))
                {
                    Visualization.transform.position = hit.point;
                }

                if (Mouse.current.leftButton.wasReleasedThisFrame
                     && SpawnDino != null
                     && hit.collider != null)
                {
                    bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                    TryDeploy(SpawnDino, hit.point, pointerOverUi, out _, out _);
                }

                if (Keyboard.current.escapeKey.wasReleasedThisFrame)
                {
                    SpawnDino = null;
                    Visualization.ChangeDino(null);
                }
            }
        }

        public void ChangeActiveDino(DinoSO Dino)
        {
            SpawnDino = Dino;
            if (RoundManager.Instance != null && IsInteractiveState(RoundManager.Instance.State))
            {
                Visualization.ChangeDino(Dino);
            }
        }

        /// <summary>
        /// Compatibility entry point for the historical isolated training scene.
        /// Both player and AI deployments now share <see cref="TryDeploy"/>.
        /// </summary>
        public bool TrySpawnDino(
            DinoSO dino,
            Vector3 position,
            bool allowDuringRunning,
            out Unit.Unit spawnedDino)
        {
            if (DeploymentService != null)
            {
                return TryDeploy(dino, position, false, out _, out spawnedDino);
            }

            spawnedDino = null;
            if (dino == null || RoundManager.Instance == null)
            {
                return false;
            }

            if (IsAtDinoCap())
            {
                OnDeploymentEvaluated?.Invoke(DeploymentResult.Rejected(DeploymentFailureReason.UnitCapReached));
                return false;
            }

            GameState state = RoundManager.Instance.State;
            bool validState = state == GameState.Setup || allowDuringRunning && state == GameState.Running;
            return validState && ResourcesToSpend >= dino.Cost &&
                   SpawnAuthorizedDino(dino, position, out spawnedDino);
        }

        public void SetPlayerInputEnabled(bool enabled)
        {
            PlayerInputEnabled = enabled;
            if (!enabled)
            {
                SpawnDino = null;
                Visualization?.ChangeDino(null);
            }
        }

        public DeploymentResult EvaluateDeployment(DinoSO dino, Vector3 position, bool blockedByUi)
        {
            if (DeploymentService == null)
            {
                return DeploymentResult.Rejected(DeploymentFailureReason.ConfigurationError);
            }

            if (IsAtDinoCap())
            {
                return DeploymentResult.Rejected(DeploymentFailureReason.UnitCapReached);
            }

            return DeploymentService.Evaluate(
                ToDeploymentPhase(RoundManager.Instance == null ? GameState.Ended : RoundManager.Instance.State),
                dino != null,
                ResourcesToSpend,
                dino == null ? 0 : dino.Cost,
                position,
                GetDeploymentRadius(dino),
                blockedByUi,
                out _);
        }

        public bool TryDeploy(
            DinoSO dino,
            Vector3 position,
            bool blockedByUi,
            out DeploymentResult result,
            out Unit.Unit spawnedDino)
        {
            spawnedDino = null;
            if (DeploymentService == null)
            {
                result = DeploymentResult.Rejected(DeploymentFailureReason.ConfigurationError);
                OnDeploymentEvaluated?.Invoke(result);
                return false;
            }

            if (IsAtDinoCap())
            {
                result = DeploymentResult.Rejected(DeploymentFailureReason.UnitCapReached);
                OnDeploymentEvaluated?.Invoke(result);
                return false;
            }

            result = DeploymentService.Evaluate(
                ToDeploymentPhase(RoundManager.Instance == null ? GameState.Ended : RoundManager.Instance.State),
                dino != null,
                ResourcesToSpend,
                dino == null ? 0 : dino.Cost,
                position,
                GetDeploymentRadius(dino),
                blockedByUi,
                out Vector3 resolvedPosition);
            if (!result.Allowed)
            {
                OnDeploymentEvaluated?.Invoke(result);
                return false;
            }

            if (IsAtDinoCap())
            {
                result = DeploymentResult.Rejected(DeploymentFailureReason.UnitCapReached);
                OnDeploymentEvaluated?.Invoke(result);
                return false;
            }

            bool spawned = SpawnAuthorizedDino(dino, resolvedPosition, out spawnedDino);
            OnDeploymentEvaluated?.Invoke(result);
            return spawned;
        }

        private bool SpawnAuthorizedDino(DinoSO dino, Vector3 position, out Unit.Unit spawnedDino)
        {
            spawnedDino = null;
            Vector3 targetDirection = RoundManager.Instance.DinoTarget.position - position;
            if (targetDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                targetDirection = Vector3.forward;
            }

            ResourcesToSpend -= dino.Cost;
            spawnedDino = Instantiate(
                dino.Prefab,
                position,
                Quaternion.LookRotation(targetDirection.normalized));
            spawnedDino.OnDeath += obj => OnDinoDeath?.Invoke(obj.Transform.GetComponent<Unit.Unit>());
            spawnedDino.UnitType = dino;
            spawnedDino.enabled = true;
            OnSpawnDino?.Invoke(spawnedDino);
            return true;
        }

        private static bool IsAtDinoCap() =>
            RoundManager.Instance != null &&
            RoundManager.Instance.AliveDinos >= DinoTrainingObservationLayout.MaxDinos;

        private static bool IsInteractiveState(GameState state) =>
            state == GameState.Setup || state == GameState.Running;

        private static float GetDeploymentRadius(DinoSO dino)
        {
            if (dino == null || dino.Prefab == null)
            {
                return 0.35f;
            }

            UnityEngine.AI.NavMeshAgent agent = dino.Prefab.GetComponent<UnityEngine.AI.NavMeshAgent>();
            return agent == null ? 0.35f : agent.radius;
        }

        private static DeploymentPhase ToDeploymentPhase(GameState state) => state switch
        {
            GameState.Setup => DeploymentPhase.Setup,
            GameState.Running => DeploymentPhase.Running,
            GameState.Ending => DeploymentPhase.Ending,
            GameState.Scoring => DeploymentPhase.Scoring,
            GameState.EnemyRepairs => DeploymentPhase.EnemyRepairs,
            _ => DeploymentPhase.Ended
        };

        private void OnDestroy()
        {
            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.OnGameStateChange -= Instance_OnGameStateChange;
            }
        }
    }
}
