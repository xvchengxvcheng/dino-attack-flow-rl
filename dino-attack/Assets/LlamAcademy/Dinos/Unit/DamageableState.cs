using UnityEngine;

namespace LlamAcademy.Dinos.Unit
{
    public static class DamageableState
    {
        public static bool IsAlive(IDamageable target)
        {
            if (target == null)
            {
                return false;
            }

            if (target is Object unityObject && unityObject == null)
            {
                return false;
            }

            Transform targetTransform = target.Transform;
            return targetTransform != null
                && targetTransform.gameObject.activeInHierarchy
                && target.Health > 0;
        }
    }
}
