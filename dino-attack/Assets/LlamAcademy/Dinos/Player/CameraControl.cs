using System;
using Unity.Cinemachine;
using LlamAcademy.Dinos.Map;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LlamAcademy.Dinos.Player
{
    [RequireComponent(typeof(CinemachineCamera))]
    public class CameraControl : MonoBehaviour
    {
        [SerializeField]
        private AnimationCurve SpeedRamp = new() { keys = new Keyframe[] { new Keyframe(0, 0), new Keyframe(0, 1) } };

        [SerializeField] private bool EnableMousePan;
        [SerializeField]
        private float EdgeScrollWidth = 100;

        [SerializeField] private BoxCollider WorldBounds;
        [SerializeField]
        [Range(0.01f, 50)]
        private float KeyboardSpeed = 5f;

        [SerializeField]
        [Range(0.01f, 1f)]
        private float ZoomSensitivity = 0.22f;

        [SerializeField]
        [Range(0.1f, 1f)]
        private float MinimumZoomScale = 0.45f;

        [SerializeField]
        [Range(1f, 3f)]
        private float MaximumZoomScale = 1.35f;

        private CinemachineCamera CinemachineCamera;
        private CinemachineFollow CinemachineFollow;
        private Transform FollowTarget;
        private Camera OutputCamera;
        private Vector3 HomeTargetPosition;
        private Vector3 HomeFollowOffset;
        private float ZoomScale = 1f;
        private float MouseScrollStartTime;
        private bool IsMouseScrolling;
        private bool IsMiddleDragging;
        private Vector3 MiddleDragOrigin;
        private Vector3 MiddleDragTargetOrigin;
        private Func<bool> ApplicationFocusProvider = () => Application.isFocused;

        private void Awake()
        {
            CinemachineCamera = GetComponent<CinemachineCamera>();
            CinemachineFollow = GetComponent<CinemachineFollow>();
            FollowTarget = CinemachineCamera.Follow;
            OutputCamera = Camera.main;

            if (FollowTarget == null || CinemachineFollow == null)
            {
                Debug.LogError("CameraControl requires a Cinemachine follow target and CinemachineFollow component.", this);
                enabled = false;
                return;
            }

            HomeTargetPosition = FollowTarget.position;
            HomeFollowOffset = CinemachineFollow.FollowOffset;
        }

        private void Update()
        {
            HandleKeyboardInput();
            HandleMouseEdgePan();
            HandleMouseZoom();
            HandleMiddleDrag();

            ClampToWorld();
        }

        private void HandleMouseEdgePan()
        {
            if (!CanConsumePointerInput(out Vector2 screenPosition))
            {
                IsMouseScrolling = false;
                return;
            }

            if (!EnableMousePan)
            {
                IsMouseScrolling = false;
                return;
            }

            Vector3 moveDirection = Vector3.zero;
            Rect pixelRect = OutputCamera.pixelRect;
            float horizontalEdgeWidth = Mathf.Min(EdgeScrollWidth, pixelRect.width * 0.5f);
            float verticalEdgeWidth = Mathf.Min(EdgeScrollWidth, pixelRect.height * 0.5f);
            float scrollLeftPosition = pixelRect.xMin + horizontalEdgeWidth;
            float scrollRightPosition = pixelRect.xMax - horizontalEdgeWidth;
            float scrollDownPosition = pixelRect.yMin + verticalEdgeWidth;
            float scrollUpPosition = pixelRect.yMax - verticalEdgeWidth;

            if (screenPosition.x < scrollLeftPosition)
            {
                moveDirection += Vector3.right;
            }
            else if (screenPosition.x > scrollRightPosition)
            {
                moveDirection += Vector3.left;
            }

            if (screenPosition.y < scrollDownPosition)
            {
                moveDirection += Vector3.forward;
            }
            else if (screenPosition.y > scrollUpPosition)
            {
                moveDirection += Vector3.back;
            }

            if (moveDirection != Vector3.zero)
            {
                if (!IsMouseScrolling)
                {
                    MouseScrollStartTime = Time.time;
                }
                IsMouseScrolling = true;
            }
            else
            {
                IsMouseScrolling = false;
            }

            ApplyPan(SpeedRamp.Evaluate(Time.time - MouseScrollStartTime) * Time.deltaTime * moveDirection);
        }

        private void HandleKeyboardInput()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.homeKey.wasPressedThisFrame)
            {
                ResetHome();
                return;
            }

            Vector3 moveDirection = Vector3.zero;
            if (Keyboard.current.upArrowKey.isPressed)
            {
                moveDirection += Vector3.back;
            }
            if (Keyboard.current.downArrowKey.isPressed)
            {
                moveDirection += Vector3.forward;
            }
            if (Keyboard.current.leftArrowKey.isPressed)
            {
                moveDirection += Vector3.right;
            }
            if (Keyboard.current.rightArrowKey.isPressed)
            {
                moveDirection += Vector3.left;
            }

            ApplyPan(KeyboardSpeed * Time.deltaTime * moveDirection);
        }

        private void HandleMouseZoom()
        {
            if (!CanConsumePointerInput(out _))
            {
                return;
            }

            float wheelDelta = Mouse.current.scroll.ReadValue().y / 120f;
            if (!Mathf.Approximately(wheelDelta, 0f))
            {
                ApplyZoom(wheelDelta);
            }
        }

        private void HandleMiddleDrag()
        {
            if (!CanConsumePointerInput(out Vector2 displayPosition))
            {
                IsMiddleDragging = false;
                return;
            }

            if (Mouse.current.middleButton.wasPressedThisFrame)
            {
                IsMiddleDragging = TryProjectMouseToTargetPlane(displayPosition, out MiddleDragOrigin);
                MiddleDragTargetOrigin = FollowTarget.position;
            }

            if (IsMiddleDragging && Mouse.current.middleButton.isPressed &&
                TryProjectMouseToTargetPlane(displayPosition, out Vector3 currentPoint))
            {
                FollowTarget.position = WorldBounds == null
                    ? MiddleDragTargetOrigin + MiddleDragOrigin - currentPoint
                    : CameraFramingMath.DragTarget(
                        MiddleDragTargetOrigin,
                        MiddleDragOrigin,
                        currentPoint,
                        WorldBounds.bounds);
                ClampToWorld();
            }

            if (Mouse.current.middleButton.wasReleasedThisFrame)
            {
                IsMiddleDragging = false;
            }
        }

        private bool CanConsumePointerInput(out Vector2 displayPosition)
        {
            displayPosition = default;
            if (Mouse.current == null || ApplicationFocusProvider == null || !ApplicationFocusProvider())
            {
                return false;
            }

            if (OutputCamera == null)
            {
                OutputCamera = Camera.main;
                if (OutputCamera == null)
                {
                    return false;
                }
            }

            Vector2 screenPosition = Mouse.current.position.ReadValue();
            if (!TryGetPointerPositionOnOutputDisplay(screenPosition, out displayPosition))
            {
                return false;
            }

            return CameraFramingMath.IsScreenPositionInside(displayPosition, OutputCamera.pixelRect);
        }

        private bool TryGetPointerPositionOnOutputDisplay(Vector2 screenPosition, out Vector2 displayPosition)
        {
            if (OutputCamera.targetDisplay == 0)
            {
                displayPosition = screenPosition;
                return true;
            }

            Vector3 relativePosition = Display.RelativeMouseAt(screenPosition);
            if (Mathf.RoundToInt(relativePosition.z) != OutputCamera.targetDisplay)
            {
                displayPosition = default;
                return false;
            }

            displayPosition = new Vector2(relativePosition.x, relativePosition.y);
            return true;
        }

        private bool TryProjectMouseToTargetPlane(Vector2 displayPosition, out Vector3 point)
        {
            Plane plane = new(Vector3.up, new Vector3(0f, FollowTarget.position.y, 0f));
            Ray ray = OutputCamera.ScreenPointToRay(displayPosition);
            if (plane.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }

            point = default;
            return false;
        }

        private void ApplyPan(Vector3 worldDelta)
        {
            FollowTarget.position = WorldBounds == null
                ? FollowTarget.position + new Vector3(worldDelta.x, 0f, worldDelta.z)
                : CameraFramingMath.PanTarget(FollowTarget.position, worldDelta, WorldBounds.bounds);
            ClampToWorld();
        }

        private void ApplyZoom(float wheelDelta)
        {
            ZoomScale = CameraFramingMath.NextZoomScale(
                ZoomScale,
                wheelDelta,
                ZoomSensitivity,
                MinimumZoomScale,
                MaximumZoomScale);
            CinemachineFollow.FollowOffset = CameraFramingMath.ScaleFollowOffset(HomeFollowOffset, ZoomScale);
            ClampToWorld();
        }

        private void ApplyMiddleDragWorldDelta(Vector3 worldDelta)
        {
            ApplyPan(worldDelta);
        }

        private void ResetHome()
        {
            FollowTarget.position = HomeTargetPosition;
            ZoomScale = 1f;
            CinemachineFollow.FollowOffset = HomeFollowOffset;
            IsMiddleDragging = false;
            ClampToWorld();
        }

        private void ClampToWorld()
        {
            if (FollowTarget != null && WorldBounds != null)
            {
                FollowTarget.position = CameraFramingMath.ClampTarget(FollowTarget.position, WorldBounds.bounds);
            }
        }

    }

}
