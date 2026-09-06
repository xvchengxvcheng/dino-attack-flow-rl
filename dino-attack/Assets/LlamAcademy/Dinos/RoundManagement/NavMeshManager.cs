using System.Linq;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.RoundManagement
{
    [DefaultExecutionOrder(10)]
    public class NavMeshManager : MonoBehaviour
    {
        [SerializeField] private NavMeshSurface EnemySurface;
        [SerializeField] private NavMeshSurface[] DinoSurfaces;

        [SerializeField] private bool ShowDebugMesh;
        [SerializeField] private bool AllowRuntimeBuild;
        public int RuntimeBuildCount { get; private set; }
        public bool IsReady { get; private set; }

        public NavMeshTriangulation EnemyTriangulation;

        [SerializeField] private MeshRenderer NavMeshRenderer;
        private MeshFilter NavMeshFilter;

        public delegate void NavMeshUpdatedEvent();

        public event NavMeshUpdatedEvent OnNavMeshUpdated;

        public static NavMeshManager Instance { get; private set; }

        private void Awake()
        {
            IsReady = false;
            if (Instance != null)
            {
                Debug.LogError($"Multiple NavMeshManagers in the scene. Destroying second one {name}.");
                Destroy(gameObject);
            }

            Instance = this;

            NavMesh.RemoveAllNavMeshData();
            bool allSurfacesInstalled = AddOrBuild(EnemySurface);
            EnemyTriangulation = NavMesh.CalculateTriangulation();
            NavMeshFilter = NavMeshRenderer.GetComponent<MeshFilter>();
            SetVisualizationFromTriangulation();

            foreach (NavMeshSurface surface in DinoSurfaces)
            {
                allSurfacesInstalled &= AddOrBuild(surface);
            }
            IsReady = allSurfacesInstalled && EnemyTriangulation.vertices.Length > 0;
        }

        /// <summary>
        /// Recalculates the NavMeshTriangulation for the enemy surface, rebaking all known surfaces.
        /// </summary>
        /// <param name="rebuildEnemySurface"></param>
        public void RecalculateTriangulation(bool rebuildEnemySurface = false)
        {
            IsReady = false;
            NavMesh.RemoveAllNavMeshData();
            if (rebuildEnemySurface && AllowRuntimeBuild)
            {
                EnemySurface.BuildNavMesh();
                RuntimeBuildCount++;
            }
            else
            {
                NavMesh.AddNavMeshData(EnemySurface.navMeshData);
            }
            EnemyTriangulation = NavMesh.CalculateTriangulation();
            SetVisualizationFromTriangulation();
            foreach (NavMeshSurface surface in DinoSurfaces)
            {
                NavMesh.AddNavMeshData(surface.navMeshData);
            }

            IsReady = EnemySurface != null && EnemySurface.navMeshData != null &&
                      DinoSurfaces != null && DinoSurfaces.All(surface => surface != null && surface.navMeshData != null) &&
                      EnemyTriangulation.vertices.Length > 0;
            OnNavMeshUpdated?.Invoke();
        }

        private bool AddOrBuild(NavMeshSurface surface)
        {
            if (surface == null || surface.navMeshData == null)
            {
                Debug.LogError("A pre-baked NavMeshData reference is required.", this);
                return false;
            }
            if (AllowRuntimeBuild)
            {
                surface.BuildNavMesh();
                RuntimeBuildCount++;
            }
            else
            {
                NavMesh.AddNavMeshData(surface.navMeshData);
            }
            return true;
        }

        /// <summary>
        /// When the NavMesh has been updated by obstacles, call this to ensure any event listeners are notified.
        /// This does not recalculate triangulation or rebuild any surfaces.
        /// </summary>
        public void NavMeshUpdatedByObstacles()
        {
            OnNavMeshUpdated?.Invoke();
        }

        private void SetVisualizationFromTriangulation()
        {
            NavMeshFilter.mesh = new Mesh();
            NavMeshFilter.mesh.SetVertices(EnemyTriangulation.vertices);
            NavMeshFilter.mesh.SetIndices(EnemyTriangulation.indices, MeshTopology.Triangles, 0);
        }
    }
}
