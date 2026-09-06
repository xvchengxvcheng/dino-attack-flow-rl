using UnityEngine;

namespace LlamAcademy.Dinos.Enemy.Defense
{
    [DisallowMultipleComponent]
    public sealed class WallSpawnSlot : MonoBehaviour
    {
        [field: SerializeField, Min(0)] public int StableId { get; private set; }
        [field: SerializeField] public Transform GuardAnchor { get; private set; }

        public void Configure(int stableId, Transform guardAnchor)
        {
            StableId = Mathf.Max(0, stableId);
            GuardAnchor = guardAnchor;
        }
    }
}
