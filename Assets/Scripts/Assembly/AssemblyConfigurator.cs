using HighlightPlus;
using Oculus.Interaction;
using UnityEngine;
using VInspector;

#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
#endif

namespace DreamVR.Assembly
{
    public enum AssemblyColliderMode
    {
        ExistingOrBox = 0,
        BoxBounds = 1,
        ConvexMesh = 2
    }

    /// <summary>
    /// VInspector-facing setup component for a complete disassembly model root.
    /// Attach it to the model root, assign a plan (or use automatic discovery), then use its buttons.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AssemblyConfigurator : MonoBehaviour
    {
        [Header("拆卸数据")]
        [SerializeField] private TextAsset _planAsset;
        [SerializeField] private Texture2D _referenceImage;
        [SerializeField] private bool _findPlanNextToModel = true;
        [SerializeField] private bool _saveSceneAfterConfigure = true;

        [Header("撤回按钮")]
        [SerializeField] private InteractableUnityEventWrapper _undoButton;

        [Header("实验条件")]
        [SerializeField] private InteractionExperimentCondition _condition =
            InteractionExperimentCondition.NoGuidance;

        [Header("自由抓取")]
        [SerializeField] private bool _disablePhysicalCollisions = true;
        [SerializeField] private AssemblyColliderMode _colliderMode = AssemblyColliderMode.ConvexMesh;
        [SerializeField, Min(0f)] private float _minimumOperationDistance = 0.005f;
        [SerializeField, Min(0f)] private float _minimumOperationAngle = 2f;

        [Header("高亮")]
        [SerializeField] private Color _contactOutlineColor = new(0.05f, 0.5f, 1f, 1f);
        [SerializeField] private Color _guidanceOutlineColor = new(0.2f, 1f, 0.35f, 1f);
        [SerializeField] private Color _completedPartOutlineColor = new(1f, 0.55f, 0.1f, 1f);
        [SerializeField, Min(0f)] private float _outlineWidth = 0.35f;
        [SerializeField, Range(0.01f, 5f)] private float _seeThroughIntensity = 0.8f;
        [SerializeField, Range(0f, 1f)] private float _seeThroughTintAlpha = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _seeThroughBorder = 0.8f;
        [SerializeField, Min(0f)] private float _seeThroughBorderWidth = 0.45f;

        [Header("方向箭头（Shapes）")]
        [SerializeField] private Color _directionArrowColor = new(1f, 0.72f, 0.2f, 0.55f);
        [SerializeField, Min(0.01f)] private float _directionArrowLengthMultiplier = 0.55f;
        [SerializeField, Min(0.01f)] private float _directionArrowMinimumLength = 0.05f;
        [SerializeField, Min(0f)] private float _directionArrowOffsetMultiplier = 0.08f;
        [SerializeField, Min(0.001f)] private float _directionArrowThicknessRatio = 0.0225f;
        [SerializeField, Range(0.1f, 0.6f)] private float _directionArrowHeadLengthRatio = 0.24f;
        [SerializeField, Min(0.01f)] private float _directionArrowHeadRadiusRatio = 0.075f;

        public TextAsset PlanAsset => _planAsset;
        public Texture2D ReferenceImage => _referenceImage;
        public bool FindPlanNextToModel => _findPlanNextToModel;
        public bool SaveSceneAfterConfigure => _saveSceneAfterConfigure;
        public InteractableUnityEventWrapper UndoButton => _undoButton;
        public InteractionExperimentCondition Condition => _condition;
        public bool DisablePhysicalCollisions => _disablePhysicalCollisions;
        public AssemblyColliderMode ColliderMode => _colliderMode;
        public float MinimumOperationDistance => _minimumOperationDistance;
        public float MinimumOperationAngle => _minimumOperationAngle;
        public Color ContactOutlineColor => _contactOutlineColor;
        public Color GuidanceOutlineColor => _guidanceOutlineColor;
        public Color CompletedPartOutlineColor => _completedPartOutlineColor;
        public float OutlineWidth => _outlineWidth;
        public float SeeThroughIntensity => _seeThroughIntensity;
        public float SeeThroughTintAlpha => _seeThroughTintAlpha;
        public float SeeThroughBorder => _seeThroughBorder;
        public float SeeThroughBorderWidth => _seeThroughBorderWidth;
        public Color DirectionArrowColor => _directionArrowColor;
        public float DirectionArrowLengthMultiplier => _directionArrowLengthMultiplier;
        public float DirectionArrowMinimumLength => _directionArrowMinimumLength;
        public float DirectionArrowOffsetMultiplier => _directionArrowOffsetMultiplier;
        public float DirectionArrowThicknessRatio => _directionArrowThicknessRatio;
        public float DirectionArrowHeadLengthRatio => _directionArrowHeadLengthRatio;
        public float DirectionArrowHeadRadiusRatio => _directionArrowHeadRadiusRatio;

#if UNITY_EDITOR
        [Button("一键配置拆卸零件")]
        public void ConfigureFromInspector()
        {
            InvokeEditorBackend("Configure");
        }

        [Button("验证当前配置")]
        public void ValidateFromInspector()
        {
            InvokeEditorBackend("Validate");
        }

        [Button("重新应用无碰撞策略")]
        public void ReapplyCollisionPolicy()
        {
            foreach (AssemblyPart part in GetComponentsInChildren<AssemblyPart>(includeInactive: true))
            {
                part.ApplyCollisionPolicy();
            }

            Debug.Log("[DreamVR] 已重新应用无物理碰撞策略。", this);
        }

        [Button("撤回上一次操作")]
        public void UndoLastOperation()
        {
            AssemblyController controller = GetComponent<AssemblyController>();
            if (controller == null)
            {
                Debug.LogWarning("[DreamVR] 尚未配置 AssemblyController。", this);
                return;
            }

            controller.UndoLastOperation();
        }

        [Button("初始化所有零件")]
        public void ResetAllParts()
        {
            AssemblyController controller = GetComponent<AssemblyController>();
            if (controller == null)
            {
                Debug.LogWarning("[DreamVR] 尚未配置 AssemblyController。", this);
                return;
            }

            controller.ResetAllImmediate();
            EditorUtility.SetDirty(controller);
        }

        private void InvokeEditorBackend(string methodName)
        {
            Type backendType = Type.GetType(
                "DreamVR.Assembly.Editor.AssemblyConfiguratorEditorBackend, DreamVR.Assembly.Editor");
            MethodInfo method = backendType?.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "DreamVR 装配配置后端未加载。请等待 Unity 编译完成后重试。");
            }

            try
            {
                method.Invoke(null, new object[] { this });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }
#endif
    }
}
