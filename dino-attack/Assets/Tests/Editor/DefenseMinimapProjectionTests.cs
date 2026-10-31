using System;
using NUnit.Framework;
using UnityEngine;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class DefenseMinimapProjectionTests
    {
        private static Vector2 Project(Vector3 point, Rect bounds, Vector2 size) => (Vector2)
            Type.GetType("LlamAcademy.Dinos.DefenseBattle.DefenseMinimap, Assembly-CSharp", true)
                .GetMethod("WorldToMap").Invoke(null, new object[] { point, bounds, size });

        [Test]
        public void ProjectionKeepsNorthUpAndCentersTheActualWorldBounds()
        {
            Rect bounds = new(-30, -50, 80, 120);
            Vector2 size = new(240, 240);
            Vector3 center = new(10, 500, 10);
            Assert.That(Project(center, bounds, size), Is.EqualTo(new Vector2(120, 120)));
            Assert.That(Project(center + Vector3.forward * 10, bounds, size).y, Is.LessThan(120));
            Assert.That(Project(center + Vector3.right * 10, bounds, size).x, Is.GreaterThan(120));
            Assert.That(Project(center + Vector3.back * 10, bounds, size).y, Is.GreaterThan(120));
            Assert.That(Project(center + Vector3.left * 10, bounds, size).x, Is.LessThan(120));
        }

        [Test]
        public void RectangularWorldAndPanelKeepEqualDistanceOnBothAxes()
        {
            Rect bounds = new(-100, -20, 200, 40);
            Vector2 size = new(260, 180);
            Vector2 center = Project(Vector3.zero, bounds, size);
            float east = Vector2.Distance(center, Project(Vector3.right * 10, bounds, size));
            float north = Vector2.Distance(center, Project(Vector3.forward * 10, bounds, size));
            Assert.That(east, Is.EqualTo(12).Within(0.001));
            Assert.That(north, Is.EqualTo(east).Within(0.001), "Do not stretch the battlefield to a square.");
        }

        [Test]
        public void OutsidePositionsStayInsideTheBorderAndDegenerateBoundsAreFinite()
        {
            Vector2 size = new(240, 240);
            Vector2 edge = Project(new Vector3(10000, 0, -10000), new Rect(-10, -10, 20, 20), size);
            Assert.That(edge, Is.EqualTo(new Vector2(230, 230)));
            Vector2 point = Project(Vector3.zero, new Rect(), size);
            Assert.That(float.IsNaN(point.x) || float.IsInfinity(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.y), Is.False);
        }
    }
}
