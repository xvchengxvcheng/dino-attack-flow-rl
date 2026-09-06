using System.Collections.Generic;
using UnityEngine;

namespace LlamAcademy.Dinos.Map
{
    public readonly struct QuadrilateralXZ
    {
        private const float Epsilon = 0.0001f;
        private const float MinimumArea = 0.01f;
        private const float MinimumEdgeLengthSquared = 0.0001f;

        public Vector2 A { get; }
        public Vector2 B { get; }
        public Vector2 C { get; }
        public Vector2 D { get; }
        public float SignedArea { get; }
        public IReadOnlyList<Vector2> Vertices => new[] { A, B, C, D };

        private QuadrilateralXZ(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float signedArea)
        {
            A = a;
            B = b;
            C = c;
            D = d;
            SignedArea = signedArea;
        }

        public static bool TryCreate(
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            out QuadrilateralXZ value)
        {
            value = default;
            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c) || !IsFinite(d))
            {
                return false;
            }

            if ((b - a).sqrMagnitude < MinimumEdgeLengthSquared ||
                (c - b).sqrMagnitude < MinimumEdgeLengthSquared ||
                (d - c).sqrMagnitude < MinimumEdgeLengthSquared ||
                (a - d).sqrMagnitude < MinimumEdgeLengthSquared)
            {
                return false;
            }

            float abCrossBc = Cross(b - a, c - b);
            float bcCrossCd = Cross(c - b, d - c);
            float cdCrossDa = Cross(d - c, a - d);
            float daCrossAb = Cross(a - d, b - a);
            bool allPositive = abCrossBc > Epsilon && bcCrossCd > Epsilon &&
                               cdCrossDa > Epsilon && daCrossAb > Epsilon;
            bool allNegative = abCrossBc < -Epsilon && bcCrossCd < -Epsilon &&
                               cdCrossDa < -Epsilon && daCrossAb < -Epsilon;
            if (!allPositive && !allNegative)
            {
                return false;
            }

            if (SegmentsProperlyIntersect(a, b, c, d) || SegmentsProperlyIntersect(b, c, d, a))
            {
                return false;
            }

            float signedArea = 0.5f * (
                a.x * b.y - b.x * a.y +
                b.x * c.y - c.x * b.y +
                c.x * d.y - d.x * c.y +
                d.x * a.y - a.x * d.y);
            if (Mathf.Abs(signedArea) < MinimumArea)
            {
                return false;
            }

            value = new QuadrilateralXZ(a, b, c, d, signedArea);
            return true;
        }

        public bool TryEvaluate(Vector2 uv01, out Vector2 point)
        {
            point = default;
            if (!IsFinite(uv01) || uv01.x < 0f || uv01.x > 1f || uv01.y < 0f || uv01.y > 1f)
            {
                return false;
            }

            Vector2 inner = Vector2.Lerp(A, B, uv01.x);
            Vector2 outer = Vector2.Lerp(D, C, uv01.x);
            point = Vector2.Lerp(inner, outer, uv01.y);
            return true;
        }

        public bool Contains(Vector2 point)
        {
            if (!IsFinite(point) || Mathf.Abs(SignedArea) < MinimumArea)
            {
                return false;
            }

            float winding = Mathf.Sign(SignedArea);
            return winding * Cross(B - A, point - A) >= -Epsilon &&
                   winding * Cross(C - B, point - B) >= -Epsilon &&
                   winding * Cross(D - C, point - C) >= -Epsilon &&
                   winding * Cross(A - D, point - D) >= -Epsilon;
        }

        public float DistanceTo(Vector2 point)
        {
            if (!IsFinite(point)) return float.PositiveInfinity;
            if (Contains(point)) return 0f;
            return Mathf.Min(
                Mathf.Min(DistanceToSegment(point, A, B), DistanceToSegment(point, B, C)),
                Mathf.Min(DistanceToSegment(point, C, D), DistanceToSegment(point, D, A)));
        }

        public bool Overlaps(QuadrilateralXZ other)
        {
            if (Mathf.Abs(SignedArea) < MinimumArea || Mathf.Abs(other.SignedArea) < MinimumArea)
            {
                return false;
            }

            return HasPositiveProjectionOverlap(this, other, B - A) &&
                   HasPositiveProjectionOverlap(this, other, C - B) &&
                   HasPositiveProjectionOverlap(this, other, D - C) &&
                   HasPositiveProjectionOverlap(this, other, A - D) &&
                   HasPositiveProjectionOverlap(this, other, other.B - other.A) &&
                   HasPositiveProjectionOverlap(this, other, other.C - other.B) &&
                   HasPositiveProjectionOverlap(this, other, other.D - other.C) &&
                   HasPositiveProjectionOverlap(this, other, other.A - other.D);
        }

        private static bool HasPositiveProjectionOverlap(
            QuadrilateralXZ first,
            QuadrilateralXZ second,
            Vector2 edge)
        {
            Vector2 axis = new(-edge.y, edge.x);
            Project(first, axis, out float firstMin, out float firstMax);
            Project(second, axis, out float secondMin, out float secondMax);
            return firstMax > secondMin + Epsilon && secondMax > firstMin + Epsilon;
        }

        private static void Project(QuadrilateralXZ value, Vector2 axis, out float minimum, out float maximum)
        {
            float a = Vector2.Dot(value.A, axis);
            float b = Vector2.Dot(value.B, axis);
            float c = Vector2.Dot(value.C, axis);
            float d = Vector2.Dot(value.D, axis);
            minimum = Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d));
            maximum = Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d));
        }

        private static bool SegmentsProperlyIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);
            return abC * abD < -Epsilon && cdA * cdB < -Epsilon;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 edge = end - start;
            float t = Mathf.Clamp01(Vector2.Dot(point - start, edge) / edge.sqrMagnitude);
            return Vector2.Distance(point, start + edge * t);
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        }
    }
}
