using System;
using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.Deployment;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Enemy.Defense;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Session;
using LlamAcademy.Dinos.Training;
using LlamAcademy.Dinos.Unit;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace LlamAcademy.Dinos.DefenseBattle
{
    [DefaultExecutionOrder(30), DisallowMultipleComponent]
    public sealed class DefensePlacementController : MonoBehaviour
    {
        public const string SnapshotConfigVersion = "defense_battlefield_v1";

        private readonly List<PlacedDefenseHandle> Placed = new();
        private readonly Collider[] BlockingHits = new Collider[128];

        [SerializeField] private Camera PlacementCamera;
        [SerializeField] private Wall WallPrefab;
        [SerializeField] private Defender ArcherPrefab;
        [SerializeField] private Defender MagePrefab;
        [SerializeField] private Transform SpawnRoot;
        [SerializeField] private DinoDeploymentZone[] DeploymentZones = Array.Empty<DinoDeploymentZone>();
        [SerializeField] private LayerMask GroundLayer;
        [SerializeField] private Vector2 DeploymentMin = new(-24f, -49f);
        [SerializeField] private Vector2 DeploymentMax = new(20f, -11f);
        [SerializeField, Min(0.1f)] private float NavMeshSampleRadius = 0.75f;
        [SerializeField, Min(0.1f)] private float WallGuardAttackRadius = 14f;

        private DefenseInventoryState InventoryValue = DefenseInventoryState.Full;
        private DefensePlacementKind? SelectedKind;
        private GameObject Ghost;
        private Renderer[] GhostRenderers = Array.Empty<Renderer>();
        private Material GhostMaterial;
        private GameObject AreaVisual;
        private Material AreaLineMaterial;
        private Material AreaFillMaterial;
        private Vector3 ResolvedGhostPosition;
        private bool GhostIsLegal;

        public DefenseInventoryState Inventory => InventoryValue;
        public DefensePlacementKind? Selection => SelectedKind;
        public bool IsLocked { get; private set; }
        public bool IsComplete => InventoryValue.IsComplete;
        public int AliveGroundGuards => Placed.Count(value => value != null && value.Kind != DefensePlacementKind.Wall && value.IsAlive);
        public int AliveWalls => Placed.Count(value => value != null && value.Kind == DefensePlacementKind.Wall && value.IsAlive);

        public event Action StateChanged;
        public event Action<string> PlacementRejected;

        public void Configure(
            Camera placementCamera,
            Wall wallPrefab,
            Defender archerPrefab,
            Defender magePrefab,
            Transform spawnRoot,
            DinoDeploymentZone[] deploymentZones,
            LayerMask groundLayer,
            Vector2 deploymentMin,
            Vector2 deploymentMax,
            float wallGuardAttackRadius)
        {
            PlacementCamera = placementCamera;
            WallPrefab = wallPrefab;
            ArcherPrefab = archerPrefab;
            MagePrefab = magePrefab;
            SpawnRoot = spawnRoot;
            DeploymentZones = deploymentZones ?? Array.Empty<DinoDeploymentZone>();
            GroundLayer = groundLayer;
            DeploymentMin = deploymentMin;
            DeploymentMax = deploymentMax;
            WallGuardAttackRadius = Mathf.Max(0.1f, wallGuardAttackRadius);
        }

        private void Start()
        {
            CreateAreaVisual();
            int seed = GameSessionRestartService.Instance == null
                ? 20260831
                : GameSessionRestartService.Instance.CurrentStartSeed;
            if (DefenseBattleReplayContext.TryConsume(seed, out DefenseLayoutSnapshot snapshot))
            {
                if (!TryRestoreSnapshot(snapshot, out string reason))
                {
                    Debug.LogError($"Defense retry layout could not be restored: {reason}", this);
                    PlacementRejected?.Invoke(reason);
                }
            }
        }

        private void Update()
        {
            if (IsLocked || RoundManager.Instance == null || RoundManager.Instance.State != GameState.Setup ||
                PlacementCamera == null || Mouse.current == null)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasReleasedThisFrame)
            {
                CancelSelection();
            }

            if (Mouse.current.rightButton.wasReleasedThisFrame)
            {
                TryRemoveAtPointer();
            }

            if (!SelectedKind.HasValue || Ghost == null)
            {
                return;
            }

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Ray ray = PlacementCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, float.MaxValue, GroundLayer, QueryTriggerInteraction.Ignore))
            {
                SetGhostVisible(false);
                return;
            }

            SetGhostVisible(true);
            Ghost.transform.SetPositionAndRotation(hit.point, GetPlacementRotation(SelectedKind.Value));
            GhostIsLegal = !overUi && TryResolveLegalPosition(SelectedKind.Value, Ghost, hit.point,
                out ResolvedGhostPosition, out _);
            Ghost.transform.position = ResolvedGhostPosition;
            SetGhostColor(GhostIsLegal ? new Color(0.12f, 0.62f, 1f, 0.7f) : new Color(1f, 0.18f, 0.12f, 0.7f));

            if (Mouse.current.leftButton.wasReleasedThisFrame && !overUi)
            {
                if (GhostIsLegal)
                {
                    PlaceSelectedAt(ResolvedGhostPosition);
                }
                else
                {
                    PlacementRejected?.Invoke("该位置不可放置");
                }
            }
        }

        public bool Select(DefensePlacementKind kind)
        {
            if (IsLocked || InventoryValue.Remaining(kind) <= 0)
            {
                return false;
            }

            SelectedKind = kind;
            RebuildGhost(kind);
            StateChanged?.Invoke();
            return true;
        }

        public void CancelSelection()
        {
            SelectedKind = null;
            DestroyGhost();
            StateChanged?.Invoke();
        }

        public void LockForBattle()
        {
            IsLocked = true;
            CancelSelection();
            if (AreaVisual != null) AreaVisual.SetActive(false);
            StateChanged?.Invoke();
        }

        public bool TryCreateSnapshot(out DefenseLayoutSnapshot snapshot, out string reason)
        {
            snapshot = null;
            if (!InventoryValue.IsComplete || Placed.Count != 11)
            {
                reason = "布防尚未完成";
                return false;
            }

            int seed = GameSessionRestartService.Instance == null
                ? 20260831
                : GameSessionRestartService.Instance.CurrentStartSeed;
            snapshot = new DefenseLayoutSnapshot(
                GameMapId.DefenseBattlefield,
                seed,
                SnapshotConfigVersion,
                Placed.Select(ToToken));
            reason = string.Empty;
            return true;
        }

        public bool TryRestoreSnapshot(DefenseLayoutSnapshot snapshot, out string reason)
        {
            if (snapshot == null || snapshot.MapId != GameMapId.DefenseBattlefield ||
                snapshot.ConfigVersion != SnapshotConfigVersion || Placed.Count != 0)
            {
                reason = "保存的布防布局不兼容";
                return false;
            }

            foreach (DefensePlacementToken token in snapshot.Placements)
            {
                if (!TrySpawnPlacement(token.Kind, token.Index,
                        new Vector3(token.X, token.Y, token.Z), Quaternion.Euler(0f, token.RotationY, 0f),
                        out _))
                {
                    reason = $"无法恢复 {token.Kind} {token.Index}";
                    return false;
                }
                InventoryValue.TryPlace(token.Kind, out InventoryValue);
            }

            IsLocked = true;
            if (AreaVisual != null) AreaVisual.SetActive(false);
            reason = string.Empty;
            StateChanged?.Invoke();
            return true;
        }

        private void PlaceSelectedAt(Vector3 position)
        {
            DefensePlacementKind kind = SelectedKind.Value;
            int index = kind switch
            {
                DefensePlacementKind.Wall => DefenseInventoryState.InitialWalls - InventoryValue.Walls,
                DefensePlacementKind.Archer => DefenseInventoryState.InitialArchers - InventoryValue.Archers,
                DefensePlacementKind.Mage => DefenseInventoryState.InitialMages - InventoryValue.Mages,
                _ => 0,
            };
            if (!TrySpawnPlacement(kind, index, position, GetPlacementRotation(kind), out string reason) ||
                !InventoryValue.TryPlace(kind, out InventoryValue))
            {
                PlacementRejected?.Invoke(string.IsNullOrEmpty(reason) ? "该单位已用完" : reason);
                return;
            }

            if (InventoryValue.Remaining(kind) == 0)
            {
                CancelSelection();
            }
            StateChanged?.Invoke();
        }

        private bool TrySpawnPlacement(
            DefensePlacementKind kind,
            int index,
            Vector3 position,
            Quaternion rotation,
            out string reason)
        {
            reason = string.Empty;
            EnemyAIController registry = EnemyAIController.Instance;
            if (registry == null || SpawnRoot == null || WallPrefab == null || ArcherPrefab == null || MagePrefab == null)
            {
                reason = "布防组件配置不完整";
                return false;
            }

            if (kind == DefensePlacementKind.Wall)
            {
                Wall wall = Instantiate(WallPrefab, position, rotation, SpawnRoot);
                wall.name = $"Player Wall {index + 1}";
                ConfigureMetadata(wall.gameObject, DinoTargetCategory.Wall, 101 + index);

                Transform anchor = new GameObject($"Wall Guard Anchor {index + 1}").transform;
                anchor.SetParent(wall.transform, false);
                Bounds wallBounds = CalculateBounds(wall.gameObject);
                NavMeshAgent archerAgent = ArcherPrefab.GetComponent<NavMeshAgent>();
                Vector3 requestedAnchor = wallBounds.center + Vector3.right *
                    (wallBounds.extents.x + (archerAgent == null ? 0.5f : archerAgent.radius) + 0.1f);
                NavMeshQueryFilter guardFilter = new()
                {
                    agentTypeID = archerAgent == null ? 0 : archerAgent.agentTypeID,
                    areaMask = archerAgent == null ? NavMesh.AllAreas : archerAgent.areaMask,
                };
                anchor.position = NavMesh.SamplePosition(requestedAnchor, out NavMeshHit guardHit, 2f, guardFilter)
                    ? guardHit.position
                    : position;
                anchor.rotation = rotation;
                Defender guard = Instantiate(ArcherPrefab, anchor.position, rotation, SpawnRoot);
                guard.name = $"Fixed Archer {index + 1}";
                guard.Idle();
                ConfigureAttackRadius(guard, WallGuardAttackRadius);
                ConfigureMetadata(guard.gameObject, DinoTargetCategory.Defender, 201 + index);
                WallGuardPost post = wall.gameObject.AddComponent<WallGuardPost>();
                post.Bind(wall, guard, anchor);

                PlacedDefenseHandle handle = wall.gameObject.AddComponent<PlacedDefenseHandle>();
                handle.Configure(kind, index, wall, null, guard);
                Placed.Add(handle);
                registry.RegisterPlacedWall(wall);
                registry.RegisterPlacedGuard(guard);
                return true;
            }

            Defender prefab = kind == DefensePlacementKind.Archer ? ArcherPrefab : MagePrefab;
            Defender groundGuard = Instantiate(prefab, position, Quaternion.identity, SpawnRoot);
            groundGuard.name = $"Player {(kind == DefensePlacementKind.Archer ? "Archer" : "Mage")} {index + 1}";
            groundGuard.Idle();
            ConfigureMetadata(groundGuard.gameObject, DinoTargetCategory.Defender,
                kind == DefensePlacementKind.Archer ? 301 + index : 401 + index);
            PlacedDefenseHandle groundHandle = groundGuard.gameObject.AddComponent<PlacedDefenseHandle>();
            groundHandle.Configure(kind, index, null, groundGuard, null);
            Placed.Add(groundHandle);
            registry.RegisterPlacedGuard(groundGuard);
            return true;
        }

        private void TryRemoveAtPointer()
        {
            Ray ray = PlacementCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            RaycastHit[] hits = Physics.RaycastAll(ray, float.MaxValue, ~0, QueryTriggerInteraction.Ignore);
            PlacedDefenseHandle handle = hits.Select(hit => hit.collider.GetComponentInParent<PlacedDefenseHandle>())
                .FirstOrDefault(value => value != null);
            if (handle == null || !InventoryValue.TryReturn(handle.Kind, out DefenseInventoryState next))
            {
                return;
            }

            EnemyAIController registry = EnemyAIController.Instance;
            if (handle.Wall != null) registry?.UnregisterPlacedWall(handle.Wall);
            if (handle.GroundGuard != null) registry?.UnregisterPlacedGuard(handle.GroundGuard);
            if (handle.WallGuard != null)
            {
                registry?.UnregisterPlacedGuard(handle.WallGuard);
                Destroy(handle.WallGuard.gameObject);
            }
            Placed.Remove(handle);
            InventoryValue = next;
            Destroy(handle.gameObject);
            StateChanged?.Invoke();
        }

        private bool TryResolveLegalPosition(
            DefensePlacementKind kind,
            GameObject preview,
            Vector3 candidate,
            out Vector3 resolved,
            out string reason)
        {
            resolved = candidate;
            NavMeshAgent prefabAgent = GetPrefab(kind).GetComponent<NavMeshAgent>();
            NavMeshQueryFilter filter = new()
            {
                agentTypeID = prefabAgent == null ? 0 : prefabAgent.agentTypeID,
                areaMask = prefabAgent == null ? NavMesh.AllAreas : prefabAgent.areaMask,
            };
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, NavMeshSampleRadius, filter))
            {
                reason = "位置不在可行走地面";
                return false;
            }
            resolved = navHit.position;
            preview.transform.position = resolved;

            Bounds bounds = CalculateBounds(preview);
            if (bounds.min.x < DeploymentMin.x || bounds.max.x > DeploymentMax.x ||
                bounds.min.z < DeploymentMin.y || bounds.max.z > DeploymentMax.y)
            {
                reason = "单位必须完整位于矩形布防区内";
                return false;
            }
            Vector3 resolvedPosition = resolved;
            if (DeploymentZones.Any(zone => zone != null && zone.gameObject.activeInHierarchy && zone.Contains(resolvedPosition)))
            {
                reason = "布防区不能与恐龙部署区重叠";
                return false;
            }

            int hitCount = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, BlockingHits,
                preview.transform.rotation, ~GroundLayer.value, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < hitCount; index++)
            {
                Collider blocker = BlockingHits[index];
                if (blocker == null || blocker.transform.IsChildOf(preview.transform) ||
                    blocker.GetComponentInParent<DinoDeploymentZone>() != null)
                {
                    continue;
                }
                reason = $"与 {blocker.name} 重叠";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private Quaternion GetPlacementRotation(DefensePlacementKind kind)
        {
            if (kind != DefensePlacementKind.Wall || WallPrefab == null)
            {
                return Quaternion.identity;
            }
            Bounds bounds = CalculateBounds(WallPrefab.gameObject);
            return bounds.size.x >= bounds.size.z ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
        }

        private GameObject GetPrefab(DefensePlacementKind kind) => kind switch
        {
            DefensePlacementKind.Wall => WallPrefab == null ? null : WallPrefab.gameObject,
            DefensePlacementKind.Archer => ArcherPrefab == null ? null : ArcherPrefab.gameObject,
            DefensePlacementKind.Mage => MagePrefab == null ? null : MagePrefab.gameObject,
            _ => null,
        };

        private void RebuildGhost(DefensePlacementKind kind)
        {
            DestroyGhost();
            GameObject prefab = GetPrefab(kind);
            if (prefab == null) return;
            GameObject staging = new("Defense Placement Preview");
            staging.SetActive(false);
            Ghost = Instantiate(prefab, staging.transform);
            Ghost.name = $"{kind} Preview";
            foreach (Behaviour behaviour in Ghost.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
            foreach (Collider collider in Ghost.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Rigidbody body in Ghost.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
            foreach (Transform child in Ghost.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
            GhostRenderers = Ghost.GetComponentsInChildren<Renderer>(true);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            GhostMaterial = shader == null ? null : new Material(shader);
            foreach (Renderer renderer in GhostRenderers)
            {
                if (GhostMaterial != null) renderer.sharedMaterial = GhostMaterial;
            }
            staging.SetActive(true);
            Ghost.transform.SetParent(null, true);
            Destroy(staging);
        }

        private void DestroyGhost()
        {
            if (Ghost != null) Destroy(Ghost);
            if (GhostMaterial != null) Destroy(GhostMaterial);
            Ghost = null;
            GhostMaterial = null;
            GhostRenderers = Array.Empty<Renderer>();
        }

        private void SetGhostVisible(bool visible)
        {
            foreach (Renderer renderer in GhostRenderers)
                if (renderer != null) renderer.enabled = visible;
        }

        private void SetGhostColor(Color color)
        {
            if (GhostMaterial == null) return;
            if (GhostMaterial.HasProperty("_BaseColor")) GhostMaterial.SetColor("_BaseColor", color);
            if (GhostMaterial.HasProperty("_Color")) GhostMaterial.SetColor("_Color", color);
        }

        private void CreateAreaVisual()
        {
            AreaVisual = new GameObject("Defense Deployment Area");
            AreaVisual.transform.SetParent(transform, false);
            LineRenderer line = AreaVisual.AddComponent<LineRenderer>();
            line.loop = true;
            line.positionCount = 4;
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = 0.18f;
            line.SetPositions(new[]
            {
                new Vector3(DeploymentMin.x, 0.15f, DeploymentMin.y),
                new Vector3(DeploymentMax.x, 0.15f, DeploymentMin.y),
                new Vector3(DeploymentMax.x, 0.15f, DeploymentMax.y),
                new Vector3(DeploymentMin.x, 0.15f, DeploymentMax.y),
            });
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                AreaLineMaterial = new Material(shader);
                AreaLineMaterial.color = new Color(0.15f, 0.7f, 1f, 0.8f);
                line.sharedMaterial = AreaLineMaterial;
            }

            GameObject fillObject = new("Translucent Fill", typeof(MeshFilter), typeof(MeshRenderer));
            fillObject.transform.SetParent(AreaVisual.transform, false);
            Mesh mesh = new() { name = "Defense Deployment Rectangle" };
            mesh.vertices = new[]
            {
                new Vector3(DeploymentMin.x, 0.04f, DeploymentMin.y),
                new Vector3(DeploymentMax.x, 0.04f, DeploymentMin.y),
                new Vector3(DeploymentMax.x, 0.04f, DeploymentMax.y),
                new Vector3(DeploymentMin.x, 0.04f, DeploymentMax.y),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            fillObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            Shader fillShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
            if (fillShader != null)
            {
                AreaFillMaterial = new Material(fillShader);
                ConfigureTransparentMaterial(AreaFillMaterial);
                Color fillColor = new(0.05f, 0.42f, 1f, 0.14f);
                AreaFillMaterial.color = fillColor;
                if (AreaFillMaterial.HasProperty("_BaseColor"))
                    AreaFillMaterial.SetColor("_BaseColor", fillColor);
                fillObject.GetComponent<MeshRenderer>().sharedMaterial = AreaFillMaterial;
            }
        }

        private static void ConfigureTransparentMaterial(Material material)
        {
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static void ConfigureMetadata(GameObject target, DinoTargetCategory category, int stableId)
        {
            DinoTargetMetadata metadata = target.GetComponent<DinoTargetMetadata>() ??
                                          target.AddComponent<DinoTargetMetadata>();
            metadata.Configure(category, stableId);
        }

        private static void ConfigureAttackRadius(Defender guard, float radius)
        {
            AttackRadius attackRadius = guard.GetComponentInChildren<AttackRadius>(true);
            if (attackRadius != null) attackRadius.ConfigureRadius(radius);
        }

        private static DefensePlacementToken ToToken(PlacedDefenseHandle handle)
        {
            Transform transform = handle.transform;
            return new DefensePlacementToken(handle.Kind, handle.StableIndex,
                transform.position.x, transform.position.y, transform.position.z, transform.eulerAngles.y);
        }

        private void OnDestroy()
        {
            DestroyGhost();
            if (AreaLineMaterial != null) Destroy(AreaLineMaterial);
            if (AreaFillMaterial != null) Destroy(AreaFillMaterial);
        }
    }
}
