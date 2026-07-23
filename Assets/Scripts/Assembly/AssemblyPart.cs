using System;
using HighlightPlus;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace DreamVR.Assembly
{
    public readonly struct AssemblyPartPose
    {
        public AssemblyPartPose(Vector3 localPosition, Quaternion localRotation)
        {
            LocalPosition = localPosition;
            LocalRotation = localRotation;
        }

        public Vector3 LocalPosition { get; }

        public Quaternion LocalRotation { get; }
    }

    [DisallowMultipleComponent]
    public sealed class AssemblyPart : MonoBehaviour
    {
        [SerializeField, Min(1)] private int _partNumber = 1;
        [SerializeField, Min(0)] private int _childIndex;
        [SerializeField, Min(1)] private int _round = 1;
        [SerializeField] private Vector3 _hintLocalDirection = Vector3.forward;
        [SerializeField, Min(0f)] private float _minimumOperationDistance = 0.005f;
        [SerializeField, Min(0f)] private float _minimumOperationAngle = 2f;

        [Header("初始姿态")]
        [SerializeField] private bool _hasInitialPose;
        [SerializeField] private Vector3 _initialLocalPosition;
        [SerializeField] private Quaternion _initialLocalRotation = Quaternion.identity;

        [Header("交互组件")]
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private Grabbable _grabbable;
        [SerializeField] private GrabInteractable _controllerGrab;
        [SerializeField] private HandGrabInteractable _handGrab;
        [SerializeField] private HighlightEffect _highlightEffect;
        [SerializeField] private AssemblyDirectionIndicator _directionIndicator;
        [SerializeField] private Color _activeOutlineColor = new(0.05f, 0.5f, 1f, 1f);
        [SerializeField] private Color _guidanceOutlineColor = new(0.2f, 1f, 0.35f, 1f);
        [SerializeField] private Color _completedOutlineColor = new(1f, 0.55f, 0.1f, 1f);

        [Header("碰撞策略")]
        [SerializeField] private bool _disablePhysicalCollisions = true;

        private bool _interactionEnabled;
        private bool _guidanceHighlighted;
        private bool _directionGuidanceVisible;
        private bool _completed;
        private bool _completedInteractionAppearance;
        private bool _hasPendingOperation;
        private bool _suspendingInteraction;
        private bool _grabbableWasEnabledBeforeSuspension;
        private AssemblyPartPose _operationStartPose;

        public event Action<AssemblyPart, AssemblyPartPose, AssemblyPartPose> OperationCommitted;

        public int PartNumber => _partNumber;

        public int ChildIndex => _childIndex;

        public int Round => _round;

        public Vector3 HintLocalDirection => _hintLocalDirection;

        public bool IsCompleted => _completed;

        public bool InteractionEnabled => _interactionEnabled;

        public bool HasPendingOperation => _hasPendingOperation;

        public bool IsInteractionSuspended => _suspendingInteraction;

        public bool GuidanceHighlighted => _guidanceHighlighted;

        public bool DirectionGuidanceVisible => _directionGuidanceVisible;

        private void Awake()
        {
            if (!_hasInitialPose)
            {
                CaptureInitialPose();
            }
        }

        private void OnEnable()
        {
            if (_controllerGrab != null)
            {
                _controllerGrab.WhenStateChanged += HandleInteractableStateChanged;
            }

            if (_handGrab != null)
            {
                _handGrab.WhenStateChanged += HandleInteractableStateChanged;
            }

            if (_grabbable != null)
            {
                _grabbable.WhenPointerEventRaised += HandlePointerEvent;
            }

            _directionIndicator?.SetGuidanceVisible(_directionGuidanceVisible);
            RefreshHighlight();
        }

        private void OnDisable()
        {
            if (_controllerGrab != null)
            {
                _controllerGrab.WhenStateChanged -= HandleInteractableStateChanged;
            }

            if (_handGrab != null)
            {
                _handGrab.WhenStateChanged -= HandleInteractableStateChanged;
            }

            if (_grabbable != null)
            {
                _grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            }

            if (!_suspendingInteraction)
            {
                CancelPendingOperation(restoreStartPose: true);
            }

            _directionIndicator?.SetGuidanceVisible(false);
            SetHighlight(false);
        }

        public void Configure(
            int partNumber,
            int childIndex,
            int round,
            Vector3 hintLocalDirection,
            float minimumOperationDistance,
            float minimumOperationAngle,
            Rigidbody rigidbody,
            Grabbable grabbable,
            GrabInteractable controllerGrab,
            HandGrabInteractable handGrab,
            HighlightEffect highlightEffect,
            AssemblyDirectionIndicator directionIndicator,
            bool disablePhysicalCollisions,
            Color activeOutlineColor,
            Color guidanceOutlineColor,
            Color completedOutlineColor,
            float seeThroughIntensity,
            float seeThroughTintAlpha,
            float seeThroughBorder,
            float seeThroughBorderWidth)
        {
            _partNumber = Mathf.Max(1, partNumber);
            _childIndex = Mathf.Max(0, childIndex);
            _round = Mathf.Max(1, round);
            _hintLocalDirection = hintLocalDirection.sqrMagnitude > Mathf.Epsilon
                ? hintLocalDirection.normalized
                : Vector3.forward;
            _minimumOperationDistance = Mathf.Max(0f, minimumOperationDistance);
            _minimumOperationAngle = Mathf.Max(0f, minimumOperationAngle);
            _rigidbody = rigidbody;
            _grabbable = grabbable;
            _controllerGrab = controllerGrab;
            _handGrab = handGrab;
            _highlightEffect = highlightEffect;
            _directionIndicator = directionIndicator;
            _disablePhysicalCollisions = disablePhysicalCollisions;
            _activeOutlineColor = activeOutlineColor;
            _guidanceOutlineColor = guidanceOutlineColor;
            _completedOutlineColor = completedOutlineColor;
            ConfigureSeeThroughHighlight(
                seeThroughIntensity,
                seeThroughTintAlpha,
                seeThroughBorder,
                seeThroughBorderWidth);
            CaptureInitialPose();
            ApplyCollisionPolicy();
        }

        public void CaptureInitialPose()
        {
            _initialLocalPosition = transform.localPosition;
            _initialLocalRotation = transform.localRotation;
            _hasInitialPose = true;
        }

        public AssemblyPartPose CaptureCurrentPose()
        {
            return new AssemblyPartPose(transform.localPosition, transform.localRotation);
        }

        public bool BeginOperationRecording()
        {
            if (!_interactionEnabled || _suspendingInteraction)
            {
                return false;
            }

            _operationStartPose = CaptureCurrentPose();
            _hasPendingOperation = true;
            return true;
        }

        public bool CompleteOperationRecording()
        {
            if (!_hasPendingOperation || _suspendingInteraction)
            {
                return false;
            }

            AssemblyPartPose before = _operationStartPose;
            AssemblyPartPose after = CaptureCurrentPose();
            _hasPendingOperation = false;

            float positionDelta = Vector3.Distance(before.LocalPosition, after.LocalPosition);
            float rotationDelta = Quaternion.Angle(before.LocalRotation, after.LocalRotation);
            if (positionDelta < _minimumOperationDistance
                && rotationDelta < _minimumOperationAngle)
            {
                RestorePose(before);
                return false;
            }

            ClearVelocities();
            OperationCommitted?.Invoke(this, before, after);
            return true;
        }

        public void CancelPendingOperation(bool restoreStartPose)
        {
            if (!_hasPendingOperation)
            {
                return;
            }

            AssemblyPartPose startPose = _operationStartPose;
            _hasPendingOperation = false;
            if (restoreStartPose)
            {
                RestorePose(startPose);
            }
        }

        public void RestorePose(AssemblyPartPose pose)
        {
            ClearVelocities();
            transform.localPosition = pose.LocalPosition;
            transform.localRotation = pose.LocalRotation;
            Physics.SyncTransforms();
        }

        public void SetInteractionEnabled(bool enabled)
        {
            _interactionEnabled = enabled;

            if (_controllerGrab != null)
            {
                _controllerGrab.enabled = enabled;
            }

            if (_handGrab != null)
            {
                _handGrab.enabled = enabled;
            }

            RefreshHighlight();
        }

        public void SetGuidanceHighlighted(bool highlighted)
        {
            _guidanceHighlighted = highlighted;
            RefreshHighlight();
        }

        public void SetDirectionGuidanceVisible(bool visible)
        {
            _directionGuidanceVisible = visible;
            _directionIndicator?.SetGuidanceVisible(visible);
        }

        public void SetCompletedInteractionAppearance(bool enabled)
        {
            _completedInteractionAppearance = enabled && _completed;
            RefreshHighlight();
        }

        public void MarkCompleted(bool completed)
        {
            _completed = completed;
            if (!completed)
            {
                _completedInteractionAppearance = false;
            }

            RefreshHighlight();
        }

        public void ResetPart()
        {
            _completed = false;
            _completedInteractionAppearance = false;
            _guidanceHighlighted = false;
            _directionGuidanceVisible = false;
            _hasPendingOperation = false;
            SetInteractionEnabled(false);
            _directionIndicator?.SetGuidanceVisible(false);

            if (_hasInitialPose)
            {
                RestorePose(new AssemblyPartPose(_initialLocalPosition, _initialLocalRotation));
            }

            SetHighlight(false);
        }

        public void SuspendInteractionForPoseRestore()
        {
            _suspendingInteraction = true;
            SetInteractionEnabled(false);
            SetGuidanceHighlighted(false);
            SetDirectionGuidanceVisible(false);
            if (_grabbable != null)
            {
                _grabbableWasEnabledBeforeSuspension = _grabbable.enabled;
                _grabbable.enabled = false;
            }
        }

        public void RestoreInteractionAfterPoseRestore()
        {
            if (_grabbable != null)
            {
                _grabbable.enabled = _grabbableWasEnabledBeforeSuspension;
            }

            _suspendingInteraction = false;
            ClearVelocities();
            RefreshHighlight();
        }

        public void ApplyCollisionPolicy()
        {
            foreach (Collider collider in GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (collider != null)
                {
                    collider.isTrigger = _disablePhysicalCollisions;
                }
            }

            if (_rigidbody != null)
            {
                // Meta scores the colliders directly, so collision detection stays enabled while
                // trigger colliders remove all physical contact response.
                _rigidbody.detectCollisions = true;
            }
        }

        private void ClearVelocities()
        {
            if (_rigidbody == null)
            {
                return;
            }

            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        private void HandleInteractableStateChanged(InteractableStateChangeArgs _)
        {
            RefreshHighlight();
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            switch (pointerEvent.Type)
            {
                case PointerEventType.Select:
                    BeginOperationRecording();
                    break;
                case PointerEventType.Unselect:
                    CompleteOperationRecording();
                    break;
                case PointerEventType.Cancel:
                    if (!_suspendingInteraction)
                    {
                        CancelPendingOperation(restoreStartPose: true);
                    }
                    break;
            }

            RefreshHighlight();
        }

        private void RefreshHighlight()
        {
            bool contactHighlight = _interactionEnabled
                && (IsHoveringOrSelected(_controllerGrab) || IsHoveringOrSelected(_handGrab));
            ApplyHighlightStyle(contactHighlight);
            SetHighlight(contactHighlight || _guidanceHighlighted);
        }

        private void ApplyHighlightStyle(bool contactHighlight)
        {
            if (_highlightEffect == null)
            {
                return;
            }

            Color stateColor = contactHighlight
                ? (_completedInteractionAppearance
                    ? _completedOutlineColor
                    : _activeOutlineColor)
                : (_guidanceHighlighted
                    ? _guidanceOutlineColor
                    : _activeOutlineColor);
            _highlightEffect.outlineColor = stateColor;
            _highlightEffect.seeThroughTintColor = stateColor;
            _highlightEffect.seeThroughBorderColor = stateColor;
            _highlightEffect.UpdateMaterialProperties();
        }

        private void ConfigureSeeThroughHighlight(
            float intensity,
            float tintAlpha,
            float border,
            float borderWidth)
        {
            if (_highlightEffect == null)
            {
                return;
            }

            _highlightEffect.seeThrough = SeeThroughMode.WhenHighlighted;
            _highlightEffect.seeThroughOccluderMask = -1;
            _highlightEffect.seeThroughOccluderMaskAccurate = false;
            _highlightEffect.seeThroughDepthOffset = 0f;
            _highlightEffect.seeThroughMaxDepth = 0f;
            _highlightEffect.seeThroughFadeRange = 0f;
            _highlightEffect.seeThroughIntensity = Mathf.Clamp(intensity, 0.01f, 5f);
            _highlightEffect.seeThroughTintAlpha = Mathf.Clamp01(tintAlpha);
            _highlightEffect.seeThroughNoise = 0f;
            _highlightEffect.seeThroughBorder = Mathf.Clamp01(border);
            _highlightEffect.seeThroughBorderWidth = Mathf.Max(0f, borderWidth);
            _highlightEffect.seeThroughBorderOnly = false;
            _highlightEffect.seeThroughOrdered = true;
        }

        private static bool IsHoveringOrSelected(IInteractableView interactable)
        {
            return interactable != null
                && (interactable.State == InteractableState.Hover
                    || interactable.State == InteractableState.Select);
        }

        private void SetHighlight(bool highlighted)
        {
            if (_highlightEffect != null && _highlightEffect.highlighted != highlighted)
            {
                _highlightEffect.SetHighlighted(highlighted);
            }
        }
    }
}
