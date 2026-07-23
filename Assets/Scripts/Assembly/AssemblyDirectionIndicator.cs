using Shapes;
using UnityEngine;
using UnityEngine.Rendering;

namespace DreamVR.Assembly
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class AssemblyDirectionIndicator : MonoBehaviour
    {
        [SerializeField] private Transform _directionSpace;
        [SerializeField] private Vector3 _localDirection = Vector3.forward;
        [SerializeField] private Vector3 _anchorLocalPosition;
        [SerializeField] private Line _shaft;
        [SerializeField] private Cone _head;
        [SerializeField] private Color _color = new(1f, 0.72f, 0.2f, 0.55f);
        [SerializeField, Min(0.001f)] private float _length = 0.18f;
        [SerializeField, Min(0f)] private float _anchorOffset = 0.03f;
        [SerializeField, Min(0.0001f)] private float _shaftThickness = 0.008f;
        [SerializeField, Min(0.001f)] private float _headLength = 0.05f;
        [SerializeField, Min(0.001f)] private float _headRadius = 0.02f;
        [SerializeField] private bool _guidanceVisible;

        public Transform DirectionSpace => _directionSpace;

        public Vector3 LocalDirection => _localDirection;

        public Line Shaft => _shaft;

        public Cone Head => _head;

        public bool GuidanceVisible => _guidanceVisible;

        public void Configure(
            Transform directionSpace,
            Vector3 localDirection,
            Vector3 anchorLocalPosition,
            Line shaft,
            Cone head,
            Color color,
            float length,
            float anchorOffset,
            float shaftThickness,
            float headLength,
            float headRadius)
        {
            _directionSpace = directionSpace;
            _localDirection = localDirection.sqrMagnitude > Mathf.Epsilon
                ? localDirection.normalized
                : Vector3.forward;
            _anchorLocalPosition = anchorLocalPosition;
            _shaft = shaft;
            _head = head;
            _color = color;
            _length = Mathf.Max(0.001f, length);
            _anchorOffset = Mathf.Max(0f, anchorOffset);
            _shaftThickness = Mathf.Max(0.0001f, shaftThickness);
            _headLength = Mathf.Clamp(headLength, 0.001f, _length * 0.8f);
            _headRadius = Mathf.Max(0.001f, headRadius);

            ConfigureShapeComponents();
            SetGuidanceVisible(false);
        }

        public void SetGuidanceVisible(bool visible)
        {
            _guidanceVisible = visible;
            enabled = visible;
            SetShapeComponentsEnabled(visible);
            if (visible)
            {
                RefreshVisualPose();
            }
        }

        private void OnEnable()
        {
            SetShapeComponentsEnabled(_guidanceVisible);
            if (_guidanceVisible)
            {
                RefreshVisualPose();
            }
        }

        private void OnDisable()
        {
            SetShapeComponentsEnabled(false);
        }

        private void LateUpdate()
        {
            if (_guidanceVisible)
            {
                RefreshVisualPose();
            }
        }

        private void ConfigureShapeComponents()
        {
            if (_shaft != null)
            {
                _shaft.BlendMode = ShapesBlendMode.Transparent;
                _shaft.Geometry = LineGeometry.Volumetric3D;
                _shaft.ThicknessSpace = ThicknessSpace.Meters;
                _shaft.EndCaps = LineEndCap.Round;
                _shaft.Thickness = _shaftThickness;
                _shaft.Color = _color;
                _shaft.ZTest = CompareFunction.Always;
                _shaft.SortingOrder = 1000;
            }

            if (_head != null)
            {
                _head.BlendMode = ShapesBlendMode.Transparent;
                _head.SizeSpace = ThicknessSpace.Meters;
                _head.FillCap = true;
                _head.Radius = _headRadius;
                _head.Length = _headLength;
                _head.Color = _color;
                _head.ZTest = CompareFunction.Always;
                _head.SortingOrder = 1001;
            }
        }

        private void SetShapeComponentsEnabled(bool visible)
        {
            if (_shaft != null)
            {
                _shaft.enabled = visible;
            }

            if (_head != null)
            {
                _head.enabled = visible;
            }
        }

        private void RefreshVisualPose()
        {
            if (_directionSpace == null || _shaft == null || _head == null)
            {
                return;
            }

            Vector3 worldDirection = _directionSpace
                .TransformDirection(_localDirection)
                .normalized;
            Vector3 start = transform.TransformPoint(_anchorLocalPosition)
                + worldDirection * _anchorOffset;
            float headLength = Mathf.Min(_headLength, _length * 0.8f);
            Vector3 headBase = start + worldDirection * (_length - headLength);

            _shaft.Start = _shaft.transform.InverseTransformPoint(start);
            _shaft.End = _shaft.transform.InverseTransformPoint(headBase);
            _shaft.Thickness = _shaftThickness;
            _shaft.Color = _color;

            _head.transform.SetPositionAndRotation(
                headBase,
                Quaternion.LookRotation(worldDirection));
            _head.Radius = _headRadius;
            _head.Length = headLength;
            _head.Color = _color;
        }
    }
}
