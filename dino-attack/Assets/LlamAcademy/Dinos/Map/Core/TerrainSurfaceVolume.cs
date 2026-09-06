using UnityEngine;

namespace LlamAcademy.Dinos.Map
{
    [RequireComponent(typeof(Collider))]
    public sealed class TerrainSurfaceVolume : MonoBehaviour
    {
        [SerializeField] private TerrainSurfaceKind surface = TerrainSurfaceKind.Grass;

        public TerrainSurfaceKind Surface => surface;

        private void Reset()
        {
            ConfigureColliderAsTrigger();
        }

        private void OnValidate()
        {
            ConfigureColliderAsTrigger();
        }

        private void ConfigureColliderAsTrigger()
        {
            if (TryGetComponent(out Collider collider))
            {
                collider.isTrigger = true;
            }
        }
    }
}
