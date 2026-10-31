using System;
using System.Collections.Generic;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Unit;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace LlamAcademy.Dinos.DefenseBattle
{
    // Owned by the map-four HUD; reads the battle without introducing a second camera or clock.
    public sealed class DefenseMinimap : VisualElement, IDisposable
    {
        private static readonly Color PlayerColor = new(0.35f, 0.9f, 1f);
        private static readonly Color EnemyColor = new(1f, 0.32f, 0.3f);
        private static readonly Color GuardColor = new(0.42f, 0.94f, 0.65f);
        private static readonly Color TargetColor = new(1f, 0.82f, 0.28f);
        private static readonly Color HouseColor = new(0.84f, 0.74f, 0.56f);
        private readonly FirstPersonDefender Player;
        private readonly RoundManager Round;
        private readonly DefensePlacementController Placement;
        private readonly VisualElement Canvas;
        private readonly VisualElement PlayerMarker;
        private readonly VisualElement TargetMarker;
        private readonly Label CountLabel;
        private readonly List<Marker> Houses = new();
        private readonly List<Marker> Defenses = new();
        private readonly Dictionary<Dino, Marker> Enemies = new();
        private readonly List<Dino> Removed = new();
        private readonly List<Vector3[]> ZoneVertices = new();
        private readonly List<Label> ZoneLabels = new();
        private readonly List<Vector3> ZoneCenters = new();
        private Rect WorldRect;
        private float Side;
        private int LastEnemyCount = -1;
        private bool Disposed;

        public DefenseMinimap(FirstPersonDefender player, RoundManager round, DefensePlacementController placement,
            IReadOnlyList<DinoDeploymentZone> zones)
        {
            Player = player;
            Round = round;
            Placement = placement;
            name = "defense-minimap";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.right = 18;
            style.top = 134;
            style.paddingLeft = style.paddingRight = 10;
            style.paddingTop = style.paddingBottom = 8;
            style.backgroundColor = new Color(0.035f, 0.075f, 0.085f, 0.94f);
            style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 8;
            var header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.marginBottom = 6;
            header.Add(Text("战场地图", Color.white, 17));
            header.Add(Text("北 ↑", PlayerColor, 15));
            Add(header);
            Canvas = new VisualElement { name = "minimap-canvas", pickingMode = PickingMode.Ignore };
            Canvas.style.overflow = Overflow.Hidden;
            Canvas.style.backgroundColor = new Color(0.11f, 0.17f, 0.16f);
            Canvas.generateVisualContent += DrawTerrain;
            Add(Canvas);
            BuildGeometry(zones);
            foreach (VillageHouse house in Round.SessionHouses)
                if (house != null) Houses.Add(MakeMarker(house, house, "house", HouseColor, 7));
            TargetMarker = Glyph("minimap-target", TargetColor, 14, true);
            Canvas.Add(TargetMarker);
            PlayerMarker = Glyph("minimap-player", PlayerColor, 16, false);
            Canvas.Add(PlayerMarker);
            var legend = new VisualElement { pickingMode = PickingMode.Ignore };
            legend.style.flexDirection = FlexDirection.Row;
            legend.style.justifyContent = Justify.SpaceBetween;
            legend.style.marginTop = 6;
            legend.Add(Text("▲ 玩家", PlayerColor, 14));
            legend.Add(Text("◆ 目标", TargetColor, 14));
            legend.Add(Text("● 守卫", GuardColor, 14));
            Add(legend);
            var footer = new VisualElement { pickingMode = PickingMode.Ignore };
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.justifyContent = Justify.SpaceBetween;
            footer.Add(Text("■ 房屋 / 城墙", HouseColor, 13));
            CountLabel = Text("敌人 0", EnemyColor, 14);
            footer.Add(CountLabel);
            Add(footer);
            if (Placement != null) Placement.StateChanged += RefreshDefenses;
            RefreshDefenses();
        }

        public void Refresh(float rootWidth, float rootHeight)
        {
            if (Disposed) return;
            bool visible = Round != null && (Round.State == GameState.Setup || Round.State == GameState.Running);
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            float size = Mathf.Clamp(Mathf.Min(rootWidth * 0.23f, rootHeight * 0.32f), 164, 252);
            if (Mathf.Abs(size - Side) > 0.5f)
            {
                Side = size;
                Canvas.style.width = Canvas.style.height = Side;
            }
            for (int i = 0; i < ZoneLabels.Count; i++)
            {
                Vector2 point = Project(ZoneCenters[i]);
                ZoneLabels[i].style.left = point.x - 12;
                ZoneLabels[i].style.top = point.y - 8;
            }
            foreach (Marker marker in Houses) UpdateMarker(marker);
            foreach (Marker marker in Defenses) UpdateMarker(marker);
            SynchronizeEnemies();
            if (LastEnemyCount != Enemies.Count)
            {
                LastEnemyCount = Enemies.Count;
                CountLabel.text = "● 敌人 " + LastEnemyCount;
            }
            TargetMarker.style.display = Round.DinoTarget != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (Round.DinoTarget != null) SetMarkerPosition(TargetMarker, Round.DinoTarget.position, 14);
            bool alive = Player != null && Player.IsControlling && Player.Health > 0;
            PlayerMarker.style.display = alive ? DisplayStyle.Flex : DisplayStyle.None;
            if (alive)
            {
                SetMarkerPosition(PlayerMarker, Player.transform.position, 16);
                PlayerMarker.style.rotate = new Rotate(new Angle(Player.transform.eulerAngles.y));
            }
            // Keep these two important symbols above all spawned units.
            TargetMarker.BringToFront();
            PlayerMarker.BringToFront();
            Canvas.MarkDirtyRepaint();
        }

        // Equal scale on X/Z, north (+Z) at the top, with room for edge markers.
        public static Vector2 WorldToMap(Vector3 point, Rect bounds, Vector2 size)
        {
            const float padding = 10f;
            float scale = Mathf.Min(Mathf.Max(1, size.x - 2 * padding) / Mathf.Max(1, bounds.width),
                Mathf.Max(1, size.y - 2 * padding) / Mathf.Max(1, bounds.height));
            return new Vector2(
                Mathf.Clamp(size.x * 0.5f + (point.x - bounds.center.x) * scale, padding, Mathf.Max(padding, size.x - padding)),
                Mathf.Clamp(size.y * 0.5f - (point.z - bounds.center.y) * scale, padding, Mathf.Max(padding, size.y - padding)));
        }

        private Vector2 Project(Vector3 point) => WorldToMap(point, WorldRect, new Vector2(Side, Side));

        private void BuildGeometry(IReadOnlyList<DinoDeploymentZone> zones)
        {
            Vector3 origin = Round.DinoTarget != null ? Round.DinoTarget.position : Player.transform.position;
            Bounds bounds = new(origin, Vector3.one);
            foreach (VillageHouse house in Round.SessionHouses)
                if (house != null) bounds.Encapsulate(house.transform.position);
            if (zones != null)
                foreach (DinoDeploymentZone zone in zones)
                {
                    if (zone == null || !zone.isActiveAndEnabled) continue;
                    var vertices = zone.WorldVertices;
                    var polygon = new Vector3[vertices.Count];
                    Vector3 center = Vector3.zero;
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        polygon[i] = vertices[i];
                        bounds.Encapsulate(vertices[i]);
                        center += vertices[i];
                    }
                    ZoneVertices.Add(polygon);
                    ZoneCenters.Add(center / vertices.Count);
                    Label label = Text("Z" + ((int)zone.Id + 1), new Color(0.85f, 0.91f, 0.82f, 0.85f), 12);
                    label.AddToClassList("minimap-zone-label");
                    label.style.position = Position.Absolute;
                    label.style.width = 26;
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                    Canvas.Add(label);
                    ZoneLabels.Add(label);
                }
            // Frame the complete initial battlefield once, so deaths never make the map jump or zoom.
            bounds.Expand(new Vector3(28, 0, 28));
            WorldRect = Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
        }

        private void RefreshDefenses()
        {
            if (Disposed) return;
            foreach (Marker marker in Defenses) marker.Element.RemoveFromHierarchy();
            Defenses.Clear();
            foreach (PlacedDefenseHandle handle in Object.FindObjectsByType<PlacedDefenseHandle>(FindObjectsSortMode.None))
            {
                if (handle.gameObject.scene != Player.gameObject.scene) continue;
                if (handle.Wall != null) Defenses.Add(MakeMarker(handle.Wall, handle.Wall, "wall", HouseColor, 8));
                if (handle.GroundGuard != null) Defenses.Add(MakeMarker(handle.GroundGuard, handle.GroundGuard, "guard", GuardColor, 5));
                if (handle.WallGuard != null) Defenses.Add(MakeMarker(handle.WallGuard, handle.WallGuard, "guard", GuardColor, 5));
            }
        }

        private void SynchronizeEnemies()
        {
            Removed.Clear();
            foreach (var entry in Enemies)
                if (!IsAlive(entry.Value) || !IsRegistered(entry.Key)) Removed.Add(entry.Key);
            foreach (Dino dino in Removed)
            {
                Enemies[dino].Element.RemoveFromHierarchy();
                Enemies.Remove(dino);
            }
            foreach (Dino dino in Round.ActiveDinoUnits)
            {
                if (dino == null || !dino.isActiveAndEnabled || dino.Health <= 0) continue;
                if (!Enemies.TryGetValue(dino, out Marker marker))
                {
                    marker = MakeMarker(dino, dino, "enemy", EnemyColor, 8);
                    Enemies.Add(dino, marker);
                }
                UpdateMarker(marker);
            }
        }

        private bool IsRegistered(Dino dino)
        {
            foreach (Dino registered in Round.ActiveDinoUnits)
                if (registered == dino) return true;
            return false;
        }

        private Marker MakeMarker(Component target, IDamageable health, string kind, Color color, float size)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore, userData = target };
            element.AddToClassList("minimap-" + kind);
            element.style.position = Position.Absolute;
            element.style.width = element.style.height = size;
            element.style.backgroundColor = color;
            if (kind == "enemy" || kind == "guard")
                element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
                    element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = size;
            element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = element.style.borderTopWidth = 1;
            element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = element.style.borderTopColor = new Color(0.03f, 0.06f, 0.08f);
            Canvas.Add(element);
            return new Marker { Target = target, Health = health, Element = element, Size = size };
        }

        private static bool IsAlive(Marker marker) => marker.Target != null &&
            marker.Target.gameObject.activeInHierarchy && marker.Health.Health > 0;

        private void UpdateMarker(Marker marker)
        {
            bool alive = IsAlive(marker);
            marker.Element.style.display = alive ? DisplayStyle.Flex : DisplayStyle.None;
            if (alive) SetMarkerPosition(marker.Element, marker.Target.transform.position, marker.Size);
        }

        private void SetMarkerPosition(VisualElement marker, Vector3 position, float size)
        {
            Vector2 point = Project(position);
            marker.style.left = point.x - size * 0.5f;
            marker.style.top = point.y - size * 0.5f;
        }

        private void DrawTerrain(MeshGenerationContext context)
        {
            if (Side <= 0 || Disposed) return;
            Painter2D painter = context.painter2D;
            painter.lineWidth = 1;
            painter.strokeColor = new Color(0.5f, 0.65f, 0.6f, 0.12f);
            for (int i = 1; i < 5; i++)
            {
                float p = Side * i / 5;
                painter.BeginPath(); painter.MoveTo(new Vector2(p, 0)); painter.LineTo(new Vector2(p, Side)); painter.Stroke();
                painter.BeginPath(); painter.MoveTo(new Vector2(0, p)); painter.LineTo(new Vector2(Side, p)); painter.Stroke();
            }
            foreach (Vector3[] vertices in ZoneVertices)
            {
                painter.fillColor = new Color(0.24f, 0.34f, 0.25f, 0.9f);
                painter.strokeColor = new Color(0.5f, 0.67f, 0.46f, 0.55f);
                painter.BeginPath(); painter.MoveTo(Project(vertices[0]));
                for (int i = 1; i < vertices.Length; i++) painter.LineTo(Project(vertices[i]));
                painter.ClosePath(); painter.Fill(); painter.Stroke();
            }
            if (Player == null || !Player.IsControlling || Camera.main == null) return;
            Vector3 position = Player.transform.position;
            float yaw = Camera.main.transform.eulerAngles.y;
            painter.fillColor = new Color(PlayerColor.r, PlayerColor.g, PlayerColor.b, 0.12f);
            painter.BeginPath(); painter.MoveTo(Project(position));
            painter.LineTo(Project(position + Quaternion.Euler(0, yaw - 30, 0) * Vector3.forward * 24));
            painter.LineTo(Project(position + Quaternion.Euler(0, yaw + 30, 0) * Vector3.forward * 24));
            painter.ClosePath(); painter.Fill();
        }

        private static VisualElement Glyph(string name, Color color, float size, bool diamond)
        {
            var element = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            element.style.position = Position.Absolute;
            element.style.width = element.style.height = size;
            element.generateVisualContent += context =>
            {
                var p = context.painter2D;
                p.fillColor = color; p.strokeColor = new Color(0.02f, 0.07f, 0.08f); p.lineWidth = 1.5f;
                p.BeginPath(); p.MoveTo(new Vector2(size / 2, 0));
                p.LineTo(new Vector2(size, diamond ? size / 2 : size));
                p.LineTo(new Vector2(size / 2, diamond ? size : size * 0.72f));
                p.LineTo(new Vector2(0, diamond ? size / 2 : size));
                p.ClosePath(); p.Fill(); p.Stroke();
            };
            return element;
        }

        private static Label Text(string value, Color color, int size) => new(value)
        {
            pickingMode = PickingMode.Ignore,
            style = { color = color, fontSize = size, marginLeft = 0, marginRight = 0, marginTop = 0, marginBottom = 0 }
        };

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            if (Placement != null) Placement.StateChanged -= RefreshDefenses;
            Canvas.generateVisualContent -= DrawTerrain;
            RemoveFromHierarchy();
        }

        private sealed class Marker
        {
            public Component Target;
            public IDamageable Health;
            public VisualElement Element;
            public float Size;
        }
    }
}
