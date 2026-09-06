using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Training
{
    [DefaultExecutionOrder(-50)]
    public sealed class DinoTrainingScenarioRandomizer : MonoBehaviour
    {
        [SerializeField] private DinoTrainingArea TrainingArea;
        [SerializeField] private bool BuildRuntimeObstacles = true;
        private readonly List<GameObject> Obstacles = new();

        public int ActiveScenario { get; private set; }

        public void Configure(DinoTrainingArea trainingArea)
        {
            TrainingArea = trainingArea;
        }

        private void Start()
        {
            float requested = Academy.Instance.EnvironmentParameters.GetWithDefault("scenario_index", -1f);
            ActiveScenario = requested >= 0f
                ? Mathf.Clamp(Mathf.RoundToInt(requested), 0, 2)
                : Random.Range(0, 3);

            if (BuildRuntimeObstacles)
            {
                BuildScenario(ActiveScenario);
            }
        }

        private void BuildScenario(int scenario)
        {
            ClearObstacles();
            if (TrainingArea == null || scenario == 0)
            {
                return;
            }

            Bounds bounds = TrainingArea.Bounds;
            float width = Mathf.Max(bounds.size.x, 10f);
            float depth = Mathf.Max(bounds.size.z, 10f);
            float y = bounds.min.y + 1f;

            if (scenario == 1)
            {
                CreateObstacle("DualLaneCenter", new Vector3(bounds.center.x, y, bounds.center.z),
                    new Vector3(width * 0.22f, 2f, depth * 0.55f));
            }
            else
            {
                CreateObstacle("ChokeLeftNear", new Vector3(bounds.center.x - width * 0.28f, y, bounds.center.z - depth * 0.15f),
                    new Vector3(width * 0.42f, 2f, depth * 0.12f));
                CreateObstacle("ChokeRightFar", new Vector3(bounds.center.x + width * 0.28f, y, bounds.center.z + depth * 0.18f),
                    new Vector3(width * 0.42f, 2f, depth * 0.12f));
            }
        }

        private void CreateObstacle(string objectName, Vector3 position, Vector3 scale)
        {
            GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = objectName;
            obstacle.transform.SetParent(transform, true);
            obstacle.transform.position = position;
            obstacle.transform.localScale = scale;
            obstacle.AddComponent<DinoTrainingObstacle>();
            NavMeshObstacle navMeshObstacle = obstacle.AddComponent<NavMeshObstacle>();
            navMeshObstacle.shape = NavMeshObstacleShape.Box;
            navMeshObstacle.carving = true;
            navMeshObstacle.carveOnlyStationary = true;
            Obstacles.Add(obstacle);
        }

        private void ClearObstacles()
        {
            foreach (GameObject obstacle in Obstacles)
            {
                if (obstacle != null)
                {
                    Destroy(obstacle);
                }
            }
            Obstacles.Clear();
        }
    }
}
