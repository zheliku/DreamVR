using HighlightPlus;
using UnityEngine;
using VInspector;

#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
#endif

namespace DreamVR.Assembly
{
    /// <summary>
    /// VInspector-facing setup component for a complete disassembly model root.
    /// Attach it to the model root, assign a plan (or use automatic discovery), then use its buttons.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AssemblyConfigurator : MonoBehaviour
    {
        [Header("拆卸数据")]
        [SerializeField] private TextAsset _planAsset;
        [SerializeField] private bool _findPlanNextToModel = true;
        [SerializeField] private bool _saveSceneAfterConfigure = true;

        [Header("实验条件")]
        [SerializeField] private InteractionExperimentCondition _condition =
            InteractionExperimentCondition.NoGuidance;

        [Header("行程余量")]
        [SerializeField, Min(0f)] private float _partSizeClearanceMultiplier = 0.35f;
        [SerializeField, Min(0f)] private float _assemblySizeClearanceMultiplier = 0.08f;
        [SerializeField, Min(0f)] private float _additionalClearance = 0.08f;
        [SerializeField, Min(0.001f)] private float _minimumTravelDistance = 0.25f;
        [SerializeField, Min(1f)] private float _travelDistanceMultiplier = 1.15f;

        [Header("碰撞与抓取")]
        [SerializeField] private bool _ignoreInternalAssemblyCollisions = true;
        [SerializeField] private bool _createBoxColliderWhenMissing = true;
        [SerializeField, Range(0.8f, 1f)] private float _completionThreshold = 0.92f;

        [Header("高亮")]
        [SerializeField] private Color _contactOutlineColor = new(0.15f, 0.85f, 1f, 1f);
        [SerializeField, Min(0f)] private float _outlineWidth = 0.35f;

        public TextAsset PlanAsset => _planAsset;
        public bool FindPlanNextToModel => _findPlanNextToModel;
        public bool SaveSceneAfterConfigure => _saveSceneAfterConfigure;
        public InteractionExperimentCondition Condition => _condition;
        public float PartSizeClearanceMultiplier => _partSizeClearanceMultiplier;
        public float AssemblySizeClearanceMultiplier => _assemblySizeClearanceMultiplier;
        public float AdditionalClearance => _additionalClearance;
        public float MinimumTravelDistance => _minimumTravelDistance;
        public float TravelDistanceMultiplier => _travelDistanceMultiplier;
        public bool IgnoreInternalAssemblyCollisions => _ignoreInternalAssemblyCollisions;
        public bool CreateBoxColliderWhenMissing => _createBoxColliderWhenMissing;
        public float CompletionThreshold => _completionThreshold;
        public Color ContactOutlineColor => _contactOutlineColor;
        public float OutlineWidth => _outlineWidth;

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

        [Button("重新应用内部防碰撞")]
        public void ReapplyCollisionPolicy()
        {
            foreach (AssemblyPart part in GetComponentsInChildren<AssemblyPart>(includeInactive: true))
            {
                part.ApplyCollisionPolicy();
            }

            Debug.Log("[DreamVR] 已重新应用内部碰撞策略。", this);
        }

        [Button("恢复所有零件")]
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
