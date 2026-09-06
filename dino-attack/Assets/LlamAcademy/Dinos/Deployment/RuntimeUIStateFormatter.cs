using System;
using System.Globalization;
using LlamAcademy.Dinos.Deployment;

namespace LlamAcademy.Dinos.UI
{
    public static class RuntimeUIStateFormatter
    {
        public static string FormatPhase(int round, string state)
        {
            string label = state switch
            {
                "Setup" => "Setup",
                "Running" => "Battle",
                "Ending" => "Ending",
                "Ended" => "Complete",
                "Scoring" => "Scoring",
                "EnemyRepairs" => "Enemy Repairs",
                _ => state ?? "Unknown"
            };
            return $"Session · {label}";
        }

        public static string FormatFood(int current, int delta)
        {
            string sign = delta >= 0 ? "+" : string.Empty;
            return $"Food {current}  ({sign}{delta})";
        }

        public static string FormatPressure(int enemies, int structures)
        {
            return enemies == 0 && structures == 0
                ? "Threat · Clear"
                : $"Threat · {enemies} enemies / {structures} structures";
        }

        public static string FormatTerrain(string zoneName, string surfaceName, float multiplier)
        {
            string safeZone = string.IsNullOrWhiteSpace(zoneName) ? "Unknown zone" : zoneName.Trim();
            string safeSurface = string.IsNullOrWhiteSpace(surfaceName) ? "Grass" : surfaceName.Trim();
            float safeMultiplier = float.IsFinite(multiplier) && multiplier > 0f ? multiplier : 1f;
            return $"{safeZone} · {safeSurface} {safeMultiplier.ToString("0.00", CultureInfo.InvariantCulture)}×";
        }

        public static string FormatDeploymentFailure(DeploymentFailureReason reason) => reason switch
        {
            DeploymentFailureReason.InvalidState => "[!] Deployment is locked during this phase",
            DeploymentFailureReason.UnknownType => "[!] Select a dinosaur",
            DeploymentFailureReason.InsufficientFood => "[!] Not enough food",
            DeploymentFailureReason.OutsideBounds => "[!] Outside the deployment area",
            DeploymentFailureReason.NoGround => "[!] No valid ground",
            DeploymentFailureReason.NoNavMesh => "[!] No reachable ground",
            DeploymentFailureReason.Overlap => "[!] Position is blocked",
            DeploymentFailureReason.BlockedByUI => "[!] Pointer is over the interface",
            DeploymentFailureReason.ConfigurationError => "[!] Deployment service is unavailable",
            _ => "[!] Cannot deploy here"
        };
    }
}
