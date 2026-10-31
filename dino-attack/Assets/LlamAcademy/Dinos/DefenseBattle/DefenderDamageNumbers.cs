using System;
using System.Collections.Generic;
using LlamAcademy.Dinos.Companion;
using UnityEngine;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.DefenseBattle
{
    // The HUD owns this view and its subscription. Positions are captured before a killing blow
    // can remove the target, so a number never holds a destroyed dinosaur reference.
    public sealed class DefenderDamageNumbers : VisualElement, IDisposable
    {
        private const float Lifetime = 1.1f;
        private const int Capacity = 32;
        private readonly FirstPersonDefender Player;
        private CompanionController Companion;
        private readonly List<Popup> Active = new(Capacity);
        private readonly Stack<Popup> Available = new(Capacity);
        private int Sequence;
        private bool Disposed;

        public DefenderDamageNumbers(FirstPersonDefender player)
        {
            Player = player;
            name = "defender-damage-numbers";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.right = style.top = style.bottom = 0;
            style.overflow = Overflow.Hidden;
            Player.AttackDamageDealt += Show;
            SubscribeCompanion();
        }

        private void Show(int amount, Vector3 position, bool isSkill)
        {
            ShowDamage(amount, position, isSkill, false);
        }

        private void ShowCompanion(int amount, Vector3 position)
        {
            ShowDamage(amount, position, false, true);
        }

        private void ShowDamage(int amount, Vector3 position, bool isSkill, bool isCompanion)
        {
            if (Disposed || amount <= 0) return;
            if (Active.Count == Capacity) Recycle(0);
            Popup popup = Available.Count > 0 ? Available.Pop() : CreatePopup();
            popup.Position = position;
            popup.Started = Time.unscaledTime;
            // Stagger rapid hits without touching Unity's gameplay random state.
            popup.Drift = (Sequence++ % 3 - 1) * 16f;
            popup.Label.text = isCompanion ? $"队友 -{amount}" : "-" + amount;
            popup.Label.EnableInClassList("companion-damage-number", isCompanion);
            popup.Label.EnableInClassList("staff-damage-number", !isSkill && !isCompanion);
            popup.Label.EnableInClassList("skill-damage-number", isSkill && !isCompanion);
            popup.Label.style.color = isCompanion
                ? new Color(0.35f, 0.95f, 1f)
                : isSkill ? new Color(0.86f, 0.63f, 1f) : new Color(1f, 0.86f, 0.3f);
            popup.Label.style.fontSize = isSkill || isCompanion ? 32 : 28;
            popup.Label.style.opacity = 1f;
            popup.Label.style.display = DisplayStyle.None; // Project in LateUpdate, after the follow camera.
            Active.Add(popup);
        }

        public void Refresh(Camera camera, Vector2 panelSize, bool running)
        {
            if (Disposed) return;
            SubscribeCompanion();
            if (!running)
            {
                ClearPopups();
                return;
            }
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                Popup popup = Active[i];
                float age = (Time.unscaledTime - popup.Started) / Lifetime;
                if (age >= 1f) { Recycle(i); continue; }
                if (camera == null || !float.IsFinite(panelSize.x) || !float.IsFinite(panelSize.y) ||
                    panelSize.x <= 0 || panelSize.y <= 0)
                {
                    popup.Label.style.display = DisplayStyle.None;
                    continue;
                }
                Vector3 viewport = camera.WorldToViewportPoint(popup.Position);
                if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1)
                {
                    popup.Label.style.display = DisplayStyle.None;
                    continue;
                }
                Rect rect = camera.rect;
                float x = (rect.x + viewport.x * rect.width) * panelSize.x;
                float y = (1f - rect.y - viewport.y * rect.height) * panelSize.y;
                popup.Label.style.left = x - 48f + popup.Drift * (0.3f + age);
                popup.Label.style.top = y - 22f - age * 58f;
                popup.Label.style.opacity = 1f - Mathf.InverseLerp(0.45f, 1f, age);
                popup.Label.style.display = DisplayStyle.Flex;
            }
        }

        private void SubscribeCompanion()
        {
            if (Companion != null || Disposed) return;
            Companion = UnityEngine.Object.FindFirstObjectByType<CompanionController>(FindObjectsInactive.Include);
            if (Companion != null) Companion.AttackDamageDealt += ShowCompanion;
        }

        private Popup CreatePopup()
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("damage-number");
            label.style.position = Position.Absolute;
            label.style.width = 96;
            label.style.height = 44;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.unityTextOutlineColor = new Color(0.05f, 0.025f, 0.075f, 0.95f);
            label.style.unityTextOutlineWidth = 1.5f;
            Add(label);
            return new Popup { Label = label };
        }

        private void Recycle(int index)
        {
            Popup popup = Active[index];
            popup.Label.style.display = DisplayStyle.None;
            Active.RemoveAt(index);
            Available.Push(popup);
        }

        private void ClearPopups()
        {
            for (int i = Active.Count - 1; i >= 0; i--) Recycle(i);
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            if (Player != null) Player.AttackDamageDealt -= Show;
            if (Companion != null) Companion.AttackDamageDealt -= ShowCompanion;
            ClearPopups();
            RemoveFromHierarchy();
        }

        private sealed class Popup
        {
            public Label Label;
            public Vector3 Position;
            public float Started;
            public float Drift;
        }
    }
}
