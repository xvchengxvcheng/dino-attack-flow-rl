using LlamAcademy.Dinos.Unit;
using LlamAcademy.Dinos.Utility;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Enemy.Defense
{
    [DisallowMultipleComponent]
    public sealed class WallGuardPost : MonoBehaviour
    {
        private Wall BoundWall;
        private Defender BoundGuard;
        private Transform Anchor;
        private Transform HealthBarAnchor;
        private bool VisualPlacementPending;
        private bool HealthBarBindingPending;

        public Wall Wall => BoundWall;
        public Defender Guard => BoundGuard;

        public void Bind(Wall wall, Defender guard, Transform anchor)
        {
            Unbind();
            BoundWall = wall;
            BoundGuard = guard;
            Anchor = anchor;
            if (BoundWall != null)
            {
                BoundWall.OnDeath += HandleWallDeath;
            }
            VisualPlacementPending = true;
        }

        private bool TryPlaceGuardVisualOnWallTop()
        {
            if (BoundWall == null || BoundGuard == null)
            {
                return false;
            }

            Renderer[] wallRenderers = BoundWall.GetComponentsInChildren<Renderer>(true);
            Renderer[] guardRenderers = BoundGuard.GetComponentsInChildren<Renderer>(true);
            if (wallRenderers.Length == 0 || guardRenderers.Length == 0)
            {
                return false;
            }

            Bounds wallBounds = wallRenderers[0].bounds;
            for (int index = 1; index < wallRenderers.Length; index++)
            {
                wallBounds.Encapsulate(wallRenderers[index].bounds);
            }

            Bounds guardBounds = guardRenderers[0].bounds;
            for (int index = 1; index < guardRenderers.Length; index++)
            {
                guardBounds.Encapsulate(guardRenderers[index].bounds);
            }

            // Leave animation clearance above the parapet so feet/weapon do not dip
            // back into the wall when the idle and attack clips update their bounds.
            List<Transform> visualRoots = new();
            foreach (Transform child in BoundGuard.transform)
            {
                if (child.GetComponentsInChildren<Renderer>(true).Length > 0)
                {
                    visualRoots.Add(child);
                }
            }

            if (visualRoots.Count == 0)
            {
                return false;
            }

            Transform visualContainer = new GameObject("GuardVisualElevation").transform;
            visualContainer.SetParent(BoundGuard.transform, false);
            foreach (Transform visualRoot in visualRoots)
            {
                visualRoot.SetParent(visualContainer, true);
            }

            Vector3 desired = new(wallBounds.center.x, wallBounds.max.y + 0.35f, wallBounds.center.z);
            Vector3 current = new(guardBounds.center.x, guardBounds.min.y, guardBounds.center.z);
            visualContainer.position += desired - current;

            guardBounds = guardRenderers[0].bounds;
            for (int index = 1; index < guardRenderers.Length; index++)
            {
                guardBounds.Encapsulate(guardRenderers[index].bounds);
            }

            HealthBarAnchor = new GameObject("HealthBarAnchor").transform;
            HealthBarAnchor.SetParent(visualContainer, true);
            HealthBarAnchor.position = new Vector3(
                guardBounds.center.x,
                guardBounds.max.y + 0.5f,
                guardBounds.center.z);
            HealthBarBindingPending = true;

            return true;
        }

        private void LateUpdate()
        {
            if (BoundGuard == null || Anchor == null)
            {
                return;
            }

            if (VisualPlacementPending && TryPlaceGuardVisualOnWallTop())
            {
                VisualPlacementPending = false;
            }

            if (HealthBarBindingPending && HealthBarCanvas.Instance != null &&
                HealthBarCanvas.Instance.SetFollowTarget(BoundGuard, HealthBarAnchor))
            {
                HealthBarBindingPending = false;
            }

            NavMeshAgent agent = BoundGuard.Agent;
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                if (agent.hasPath)
                {
                    agent.ResetPath();
                }
                agent.isStopped = true;
                if ((BoundGuard.transform.position - Anchor.position).sqrMagnitude > 0.01f)
                {
                    agent.Warp(Anchor.position);
                }
            }
            else
            {
                BoundGuard.transform.SetPositionAndRotation(Anchor.position, Anchor.rotation);
            }
        }

        private void HandleWallDeath(IDamageable damageable)
        {
            if (BoundGuard != null && BoundGuard.Health > 0)
            {
                BoundGuard.TakeDamage(int.MaxValue);
            }
            Unbind();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Unbind()
        {
            if (BoundWall != null)
            {
                BoundWall.OnDeath -= HandleWallDeath;
            }
            BoundWall = null;
            HealthBarAnchor = null;
            VisualPlacementPending = false;
            HealthBarBindingPending = false;
        }
    }
}
