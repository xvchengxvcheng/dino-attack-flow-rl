using System.Collections;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Utility;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Unit
{
    public class Wall : Unit
    {
        [SerializeField] private GameObject Root;
        [field: SerializeField] public float HealthToCostMultiplier { get; private set; } = 0.2f;
        [SerializeField] private NavMeshUpdateData UpdateNavMeshData;

        private float ObstacleSpawnHeight;

        protected override void Awake()
        {
            base.Awake();

            if (Root == null)
            {
                Root = gameObject;
            }

            Rigidbody = null;

            ObstacleSpawnHeight = UpdateNavMeshData.Obstacles.Length == 0 ? 0 : UpdateNavMeshData.Obstacles[0].transform.position.y;
        }

        private void OnDestroy()
        {
            if (HealthBarCanvas.Instance != null)
            {
                HealthBarCanvas.Instance.Unregister(this, false);
            }
            if (HealthBar != null)
            {
                Destroy(HealthBar.gameObject);
            }

            UpdateNavMeshData.Destroy();
        }

        protected override void OnEnable()
        {
            if (HealthBar != null)
            {
                HealthBar.gameObject.SetActive(true);
                if (HealthBarCanvas.Instance != null)
                {
                    HealthBarCanvas.Instance.Register(HealthBar, this);
                }
            }

            UpdateNavMeshData.RemoveNavMeshData();
            UpdateNavMeshData.RestoreOriginalObstaclePosition(ObstacleSpawnHeight);
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            if (HealthBarCanvas.Instance != null)
            {
                HealthBarCanvas.Instance.Unregister(this, true);
            }
            base.OnDisable();
        }

        protected override void OnTargetEnter(IDamageable target) {}

        protected override void OnTargetExit(IDamageable target) {}

        public void Repair(int amount)
        {
            if (Health == 0 && amount > 0)
            {
                gameObject.SetActive(true);
            }
            Health += amount;
            if (Health > MaxHealth)
            {
                Debug.LogWarning($"Health attempted to exceed max health by {MaxHealth - Health}. Clamped to Max Health. This probably means there is a math error elsewhere.");
                Health = MaxHealth;
            }

            if (HealthBar != null)
            {
                HealthBar.SetProgress((float)Health / MaxHealth);
            }
        }

        public override void Die()
        {
            UpdateNavMeshData.MoveObstacles();
            StartCoroutine(HandleDeath());
        }

        private IEnumerator HandleDeath()
        {
            yield return UpdateNavMeshData.Rebake();
            // destruction here would be cool
            Root.gameObject.SetActive(false);
            NavMeshManager.Instance.NavMeshUpdatedByObstacles();
        }

        [System.Serializable]
        private struct NavMeshUpdateData
        {
            [field: SerializeField] public NavMeshObstacle[] Obstacles { get; private set; }
            [field: SerializeField] public float DeathObstacleHeight { get; private set; }
            [field: SerializeField] public NavMeshSurface[] RebakeSurfacesOnDeath { get; private set; }
            [field: SerializeField] public GameObject[] EnableObjectsBeforeRebake { get; private set; }
            [field: SerializeField] public MonoBehaviour[] EnableComponentsBeforeRebake { get; private set; }

            /// <summary>
            /// Bake all <see cref="RebakeSurfacesOnDeath"/> after enabling all <see cref="EnableObjectsBeforeRebake"/> and <see cref="EnableComponentsBeforeRebake"/>
            /// </summary>
            public IEnumerator Rebake()
            {
                foreach (GameObject gameObject in EnableObjectsBeforeRebake)
                {
                    gameObject.SetActive(true);
                    gameObject.transform.SetParent(null, true);
                }
                foreach (MonoBehaviour behaviour in EnableComponentsBeforeRebake)
                {
                    behaviour.enabled = true;
                }

                yield return null;
                foreach (NavMeshSurface surface in RebakeSurfacesOnDeath)
                {
                    surface.BuildNavMesh();
                }
            }

            /// <summary>
            /// Remove all NavMeshData from all <see cref="RebakeSurfacesOnDeath"/> and disable all <see cref="EnableObjectsBeforeRebake"/> and <see cref="EnableComponentsBeforeRebake"/>
            /// </summary>
            public void RemoveNavMeshData()
            {
                foreach (NavMeshSurface surface in RebakeSurfacesOnDeath)
                {
                    surface.RemoveData();
                }

                foreach (GameObject gameObject in EnableObjectsBeforeRebake)
                {
                    gameObject.SetActive(false);
                }
                foreach (MonoBehaviour behaviour in EnableComponentsBeforeRebake)
                {
                    behaviour.enabled = false;
                }
            }

            /// <summary>
            /// Shift all obstacles by the <see cref="DeathObstacleHeight"/>
            /// </summary>
            public void MoveObstacles()
            {
                foreach (NavMeshObstacle obstacle in Obstacles)
                {
                    Vector3 originalPosition = obstacle.transform.position;
                    obstacle.transform.position = new Vector3(originalPosition.x, DeathObstacleHeight, originalPosition.z);
                }
            }

            /// <summary>
            /// Restores all obstacles to the specified height.
            /// Note this only supports Y shifting obstacles.
            /// </summary>
            /// <param name="spawnHeight"></param>
            public void RestoreOriginalObstaclePosition(float spawnHeight)
            {
                foreach (NavMeshObstacle obstacle in Obstacles)
                {
                    Vector3 originalPosition = obstacle.transform.position;
                    obstacle.transform.position = new Vector3(originalPosition.x, spawnHeight, originalPosition.z);
                    obstacle.transform.SetParent(null, true);
                }
            }

            /// <summary>
            /// Removes all baked NavMeshData and destroys all related obstacles
            /// </summary>
            public void Destroy()
            {
                foreach (NavMeshObstacle obstacle in Obstacles)
                {
                    if (obstacle != null)
                    {
                        Object.Destroy(obstacle.gameObject);
                    }
                }

                foreach (NavMeshSurface surface in RebakeSurfacesOnDeath)
                {
                    if (surface != null)
                    {
                        surface.RemoveData();
                    }
                }
            }
        }

    }
}
