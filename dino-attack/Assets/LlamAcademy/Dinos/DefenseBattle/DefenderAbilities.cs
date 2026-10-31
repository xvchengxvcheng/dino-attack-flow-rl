using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Unit;
using UnityEngine;

namespace LlamAcademy.Dinos.DefenseBattle
{
    [DefaultExecutionOrder(70), DisallowMultipleComponent]
    public sealed class DefenderAbilities : MonoBehaviour
    {
        [SerializeField] private FirstPersonDefender Player;
        [SerializeField] private Transform BurstVisual;
        [SerializeField] private Transform ShieldVisual;
        private readonly Collider[] Hits = new Collider[128];
        private readonly HashSet<Dino> Damaged = new();
        private float NextBurst, NextShield, NextDash, ShieldUntil, DashUntil, BurstUntil;
        private int ShieldPoints;
        private Vector3 DashDirection;
        private Vector3 BurstPosition;
        private float Clock => Player != null ? Player.ActionElapsedSeconds : 0f;
        public float BurstCooldownRemaining => Mathf.Max(0f, NextBurst - Clock);
        public float ShieldCooldownRemaining => Mathf.Max(0f, NextShield - Clock);
        public float DashCooldownRemaining => Mathf.Max(0f, NextDash - Clock);
        public int ShieldRemaining => Player.IsControlling && Clock < ShieldUntil ? ShieldPoints : 0;
        public bool IsDashing => Player.IsControlling && Clock < DashUntil;

        private void Awake()
        {
            if (Player == null) Player = GetComponent<FirstPersonDefender>();
            if (BurstVisual != null) BurstVisual.gameObject.SetActive(false);
            if (ShieldVisual != null) ShieldVisual.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (BurstVisual != null)
            {
                bool visible = Player.IsControlling && Clock < BurstUntil;
                BurstVisual.gameObject.SetActive(visible);
                if (visible) BurstVisual.position = BurstPosition;
                if (visible) BurstVisual.localScale = Vector3.one * Mathf.Lerp(14f, 1f, Mathf.Clamp01((BurstUntil - Clock) / 0.5f));
            }
            if (ShieldVisual != null)
            {
                ShieldVisual.gameObject.SetActive(ShieldRemaining > 0);
                ShieldVisual.Rotate(0f, 35f * Time.deltaTime, 0f);
            }
            if (IsDashing) Player.MoveDash(DashDirection * (24f * Time.deltaTime));
        }

        public bool TryBurst()
        {
            if (!Player.IsControlling || BurstCooldownRemaining > 0f ||
                !Player.TryGetAimPoint(30f, out Vector3 center)) return false;
            NextBurst = Clock + 7f;
            BurstUntil = Clock + 0.5f;
            BurstPosition = center;
            if (BurstVisual != null) BurstVisual.position = center;
            int count = Physics.OverlapSphereNonAlloc(center, 7f, Hits, ~0, QueryTriggerInteraction.Ignore);
            Damaged.Clear();
            // Saturation fails closed instead of producing order-dependent partial area damage.
            if (count < Hits.Length)
                for (int i = 0; i < count; i++)
                {
                    Dino dino = Hits[i].GetComponentInParent<Dino>();
                    if (dino == null || dino.IsDead || !RoundManager.Instance.ActiveDinoUnits.Contains(dino) ||
                        Damaged.Contains(dino) || !Player.HasLineOfSight(center, Hits[i])) continue;
                    Damaged.Add(dino);
                    Player.DealAttackDamage(dino, 55, Hits[i], true);
                }
            Player.PlayCastAnimation();
            return true;
        }

        public bool TryShield()
        {
            if (!Player.IsControlling || ShieldCooldownRemaining > 0f) return false;
            NextShield = Clock + 10f;
            ShieldUntil = Clock + 4f;
            ShieldPoints = 60;
            return true;
        }

        public bool TryDash()
        {
            if (!Player.IsControlling || DashCooldownRemaining > 0f) return false;
            NextDash = Clock + 6f;
            DashUntil = Clock + 0.22f;
            DashDirection = Player.LastMoveDirection.sqrMagnitude > 0.01f ? Player.LastMoveDirection : Player.transform.forward;
            return true;
        }

        public int AbsorbDamage(int amount)
        {
            if (amount <= 0) return 0;
            int absorbed = Mathf.Min(ShieldRemaining, amount);
            ShieldPoints -= absorbed;
            return amount - absorbed;
        }
    }
}
