using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Unit;
using UnityEngine;

namespace LlamAcademy.Dinos.DefenseBattle
{
    [DisallowMultipleComponent]
    public sealed class PlacedDefenseHandle : MonoBehaviour
    {
        public DefensePlacementKind Kind { get; private set; }
        public int StableIndex { get; private set; }
        public Wall Wall { get; private set; }
        public Defender GroundGuard { get; private set; }
        public Defender WallGuard { get; private set; }

        public bool IsAlive => Wall != null ? Wall.Health > 0 : GroundGuard != null && GroundGuard.Health > 0;

        public void Configure(
            DefensePlacementKind kind,
            int stableIndex,
            Wall wall,
            Defender groundGuard,
            Defender wallGuard)
        {
            Kind = kind;
            StableIndex = stableIndex;
            Wall = wall;
            GroundGuard = groundGuard;
            WallGuard = wallGuard;
        }
    }
}
