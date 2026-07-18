using System;
using HighlightPlus;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace DreamVR.Assembly
{
    [DisallowMultipleComponent]
    public sealed class AssemblyPart : MonoBehaviour
    {
        [SerializeField] private int _childIndex;
        [SerializeField] private int _round;
        [SerializeField] private Vector3 _localDirection = Vector3.forward;
        [SerializeField, Min(0.001f)] private float _maxDistance = 0.1f;
        [SerializeField, Range(0.8f, 1f)] private float _completionThreshold = 0.98f;

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

        [Header("碰撞策略")]
        [SerializeField] private Transform _collisionRoot;
        [SerializeField] private bool _ignoreInternalAssemblyCollisions;

        private bool _interactionEnabled;
        private bool _guidanceHighlighted;
        private bool _completed;

        public event Action<AssemblyPart> ReleasedAtEnd;

        public int ChildIndex => _childIndex;

        public int Round => _round;

        public Vector3 LocalDirection => _localDirection;

        public float MaxDistance => _maxDistance;

        public bool IsCompleted => _completed;

        public bool InteractionEnabled => _interactionEnabled;

        public float Progress
        {
            get
            {
                if (!_hasInitialPose || _maxDistance <= Mathf.Epsilon)
                {
                    return 0f;
                }

                float distance = Vector3.Dot(transform.localPosition - _initialLocalPosition, _localDirection);
                return Mathf.Clamp01(distance / _maxDistance);
            }
        }

        private void Awake()
        {
            if (!_hasInitialPose)
            {
                CaptureInitialPose();
            }
        }

        private void OnEnable()
        {
            ApplyCollisionPolicy();

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

            SetHighlight(false);
        }

        private void LateUpdate()
        {
            if (!_hasInitialPose)
            {
                return;
            }

            transform.localPosition = ConstrainLocalPosition(
                _initialLocalPosition,
                transform.localPosition,
                _localDirection,
                _maxDistance);
            transform.localRotation = _initialLocalRotation;
        }

        public void Configure(
            int childIndex,
            int round,
            Vector3 localDirection,
            float maxDistance,
            Rigidbody rigidbody,
            Grabbable grabbable,
            GrabInteractable controllerGrab,
            HandGrabInteractable handGrab,
            HighlightEffect highlightEffect)
        {
            Configure(
                childIndex,
                round,
                localDirection,
                maxDistance,
                _completionThreshold,
                rigidbody,
                grabbable,
                controllerGrab,
                handGrab,
                highlightEffect,
                transform.parent,
                ignoreInternalAssemblyCollisions: false);
        }

        public void Configure(
            int childIndex,
            int round,
            Vector3 localDirection,
            float maxDistance,
            float completionThreshold,
            Rigidbody rigidbody,
            Grabbable grabbable,
            GrabInteractable controllerGrab,
            HandGrabInteractable handGrab,
            HighlightEffect highlightEffect,
            Transform collisionRoot,
            bool ignoreInternalAssemblyCollisions)
        {
            _childIndex = childIndex;
            _round = round;
            _localDirection = localDirection.normalized;
            _maxDistance = Mathf.Max(0.001f, maxDistance);
            _completionThreshold = Mathf.Clamp(completionThreshold, 0.8f, 1f);
            _rigidbody = rigidbody;
            _grabbable = grabbable;
            _controllerGrab = controllerGrab;
            _handGrab = handGrab;
            _highlightEffect = highlightEffect;
            _collisionRoot = collisionRoot;
            _ignoreInternalAssemblyCollisions = ignoreInternalAssemblyCollisions;
            CaptureInitialPose();
            ApplyCollisionPolicy();
        }

        public void CaptureInitialPose()
        {
            _initialLocalPosition = transform.localPosition;
            _initialLocalRotation = transform.localRotation;
            _hasInitialPose = true;
        }

        public void SetInteractionEnabled(bool enabled)
        {
            _interactionEnabled = enabled && !_completed;

            if (_controllerGrab != null)
            {
                _controllerGrab.enabled = _interactionEnabled;
            }

            if (_handGrab != null)
            {
                _handGrab.enabled = _interactionEnabled;
            }

            RefreshHighlight();
        }

        public void SetGuidanceHighlighted(bool highlighted)
        {
            _guidanceHighlighted = highlighted;
            RefreshHighlight();
        }

        public void MarkCompleted(bool completed)
        {
            _completed = completed;
            if (completed)
            {
                SetInteractionEnabled(false);
            }
        }

        public void ResetPart()
        {
            _completed = false;
            _guidanceHighlighted = false;
            SetInteractionEnabled(false);

            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }

            if (_hasInitialPose)
            {
                transform.localPosition = _initialLocalPosition;
                transform.localRotation = _initialLocalRotation;
            }

            SetHighlight(false);
        }

        public bool EvaluateCompletionAfterRelease()
        {
            if (!_interactionEnabled || _completed || Progress < _completionThreshold)
            {
                return false;
            }

            ReleasedAtEnd?.Invoke(this);
            return true;
        }

        /// <summary>
        /// Movable assembly parts must not physically block each other while following their prescribed axis.
        /// Hand and controller colliders are outside this root and remain unaffected.
        /// </summary>
        public void ApplyCollisionPolicy()
        {
            if (_collisionRoot == null)
            {
                return;
            }

            Collider[] ownColliders = GetComponentsInChildren<Collider>(includeInactive: true);
            if (ownColliders.Length == 0)
            {
                return;
            }

            var ownSet = new System.Collections.Generic.HashSet<Collider>(ownColliders);
            foreach (Collider ownCollider in ownColliders)
            {
                if (ownCollider == null)
                {
                    continue;
                }

                foreach (Collider assemblyCollider in
                         _collisionRoot.GetComponentsInChildren<Collider>(includeInactive: true))
                {
                    if (assemblyCollider == null || ownSet.Contains(assemblyCollider))
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(
                        ownCollider,
                        assemblyCollider,
                        _ignoreInternalAssemblyCollisions);
                }
            }
        }

        public static Vector3 ConstrainLocalPosition(
            Vector3 initialPosition,
            Vector3 candidatePosition,
            Vector3 localDirection,
            float maxDistance)
        {
            Vector3 direction = localDirection.sqrMagnitude > Mathf.Epsilon
                ? localDirection.normalized
                : Vector3.forward;
            float distance = Vector3.Dot(candidatePosition - initialPosition, direction);
            distance = Mathf.Clamp(distance, 0f, Mathf.Max(0f, maxDistance));
            return initialPosition + direction * distance;
        }

        private void HandleInteractableStateChanged(InteractableStateChangeArgs _)
        {
            RefreshHighlight();
        }

        private void HandlePointerEvent(PointerEvent pointerEvent)
        {
            if ((pointerEvent.Type == PointerEventType.Unselect || pointerEvent.Type == PointerEventType.Cancel)
                && EvaluateCompletionAfterRelease())
            {
                RefreshHighlight();
            }
        }

        private void RefreshHighlight()
        {
            bool contactHighlight = _interactionEnabled
                && (IsHoveringOrSelected(_controllerGrab) || IsHoveringOrSelected(_handGrab));
            SetHighlight(contactHighlight || _guidanceHighlighted);
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
