using System.Collections;
using LlamAcademy.Dinos.Unit;
using LlamAcademy.Dinos.Simulation;
using UnityEngine;
using UnityEngine.AI;

namespace LlamAcademy.Dinos.Config
{
    [CreateAssetMenu(fileName = "Auto Attack", menuName = "AI/Attacks/Snaring Attack")]
    public class SnareAttackSO : DamagingAttackSO
    {
        [field: SerializeField] public float SnareDuration { get; private set; } = 5f;

        public override void ApplyEffect(IDamageable damageable)
        {
            base.ApplyEffect(damageable);

            if (damageable.Transform.TryGetComponent(out NavMeshAgent agent))
            {
                DinoFixedSnare snare = agent.GetComponent<DinoFixedSnare>();
                if (snare == null)
                {
                    snare = agent.gameObject.AddComponent<DinoFixedSnare>();
                }
                snare.Apply(SnareDuration);
            }
        }
    }
}
