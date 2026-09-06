using System.Collections.Generic;
using UnityEngine;

namespace LlamAcademy.Dinos.Deployment
{
    public static class DinoDeploymentZoneLayout
    {
        public static bool ContainsPosition(
            Bounds fallbackBounds,
            IReadOnlyList<Bounds> approvedZones,
            Vector3 position,
            bool allowLegacyFallback)
        {
            if (approvedZones == null || approvedZones.Count == 0)
            {
                return allowLegacyFallback && ContainsHorizontal(fallbackBounds, position);
            }

            for (int i = 0; i < approvedZones.Count; i++)
            {
                if (ContainsHorizontal(approvedZones[i], position))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsHorizontal(Bounds bounds, Vector3 position) =>
            position.x >= bounds.min.x && position.x <= bounds.max.x &&
            position.z >= bounds.min.z && position.z <= bounds.max.z;
    }
}
