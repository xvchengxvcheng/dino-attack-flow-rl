using System;
using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Simulation;
using LlamAcademy.Dinos.Unit;
using LlamAcademy.Dinos.Utility;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;
using Action = Unity.Behavior.Action;

namespace LlamAcademy.Dinos.Behavior
{
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name: "Attack Closest Object", story: "[Self] attacks [ClosestAttackable] until it dies.",
        category: "Action", id: "693534fc8c8f928bfb1b911de9ac3cf5")]
    public partial class AttackClosestObjectAction : Action
    {
        [SerializeReference] public BlackboardVariable<GameObject> Self;
        [SerializeReference] public BlackboardVariable<GameObject> ClosestAttackable;
        [SerializeReference] public BlackboardVariable<AttackConfigSO> AttackConfig;
        [SerializeReference] public BlackboardVariable<float> RotationSpeed = new(5);
        [SerializeReference] public BlackboardVariable<float> LastAttackTime;
        [SerializeReference] public BlackboardVariable<AttackEventChannel> AttackEventChannel;
        [SerializeReference] public BlackboardVariable<DeathEventChannel> DeathEventChannel;

        private Transform Transform;
        private Vector3 TargetPosition;
        private IDamageable Damageable;
        private Collider DamageableCollider;
        private Animator Animator;
        private readonly Dictionary<AttackTypeSO, long> AttackTimes = new();
        private readonly List<PendingEffect> PendingEffects = new();

        private sealed class PendingEffect
        {
            public AttackTypeSO Attack;
            public IDamageable Damageable;
            public ObjectPool<ParticleSystem> ParticlePool;
            public long DueTick;
            public bool SpawnImpactParticle;
        }

        protected override Status OnStart()
        {
            if (AttackConfig.Value == null || Self.Value == null || ClosestAttackable.Value == null ||
                !ClosestAttackable.Value.TryGetComponent(out Damageable) || !TargetIsAlive())
            {
                return Status.Failure;
            }

            Transform = Self.Value.transform;
            TargetPosition = GetTargetPosition();
            Animator = Self.Value.GetComponent<Animator>();

            foreach (AttackTypeSO attackType in AttackConfig.Value.AttackTypes)
            {
                AttackTimes.TryAdd(attackType, 0);
            }
            PendingEffects.Clear();

            return Status.Running;
        }

        private Vector3 GetTargetPosition()
        {
            DamageableCollider = ClosestAttackable.Value.GetComponentInChildren<Collider>();

            if (DamageableCollider != null)
            {
                return DamageableCollider.ClosestPoint(Transform.position);
            }

            return ClosestAttackable.Value.transform.position;
        }

        protected override Status OnUpdate()
        {
            long currentTick = DinoFixedStepTime.CurrentTick;
            ApplyPendingEffects(currentTick);
            if (!TargetIsAlive()) return Status.Failure;

            TargetPosition = GetTargetPosition();

            AttackTypeSO attack = AttackTimes
                .Where(candidate => candidate.Value + DinoFixedStepTime.SecondsToTicks(
                    AttackConfig.Value.GetAttackDelay(candidate.Key)) <= currentTick)
                .OrderBy(attack => attack.Key.Priority)
                .FirstOrDefault().Key;

            // keep rotating even if we're early aborting
            Vector3 lookDirection = TargetPosition - Transform.position;
            lookDirection.y = 0;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion lookRotation = Quaternion.LookRotation(lookDirection.normalized);
                Transform.rotation = Quaternion.Slerp(
                    Transform.rotation,
                    lookRotation,
                    Time.fixedDeltaTime * RotationSpeed);
            }

            if (attack == null) return Status.Running; // early abort if no attacks can be used

            if (Animator != null)
            {
                Animator.SetTrigger(AnimationConstants.IS_ATTACKING_PARAMETER);
            }

            LastAttackTime.Value = currentTick * Time.fixedDeltaTime;
            AttackTimes[attack] = currentTick;

            if (AttackEventChannel.Value != null)
            {
                AttackEventChannel.Value.SendEventMessage(Self.Value, Damageable.Transform.gameObject);
            }

            ObjectPool<ParticleSystem> particlePool = ParticleSystemHelper.Instance.GetParticleSystemPoolFor(attack);

            if (particlePool == null)
            {
                if (attack.DelayDamage)
                {
                    ScheduleEffect(attack, Damageable, null, attack.DelayDamageTime, false);
                }
                else
                {
                    ApplyDamage(attack, Damageable);
                }
            }

            if (particlePool != null)
            {
                if (attack.AttackParticleMoveConfig.MoveToTarget)
                {
                    ParticleSystem system = particlePool.Get();
                    system.transform.position = TargetPosition;
                    system.transform.LookAt(Transform.position + Vector3.up);
                    system.transform.position = Transform.position + attack.AttackParticleMoveConfig.LocalSpawnOffset;
                    // This will delay call attack.ApplyEffect
                    ParticleSystemHelper.Instance.MoveParticleSystemToTargetVisual(
                        Damageable,
                        system,
                        attack);
                    ScheduleEffect(
                        attack,
                        Damageable,
                        null,
                        attack.AttackParticleMoveConfig.MoveToTargetDelay +
                        attack.AttackParticleMoveConfig.MoveToTargetTime,
                        false);
                    ParticleSystemHelper.Instance.ReAddSystemToPoolOnDisable(particlePool, system);
                }
                else if (attack.DelayDamage)
                {
                    ScheduleEffect(
                        attack,
                        Damageable,
                        particlePool,
                        attack.DelayDamageTime,
                        true);
                }
                else
                {
                    ApplyDamage(attack, Damageable, particlePool);
                }
            }

            if (!DamageableState.IsAlive(Damageable))
            {
                if (DeathEventChannel.Value != null)
                {
                    DeathEventChannel.Value.SendEventMessage(Self.Value, ClosestAttackable);
                }
                return Status.Success;
            }

            return Status.Running;
        }

        protected override void OnEnd() => PendingEffects.Clear();

        private void ApplyDamage(AttackTypeSO attack, IDamageable damageable, ObjectPool<ParticleSystem> particlePool = null)
        {
            if (!DamageableState.IsAlive(damageable)) return;

            if (particlePool != null)
            {
                ParticleSystem system = particlePool.Get();
                system.transform.position = TargetPosition;
                system.transform.LookAt(Transform.position + Vector3.up);
                ParticleSystemHelper.Instance.ReAddSystemToPoolOnDisable(particlePool, system);
            }

            TryApplyEffect(attack, damageable);
        }

        private void ScheduleEffect(
            AttackTypeSO attack,
            IDamageable damageable,
            ObjectPool<ParticleSystem> particlePool,
            float delaySeconds,
            bool spawnImpactParticle)
        {
            long delayTicks = DinoFixedStepTime.SecondsToTicks(delaySeconds);
            if (delayTicks == 0L)
            {
                ApplyScheduledEffect(attack, damageable, particlePool, spawnImpactParticle);
                return;
            }

            PendingEffects.Add(new PendingEffect
            {
                Attack = attack,
                Damageable = damageable,
                ParticlePool = particlePool,
                DueTick = DinoFixedStepTime.CurrentTick + delayTicks,
                SpawnImpactParticle = spawnImpactParticle
            });
        }

        private void ApplyPendingEffects(long currentTick)
        {
            for (int index = PendingEffects.Count - 1; index >= 0; index--)
            {
                PendingEffect pending = PendingEffects[index];
                if (currentTick < pending.DueTick)
                {
                    continue;
                }

                PendingEffects.RemoveAt(index);
                ApplyScheduledEffect(
                    pending.Attack,
                    pending.Damageable,
                    pending.ParticlePool,
                    pending.SpawnImpactParticle);
            }
        }

        private void ApplyScheduledEffect(
            AttackTypeSO attack,
            IDamageable damageable,
            ObjectPool<ParticleSystem> particlePool,
            bool spawnImpactParticle)
        {
            if (!DamageableState.IsAlive(damageable))
            {
                return;
            }

            if (spawnImpactParticle)
            {
                ApplyDamage(attack, damageable, particlePool);
                return;
            }

            TryApplyEffect(attack, damageable);
        }

        private static bool TryApplyEffect(AttackTypeSO attack, IDamageable damageable)
        {
            if (attack == null || !DamageableState.IsAlive(damageable))
            {
                return false;
            }

            attack.ApplyEffect(damageable);
            return true;
        }

        private bool TargetIsAlive()
        {
            return ClosestAttackable.Value != null && DamageableState.IsAlive(Damageable);
        }
    }
}
