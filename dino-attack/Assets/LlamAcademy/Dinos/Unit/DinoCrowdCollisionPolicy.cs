using UnityEngine;

namespace LlamAcademy.Dinos.Unit
{
    public static class DinoCrowdCollisionPolicy
    {
        public static void IgnoreMutualBodyCollisions(Collider[] first, Collider[] second)
        {
            if (first == null || second == null) return;

            foreach (Collider firstCollider in first)
            foreach (Collider secondCollider in second)
            {
                if (firstCollider == null || secondCollider == null ||
                    firstCollider.isTrigger || secondCollider.isTrigger)
                {
                    continue;
                }

                Physics.IgnoreCollision(firstCollider, secondCollider, true);
            }
        }
    }
}
