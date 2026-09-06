using LlamAcademy.Dinos.Map;
using UnityEngine;

namespace LlamAcademy.Dinos.Unit
{
    [DisallowMultipleComponent]
    public sealed class DinoTargetMetadata : MonoBehaviour
    {
        [field: SerializeField] public DinoTargetCategory Category { get; private set; }
        [field: SerializeField, Min(0)] public int StableId { get; private set; }

        public void Configure(DinoTargetCategory category, int stableId)
        {
            Category = category;
            StableId = Mathf.Max(0, stableId);
        }
    }
}
