using UnityEngine;
using UnityEngine.Rendering;

namespace LlamAcademy.Dinos.Companion
{
    /// <summary>
    /// Owns the companion's presentation-only weapon and spell feedback.
    /// Damage remains authoritative in CompanionController; this component only
    /// creates the hand prop and short-lived beam/impact visuals.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionCombatPresentation : MonoBehaviour
    {
        private const float TrailLifetime = 0.18f;
        private const float ImpactLifetime = 0.24f;

        private Transform CharacterModel;
        private Transform Staff;
        private LineRenderer SpellTrail;
        private GameObject ImpactVisual;
        private Material WoodMaterial;
        private Material GoldMaterial;
        private Material CrystalMaterial;
        private Material TrailMaterial;
        private float TrailUntil;
        private float ImpactUntil;

        public Transform StaffTransform => Staff;

        public void Initialize(GameObject characterModel, Animator animator)
        {
            if (characterModel == null || animator == null)
            {
                return;
            }

            CharacterModel = characterModel.transform;
            if (Staff == null)
            {
                CreateStaff(animator);
            }

            if (SpellTrail == null)
            {
                CreateSpellTrail();
            }

            if (ImpactVisual == null)
            {
                CreateImpactVisual();
            }
        }

        public void PlayAttack(Vector3 targetPosition)
        {
            if (Staff == null || SpellTrail == null)
            {
                return;
            }

            Vector3 origin = Staff.position + Staff.up * 0.88f;
            SpellTrail.SetPosition(0, origin);
            SpellTrail.SetPosition(1, targetPosition);
            SpellTrail.enabled = true;
            TrailUntil = Time.unscaledTime + TrailLifetime;

            if (ImpactVisual != null)
            {
                ImpactVisual.transform.position = targetPosition;
                ImpactVisual.transform.localScale = Vector3.one * 0.18f;
                ImpactVisual.SetActive(true);
                ImpactUntil = Time.unscaledTime + ImpactLifetime;
            }
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (SpellTrail != null && SpellTrail.enabled && now >= TrailUntil)
            {
                SpellTrail.enabled = false;
            }

            if (ImpactVisual != null && ImpactVisual.activeSelf)
            {
                float remaining = Mathf.Clamp01((ImpactUntil - now) / ImpactLifetime);
                ImpactVisual.transform.localScale = Vector3.one * Mathf.Lerp(0.52f, 0.18f, remaining);
                if (now >= ImpactUntil)
                {
                    ImpactVisual.SetActive(false);
                }
            }
        }

        private void CreateStaff(Animator animator)
        {
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null)
            {
                hand = FindDescendant(CharacterModel, "J_Bip_R_Hand");
            }
            if (hand == null)
            {
                Debug.LogWarning("Companion model has no right-hand bone; the combat staff cannot be attached.", this);
                return;
            }

            Staff = new GameObject("Companion Arcane Staff").transform;
            Staff.SetParent(hand, false);
            float handScale = Mathf.Max(0.01f, hand.lossyScale.x);
            Staff.localPosition = new Vector3(0.04f, 0.05f, 0.03f) / handScale;
            Staff.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Staff.localScale = Vector3.one * (1.1f / handScale);

            WoodMaterial = CreateMaterial("Companion Staff Wood", new Color(0.20f, 0.08f, 0.035f), false);
            GoldMaterial = CreateMaterial("Companion Staff Gold", new Color(0.95f, 0.60f, 0.12f), true);
            CrystalMaterial = CreateMaterial("Companion Staff Crystal", new Color(0.08f, 0.85f, 1f), true);

            CreateStaffPart(PrimitiveType.Cylinder, "Wooden shaft", Vector3.zero,
                new Vector3(0.07f, 0.62f, 0.07f), WoodMaterial);
            CreateStaffPart(PrimitiveType.Cylinder, "Gold collar", new Vector3(0f, 0.62f, 0f),
                new Vector3(0.15f, 0.05f, 0.15f), GoldMaterial);
            Transform crystal = CreateStaffPart(PrimitiveType.Cube, "Crystal", new Vector3(0f, 0.83f, 0f),
                new Vector3(0.16f, 0.30f, 0.16f), CrystalMaterial);
            crystal.localRotation = Quaternion.Euler(0f, 45f, 20f);
        }

        private Transform CreateStaffPart(PrimitiveType primitive, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(Staff, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            return part.transform;
        }

        private void CreateSpellTrail()
        {
            TrailMaterial = CreateMaterial("Companion Spell Trail", new Color(0.08f, 0.85f, 1f), true);
            GameObject lineObject = new("Companion Spell Trail");
            lineObject.transform.SetParent(transform, false);
            SpellTrail = lineObject.AddComponent<LineRenderer>();
            SpellTrail.positionCount = 2;
            SpellTrail.startWidth = 0.09f;
            SpellTrail.endWidth = 0.025f;
            SpellTrail.numCapVertices = 4;
            SpellTrail.sharedMaterial = TrailMaterial;
            SpellTrail.useWorldSpace = true;
            SpellTrail.shadowCastingMode = ShadowCastingMode.Off;
            SpellTrail.receiveShadows = false;
            SpellTrail.enabled = false;
        }

        private void CreateImpactVisual()
        {
            ImpactVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ImpactVisual.name = "Companion Spell Impact";
            ImpactVisual.transform.SetParent(transform, false);
            ImpactVisual.transform.localScale = Vector3.one * 0.18f;
            Renderer renderer = ImpactVisual.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = CrystalMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            Collider collider = ImpactVisual.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            ImpactVisual.SetActive(false);
        }

        private static Material CreateMaterial(string name, Color color, bool emission)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                            Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Standard");
            if (shader == null) return null;

            Material material = new(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.color = color;
            if (emission && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.8f);
            }
            return material;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name) return child;
            }
            return null;
        }

        private void OnDestroy()
        {
            DestroyRuntimeObject(ImpactVisual);
            DestroyRuntimeObject(SpellTrail == null ? null : SpellTrail.gameObject);
            DestroyRuntimeObject(WoodMaterial);
            DestroyRuntimeObject(GoldMaterial);
            DestroyRuntimeObject(CrystalMaterial);
            DestroyRuntimeObject(TrailMaterial);
        }

        private static void DestroyRuntimeObject(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
