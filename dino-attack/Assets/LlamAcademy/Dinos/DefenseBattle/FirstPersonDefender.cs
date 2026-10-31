using System;
using System.Linq;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Unit;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace LlamAcademy.Dinos.DefenseBattle
{
    // An extra local combatant; deliberately not registered in the trained eleven-guard observation set.
    [DefaultExecutionOrder(60), RequireComponent(typeof(CharacterController)), DisallowMultipleComponent]
    public sealed class FirstPersonDefender : MonoBehaviour, IDamageable
    {
        [SerializeField] private DefenseBattleCoordinator Coordinator;
        [SerializeField] private GameObject CharacterModel;
        // Serialized name retained for the existing Map 4 scene. This camera now follows in third person.
        [SerializeField] private Camera FirstPersonCamera;
        [SerializeField] private Camera StrategyCamera;
        [SerializeField] private UnitSO DamageType;
        [SerializeField] private Transform Staff;
        [SerializeField] private LineRenderer SpellTrail;
        [SerializeField] private Vector3 SpawnPosition = new(-2f, 0f, -43f);
        [SerializeField] private Bounds WalkBounds = new(Vector3.zero, new Vector3(100f, 100f, 110f));
        [SerializeField] private float WalkSpeed = 7f;
        [SerializeField] private float SprintSpeed = 11f;
        [SerializeField] private float LookSensitivity = 0.12f;
        [SerializeField] private int SpellDamage = 25;
        [SerializeField] private float SpellRange = 55f;
        [SerializeField] private float SpellCooldown = 0.45f;

        private readonly Collider[] SpawnHits = new Collider[128];
        private readonly RaycastHit[] ShotHits = new RaycastHit[128];
        private CharacterController Motor;
        private Renderer[] BodyRenderers;
        private CameraControl[] StrategyControls;
        private AudioListener StrategyListener;
        private bool StartedBattle;
        private bool DemoMode;
        private bool OwnsCamera;
        private bool LookCaptured;
        private float VerticalSpeed;
        private float NextSpellTime;
        private float TrailUntil;
        private bool Dead;
        private bool BodyVisible = true;
        // Dialogue owns the keyboard and pointer while the player can still be in combat.
        // Keep this separate from IsControlling so the combat HUD remains visible.
        private bool DialogueInputBlocked;
        private bool PersistentInteractionMode;
        private bool InteractionPointerOverUi;
        private bool PrimaryPressSuppressed;
        private bool SecondaryPressSuppressed;
        private ThirdPersonFollowCamera FollowCamera;
        private DefenderAbilities Abilities;
        private Animator CharacterAnimator;
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int CastParameter = Animator.StringToHash("Cast");
        public Vector3 LastMoveDirection { get; private set; }

        public int MaxHealth { get; set; } = 120;
        public int Health { get; set; } = 120;
        public Transform Transform => transform;
        public UnitSO UnitType => DamageType;
        public float Pitch { get; private set; }
        public bool IsControlling => OwnsCamera && !Dead &&
                                     (DemoMode || RoundManager.Instance != null &&
                                      RoundManager.Instance.State == GameState.Running);
        // The village intentionally stays in Setup, so its round clock never advances.
        // Use scaled game time there; formal battles retain their fixed-step round clock.
        internal float ActionElapsedSeconds => DemoMode ? Time.time :
            RoundManager.Instance != null ? RoundManager.Instance.RunningElapsedSeconds : 0f;
        public bool BlockCombatInput => DialogueInputBlocked;
        public bool IsLookCaptured => LookCaptured;
        public bool PersistentInteractionEnabled => PersistentInteractionMode;
        public bool IsKnockedOut => Dead;
        public string StatusMessage { get; private set; } = string.Empty;
        public float HitFlashUntil { get; private set; }
        public float DamageFlashUntil { get; private set; }
        public event IDamageable.TakeDamageEvent OnTakeDamage;
        public event IDamageable.DeathEvent OnDeath;
        // Actual HP lost, a captured world position, and whether this was an area skill.
        public event Action<int, Vector3, bool> AttackDamageDealt;

        private void Awake()
        {
            Motor = GetComponent<CharacterController>();
            Motor.enabled = false;
            if (Coordinator == null || CharacterModel == null || FirstPersonCamera == null ||
                StrategyCamera == null || DamageType == null)
            {
                Debug.LogError("Map 4 defender is missing required scene references.", this);
                enabled = false;
                return;
            }
            BodyRenderers = CharacterModel.GetComponentsInChildren<Renderer>(true);
            FollowCamera = FirstPersonCamera.GetComponent<ThirdPersonFollowCamera>();
            Abilities = GetComponent<DefenderAbilities>();
            CharacterAnimator = CharacterModel.GetComponentInChildren<Animator>(true);
            StrategyControls = FindObjectsByType<CameraControl>(FindObjectsSortMode.None);
            StrategyListener = StrategyCamera.GetComponent<AudioListener>();
            CharacterModel.SetActive(false);
            FirstPersonCamera.gameObject.SetActive(false);
            if (SpellTrail != null) SpellTrail.enabled = false;
        }

        private void Update()
        {
            if (RoundManager.Instance == null) return;
            bool running = RoundManager.Instance.State == GameState.Running;
            if (DemoMode && !StartedBattle)
            {
                StartedBattle = true;
                BeginBattle();
            }
            else if (!DemoMode && running && !StartedBattle)
            {
                StartedBattle = true;
                BeginBattle();
            }
            if (!DemoMode && !running && OwnsCamera) ReleaseView();
            if (SpellTrail != null && Time.unscaledTime >= TrailUntil) SpellTrail.enabled = false;
            if (!IsControlling) return;

            if (DialogueInputBlocked)
            {
                // Do not let a key held before opening dialogue leak into gameplay.
                LastMoveDirection = Vector3.zero;
                if (PersistentInteractionMode)
                    SimulateMovement(Vector2.zero, false, false, Time.deltaTime);
                return;
            }

            if (PersistentInteractionMode)
            {
                UpdatePersistentInteraction();
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetLookCaptured(false);
            if (!LookCaptured)
            {
                if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
                    SetLookCaptured(true);
                SimulateMovement(Vector2.zero, false, false, Time.deltaTime);
                return;
            }
            if (Mouse.current != null)
            {
                ApplyLook(Mouse.current.delta.ReadValue());
                if (Mouse.current.leftButton.isPressed) TryCast();
            }
            if (Keyboard.current != null)
            {
                Keyboard key = Keyboard.current;
                Vector2 move = new((key.dKey.isPressed ? 1 : 0) - (key.aKey.isPressed ? 1 : 0),
                    (key.wKey.isPressed ? 1 : 0) - (key.sKey.isPressed ? 1 : 0));
                SimulateMovement(move, key.leftShiftKey.isPressed, key.spaceKey.wasPressedThisFrame, Time.deltaTime);
                if (Abilities != null)
                {
                    if (key.qKey.wasPressedThisFrame) Abilities.TryBurst();
                    if (key.eKey.wasPressedThisFrame) Abilities.TryShield();
                    if (key.rKey.wasPressedThisFrame) Abilities.TryDash();
                }
            }
        }

        private void BeginBattle()
        {
            if (!TryFindSpawn(out Vector3 position))
            {
                StatusMessage = "附近没有安全出生点，本局进入观战";
                Debug.LogWarning(StatusMessage, this);
                return;
            }
            transform.SetPositionAndRotation(position, Quaternion.identity);
            Health = MaxHealth;
            CharacterModel.SetActive(true);
            // The imported character remains visible in the following camera.
            foreach (Renderer body in BodyRenderers) body.shadowCastingMode = ShadowCastingMode.On;
            Motor.enabled = true;
            StrategyCamera.enabled = false;
            StrategyCamera.tag = "Untagged";
            if (StrategyListener != null) StrategyListener.enabled = false;
            foreach (CameraControl control in StrategyControls) control.enabled = false;
            FirstPersonCamera.gameObject.SetActive(true);
            FirstPersonCamera.tag = "MainCamera";
            OwnsCamera = true;
            FollowCamera?.ResetView();
            SetLookCaptured(true);
        }

        /// <summary>
        /// Starts the player camera and third-person controller while the demo
        /// scene intentionally keeps the normal round manager in Setup state.
        /// </summary>
        public void BeginDemoMode()
        {
            DemoMode = true;
            StartedBattle = true;
            BeginBattle();
        }

        private bool TryFindSpawn(out Vector3 position)
        {
            Physics.SyncTransforms();
            for (int ring = 0; ring <= 10; ring++)
            {
                int count = ring == 0 ? 1 : 16;
                for (int index = 0; index < count; index++)
                {
                    float angle = index * Mathf.PI * 2f / count;
                    Vector3 candidate = SpawnPosition + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (ring * 2f);
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit nav, 1.5f, NavMesh.AllAreas)) continue;
                    candidate = nav.position + Vector3.up * 0.08f;
                    if (!WithinBounds(candidate)) continue;
                    float radius = Motor.radius + 0.08f;
                    int found = Physics.OverlapCapsuleNonAlloc(candidate + Vector3.up * (radius + 0.12f),
                        candidate + Vector3.up * (Motor.height - radius), radius, SpawnHits,
                        ~0, QueryTriggerInteraction.Ignore);
                    if (found == SpawnHits.Length) continue;
                    bool blocked = false;
                    for (int hit = 0; hit < found; hit++)
                        if (SpawnHits[hit] != null && !SpawnHits[hit].transform.IsChildOf(transform)) blocked = true;
                    if (blocked) continue;
                    position = candidate;
                    return true;
                }
            }
            position = default;
            return false;
        }

        public void ApplyLook(Vector2 delta)
        {
            if (!IsControlling || DialogueInputBlocked || !float.IsFinite(delta.x) || !float.IsFinite(delta.y)) return;
            if (FollowCamera == null) return;
            FollowCamera.ApplyLook(delta, LookSensitivity);
            Pitch = FollowCamera.Pitch;
        }

        public void SimulateMovement(Vector2 move, bool sprint, bool jump, float deltaTime)
        {
            if (!IsControlling || !Motor.enabled || !float.IsFinite(move.x) || !float.IsFinite(move.y) ||
                !float.IsFinite(deltaTime) || deltaTime <= 0f) return;
            float dt = Mathf.Min(deltaTime, 0.05f);
            if (DialogueInputBlocked)
            {
                LastMoveDirection = Vector3.zero;
                if (Motor.isGrounded && VerticalSpeed < 0f) VerticalSpeed = -2f;
                VerticalSpeed = Mathf.Max(-30f, VerticalSpeed - 22f * dt);
                Motor.Move(Vector3.up * (VerticalSpeed * dt));
                if (CharacterAnimator != null)
                    CharacterAnimator.SetFloat(SpeedParameter, 0f, 0.1f, dt);
                return;
            }
            move = Vector2.ClampMagnitude(move, 1f);
            if (Motor.isGrounded && VerticalSpeed < 0f) VerticalSpeed = -2f;
            if (jump && Motor.isGrounded) VerticalSpeed = 7f;
            VerticalSpeed = Mathf.Max(-30f, VerticalSpeed - 22f * dt);
            Quaternion planar = FollowCamera != null ? FollowCamera.PlanarRotation : transform.rotation;
            Vector3 direction = planar * new Vector3(move.x, 0f, move.y);
            LastMoveDirection = direction;
            if (move.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 720f * dt);
            Vector3 horizontal = direction * (sprint ? SprintSpeed : WalkSpeed) * dt;
            if (Abilities != null && Abilities.IsDashing) horizontal = Vector3.zero;
            Vector3 desired = transform.position + horizontal;
            desired.x = Mathf.Clamp(desired.x, WalkBounds.min.x + Motor.radius, WalkBounds.max.x - Motor.radius);
            desired.z = Mathf.Clamp(desired.z, WalkBounds.min.z + Motor.radius, WalkBounds.max.z - Motor.radius);
            Vector3 displacement = desired - transform.position;
            displacement.y = VerticalSpeed * dt;
            Vector3 before = transform.position;
            Motor.Move(displacement);
            Vector3 travelled = transform.position - before;
            travelled.y = 0f;
            if (CharacterAnimator != null)
                CharacterAnimator.SetFloat(SpeedParameter, Mathf.Min(SprintSpeed / WalkSpeed,
                    travelled.magnitude / Mathf.Max(0.0001f, dt * WalkSpeed)), 0.1f, dt);
        }

        private bool WithinBounds(Vector3 point) => point.x > WalkBounds.min.x + Motor.radius &&
            point.x < WalkBounds.max.x - Motor.radius && point.z > WalkBounds.min.z + Motor.radius && point.z < WalkBounds.max.z - Motor.radius;

        public void MoveDash(Vector3 displacement)
        {
            if (!IsControlling || DialogueInputBlocked || !Motor.enabled || !float.IsFinite(displacement.x) || !float.IsFinite(displacement.z)) return;
            Vector3 desired = transform.position + displacement;
            desired.x = Mathf.Clamp(desired.x, WalkBounds.min.x + Motor.radius, WalkBounds.max.x - Motor.radius);
            desired.z = Mathf.Clamp(desired.z, WalkBounds.min.z + Motor.radius, WalkBounds.max.z - Motor.radius);
            Motor.Move(desired - transform.position);
        }

        public void SetBodyVisible(bool visible)
        {
            if (visible == BodyVisible || BodyRenderers == null) return;
            BodyVisible = visible;
            foreach (Renderer body in BodyRenderers)
                if (body != null) body.shadowCastingMode = visible ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
        }

        private bool FindObstruction(Vector3 origin, Vector3 direction, float range, out RaycastHit closest)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, ShotHits, range, ~0, QueryTriggerInteraction.Ignore);
            closest = default;
            float distance = range + 0.001f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = ShotHits[i];
                if (hit.collider == null || hit.transform.IsChildOf(transform) || hit.distance >= distance) continue;
                closest = hit;
                distance = hit.distance;
            }
            // A saturated query must not allow a shot through unknown geometry.
            return count == ShotHits.Length || closest.collider != null;
        }

        public bool TryGetAimPoint(float range, out Vector3 point)
        {
            point = default;
            if (!IsControlling || DialogueInputBlocked || !float.IsFinite(range) || range <= 0f) return false;
            Ray ray = new(FirstPersonCamera.transform.position, FirstPersonCamera.transform.forward);
            if (FindObstruction(ray.origin, ray.direction, range, out RaycastHit aimed))
            {
                if (aimed.collider == null) return false;
                point = aimed.point + aimed.normal * 0.06f;
            }
            else point = ray.GetPoint(range);
            // Verify the path from the character too: the offset camera cannot shoot around a wall.
            Vector3 origin = transform.position + Vector3.up * 1.8f;
            Vector3 direction = point - origin;
            if (FindObstruction(origin, direction.normalized, direction.magnitude, out RaycastHit block))
            {
                if (block.collider == null) return false;
                point = block.point + block.normal * 0.06f;
            }
            return true;
        }

        public bool HasLineOfSight(Vector3 origin, Collider target)
        {
            Vector3 delta = target.bounds.center - origin;
            if (delta.sqrMagnitude < 0.01f || target.bounds.Contains(origin)) return true;
            if (!FindObstruction(origin + delta.normalized * 0.05f, delta.normalized, delta.magnitude, out RaycastHit hit)) return true;
            return hit.collider != null && hit.collider.GetComponentInParent<Dino>() == target.GetComponentInParent<Dino>();
        }

        public void PlayCastAnimation()
        {
            if (CharacterAnimator != null) { CharacterAnimator.ResetTrigger(CastParameter); CharacterAnimator.SetTrigger(CastParameter); }
        }

        internal void DealAttackDamage(Dino target, int amount, Collider body, bool isSkill)
        {
            Bounds bounds = body.bounds;
            Vector3 numberPosition = new(bounds.center.x, bounds.max.y + 0.4f, bounds.center.z);
            int before = target.Health;
            target.TakeDamage(amount);
            int applied = Mathf.Clamp(before - target.Health, 0, Mathf.Max(0, before));
            if (applied > 0) AttackDamageDealt?.Invoke(applied, numberPosition, isSkill);
        }

        public bool TryCast()
        {
            if (!IsControlling || DialogueInputBlocked || ActionElapsedSeconds < NextSpellTime ||
                !TryGetAimPoint(SpellRange, out Vector3 point)) return false;
            NextSpellTime = ActionElapsedSeconds + SpellCooldown;
            // Moving shots must not snap the whole body away from its gait every 0.45 seconds.
            if (FollowCamera != null && LastMoveDirection.sqrMagnitude < 0.01f)
                transform.rotation = FollowCamera.PlanarRotation;
            Vector3 origin = transform.position + Vector3.up * 1.8f;
            Vector3 delta = point - origin;
            if (FindObstruction(origin, delta.normalized, delta.magnitude + 0.15f, out RaycastHit hit) && hit.collider != null &&
                hit.collider.GetComponentInParent<Dino>() is Dino dino && !dino.IsDead && RoundManager.Instance.ActiveDinoUnits.Contains(dino))
            {
                DealAttackDamage(dino, SpellDamage, hit.collider, false);
                HitFlashUntil = Time.unscaledTime + 0.18f;
                point = hit.point;
            }
            if (SpellTrail != null)
            {
                SpellTrail.SetPosition(0, Staff != null ? Staff.position + Staff.up * 0.7f : origin);
                SpellTrail.SetPosition(1, point);
                SpellTrail.enabled = true;
                TrailUntil = Time.unscaledTime + 0.13f;
            }
            PlayCastAnimation();
            return true;
        }

        public void TakeDamage(int damage)
        {
            if (!IsControlling || Dead || damage <= 0) return;
            if (Abilities != null) damage = Abilities.AbsorbDamage(damage);
            if (damage == 0) return;
            int applied = Mathf.Min(Health, damage);
            Health -= applied;
            DamageFlashUntil = Time.unscaledTime + 0.3f;
            OnTakeDamage?.Invoke(this, applied);
            if (Health > 0) return;
            Dead = true;
            OnDeath?.Invoke(this);
            StatusMessage = "你已倒地，守卫继续保卫村庄";
            Motor.enabled = false;
            ReleaseView();
            if (CharacterAnimator != null) CharacterAnimator.enabled = false;
            CharacterModel.transform.localRotation = Quaternion.Euler(0f, 0f, 80f);
        }

        public void SetLookCaptured(bool captured)
        {
            if (PersistentInteractionMode)
            {
                LookCaptured = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }
            LookCaptured = captured && IsControlling && !DialogueInputBlocked;
            Cursor.lockState = LookCaptured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !LookCaptured;
        }

        /// <summary>
        /// Enables the always-visible JEV interaction mode. The dialogue UI reports
        /// whether the pointer is over itself through SetInteractionPointerOverUi.
        /// </summary>
        public void ConfigurePersistentInteraction(bool enabled)
        {
            if (PersistentInteractionMode == enabled)
            {
                return;
            }

            PersistentInteractionMode = enabled;
            InteractionPointerOverUi = false;
            PrimaryPressSuppressed = false;
            SecondaryPressSuppressed = false;
            if (enabled)
            {
                LookCaptured = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (IsControlling && !DialogueInputBlocked)
            {
                // Leaving the JEV panel returns to the regular mouse-look mode.
                SetLookCaptured(true);
            }
        }

        public void SetInteractionPointerOverUi(bool overUi)
        {
            InteractionPointerOverUi = overUi;
        }

        public void SetDialogueInputBlocked(bool blocked)
        {
            DialogueInputBlocked = blocked;
            if (blocked)
            {
                SetLookCaptured(false);
                LastMoveDirection = Vector3.zero;
            }
        }

        private void UpdatePersistentInteraction()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                    PrimaryPressSuppressed = InteractionPointerOverUi;
                if (mouse.rightButton.wasPressedThisFrame)
                    SecondaryPressSuppressed = InteractionPointerOverUi;

                // A press that began on the dialogue panel stays suppressed until release,
                // even if the pointer is dragged over the world before releasing it.
                if (mouse.leftButton.isPressed && !PrimaryPressSuppressed && !InteractionPointerOverUi)
                    TryCast();
                if (mouse.rightButton.isPressed && !SecondaryPressSuppressed && !InteractionPointerOverUi)
                    ApplyLook(mouse.delta.ReadValue());

                if (mouse.leftButton.wasReleasedThisFrame) PrimaryPressSuppressed = false;
                if (mouse.rightButton.wasReleasedThisFrame) SecondaryPressSuppressed = false;
            }

            Keyboard key = Keyboard.current;
            Vector2 move = Vector2.zero;
            bool sprint = false;
            bool jump = false;
            if (key != null)
            {
                move = new Vector2((key.dKey.isPressed ? 1 : 0) - (key.aKey.isPressed ? 1 : 0),
                    (key.wKey.isPressed ? 1 : 0) - (key.sKey.isPressed ? 1 : 0));
                sprint = key.leftShiftKey.isPressed;
                jump = key.spaceKey.wasPressedThisFrame;
                if (Abilities != null)
                {
                    if (key.qKey.wasPressedThisFrame) Abilities.TryBurst();
                    if (key.eKey.wasPressedThisFrame) Abilities.TryShield();
                    if (key.rKey.wasPressedThisFrame) Abilities.TryDash();
                }
            }
            SimulateMovement(move, sprint, jump, Time.deltaTime);
        }

        private void ReleaseView()
        {
            if (!OwnsCamera) return;
            OwnsCamera = false;
            DialogueInputBlocked = false;
            SetLookCaptured(false);
            Motor.enabled = false;
            FirstPersonCamera.tag = "Untagged";
            FirstPersonCamera.gameObject.SetActive(false);
            if (StrategyCamera != null)
            {
                StrategyCamera.tag = "MainCamera";
                StrategyCamera.enabled = true;
            }
            if (StrategyListener != null) StrategyListener.enabled = true;
            foreach (CameraControl control in StrategyControls) if (control != null) control.enabled = true;
            foreach (Renderer body in BodyRenderers) if (body != null) body.shadowCastingMode = ShadowCastingMode.On;
        }

        private void OnApplicationFocus(bool focused) { if (!focused && OwnsCamera) SetLookCaptured(false); }
        private void OnDisable() => ReleaseView();
    }
}
