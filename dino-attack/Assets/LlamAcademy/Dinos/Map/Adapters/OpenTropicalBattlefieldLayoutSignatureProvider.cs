using System;
using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Enemy.Defense;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Unit;
using UnityEngine;

namespace LlamAcademy.Dinos.Map.Adapters
{
    [DisallowMultipleComponent]
    public sealed class OpenTropicalBattlefieldLayoutSignatureProvider : MonoBehaviour, IBattlefieldLayoutSignatureProvider
    {
        public bool TryCapture(out BattlefieldLayoutSignature signature)
        {
            signature = null;
            GameSessionRestartService restart = GameSessionRestartService.Instance;
            RoundManager round = RoundManager.Instance;
            SessionDefenseLayoutController walls = FindFirstObjectByType<SessionDefenseLayoutController>(FindObjectsInactive.Include);
            SessionGroundDefenseController ground = FindFirstObjectByType<SessionGroundDefenseController>(FindObjectsInactive.Include);
            DinoDeploymentZone[] zones = FindObjectsByType<DinoDeploymentZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OrderBy(value => value.Id)
                .ToArray();
            if (restart == null || round == null || round.DinoTarget == null || walls == null || ground == null ||
                zones.Length != 5 || round.SessionHouses == null || round.SessionHouses.Count != 8 ||
                walls.ActiveWalls.Count != 3 || walls.ActiveGuards.Count != 3 || ground.ActiveGroundGuards.Count != 5)
            {
                return false;
            }

            BattlefieldZoneSignature[] zoneSignatures = zones.Select(zone =>
                new BattlefieldZoneSignature(
                    zone.Id,
                    zone.WorldVertices.Select(vertex => new Vector2(vertex.x, vertex.z)))).ToArray();
            BattlefieldEntitySignature[] houseSignatures = round.SessionHouses
                .Where(value => value != null)
                .Select(value => At($"house_{value.StableHouseId}", value.transform.position))
                .ToArray();
            BattlefieldEntitySignature[] wallSignatures = walls.ActiveWalls
                .Where(value => value != null)
                .Select(value => At(StableTargetId("wall", value.gameObject), value.transform.position))
                .ToArray();
            BattlefieldEntitySignature[] guardSignatures = walls.ActiveGuards
                .Concat(ground.ActiveGroundGuards)
                .Where(value => value != null)
                .Select(value => At(StableTargetId("guard", value.gameObject), value.transform.position))
                .ToArray();
            if (houseSignatures.Length != 8 || wallSignatures.Length != 3 || guardSignatures.Length != 8)
            {
                return false;
            }

            signature = BattlefieldLayoutSignature.Create(
                "open_tropical_battlefield",
                restart.CurrentStartSeed,
                0,
                zoneSignatures,
                houseSignatures,
                wallSignatures,
                guardSignatures,
                new[] { At("dino_target", round.DinoTarget.position) });
            return true;
        }

        private static string StableTargetId(string prefix, GameObject target)
        {
            DinoTargetMetadata metadata = target.GetComponent<DinoTargetMetadata>();
            if (metadata == null)
            {
                throw new InvalidOperationException($"Generated {prefix} '{target.name}' has no stable target metadata.");
            }
            return $"{prefix}_{metadata.StableId}";
        }

        private static BattlefieldEntitySignature At(string stableId, Vector3 position) =>
            new(stableId, new Vector2(position.x, position.z));
    }
}
