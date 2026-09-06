using UnityEngine;

namespace LlamAcademy.Dinos.Map
{
    public static class CameraFramingMath
    {
        public static Vector3 ClampTarget(Vector3 position, Bounds worldBounds) => worldBounds.ClosestPoint(position);

        public static float ClampZoom(float value, float minimum, float maximum) => Mathf.Clamp(value, minimum, maximum);

        public static Vector3 ScaleFollowOffset(Vector3 homeOffset, float zoomScale) => homeOffset * zoomScale;

        public static Vector3 PanTarget(Vector3 position, Vector3 worldDelta, Bounds worldBounds) =>
            ClampTarget(position + new Vector3(worldDelta.x, 0f, worldDelta.z), worldBounds);

        public static float NextZoomScale(
            float currentScale,
            float wheelDelta,
            float sensitivity,
            float minimum,
            float maximum) =>
            ClampZoom(currentScale - wheelDelta * sensitivity, minimum, maximum);

        public static Vector3 DragTarget(
            Vector3 targetAtDragStart,
            Vector3 dragOrigin,
            Vector3 currentPoint,
            Bounds worldBounds) =>
            PanTarget(targetAtDragStart, dragOrigin - currentPoint, worldBounds);

        public static bool IsScreenPositionInside(Vector2 position, int width, int height) =>
            IsScreenPositionInside(position, new Rect(0f, 0f, width, height));

        public static bool IsScreenPositionInside(Vector2 position, Rect pixelRect) =>
            position.x >= pixelRect.xMin && position.y >= pixelRect.yMin &&
            position.x < pixelRect.xMax && position.y < pixelRect.yMax;
    }
}
