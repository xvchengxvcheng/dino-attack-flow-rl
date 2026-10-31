using UnityEngine;
using UnityEngine.InputSystem;

namespace LlamAcademy.Dinos.DefenseBattle
{
    [DefaultExecutionOrder(90), DisallowMultipleComponent]
    public sealed class ThirdPersonFollowCamera : MonoBehaviour
    {
        [SerializeField] private FirstPersonDefender Player;
        [SerializeField] private float Distance = 5.8f;
        private readonly RaycastHit[] Hits = new RaycastHit[128];
        private bool Initialized;
        public float Yaw { get; private set; }
        public float Pitch { get; private set; } = 12f;
        public Quaternion PlanarRotation => Quaternion.Euler(0f, Yaw, 0f);

        public void ResetView()
        {
            Yaw = Player.transform.eulerAngles.y;
            Pitch = 12f;
            Initialized = false;
            Refresh(0f);
        }

        public void ApplyLook(Vector2 delta, float sensitivity)
        {
            if (!float.IsFinite(delta.x) || !float.IsFinite(delta.y)) return;
            Yaw = Mathf.Repeat(Yaw + Mathf.Clamp(delta.x, -2000f, 2000f) * sensitivity, 360f);
            Pitch = Mathf.Clamp(Pitch - delta.y * sensitivity, -25f, 65f);
        }

        private void LateUpdate()
        {
            if (Player == null || !Player.IsControlling) return;
            if (Player.IsLookCaptured && Mouse.current != null)
                Distance = Mathf.Clamp(Distance - Mouse.current.scroll.y.ReadValue() * 0.005f, 3.5f, 8.5f);
            Refresh(Time.unscaledDeltaTime);
        }

        public void Refresh(float deltaTime)
        {
            if (Player == null) return;
            Vector3 pivot = Player.transform.position + Vector3.up * 2f;
            Quaternion rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            Vector3 offset = rotation * new Vector3(0.7f, 0f, -Distance);
            Vector3 desired = pivot + offset;
            float length = offset.magnitude;
            int count = Physics.SphereCastNonAlloc(pivot, 0.24f, offset / length, Hits, length,
                ~0, QueryTriggerInteraction.Ignore);
            float clearDistance = count == Hits.Length ? 0.35f : length;
            for (int i = 0; i < count; i++)
                if (Hits[i].collider != null && !Hits[i].transform.IsChildOf(Player.transform))
                    clearDistance = Mathf.Min(clearDistance, Mathf.Max(0.35f, Hits[i].distance - 0.12f));
            bool blocked = clearDistance < length;
            if (blocked) desired = pivot + offset.normalized * clearDistance;
            // Obstructions retract immediately; camera recovery and translation use wall-clock smoothing.
            transform.position = !Initialized || blocked ? desired
                : Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-18f * Mathf.Max(0f, deltaTime)));
            transform.rotation = rotation;
            Player.SetBodyVisible(Vector3.Distance(pivot, transform.position) > 1.5f);
            Initialized = true;
        }
    }
}
