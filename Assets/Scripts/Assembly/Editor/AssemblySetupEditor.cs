using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HighlightPlus;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Shapes;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DreamVR.Assembly.Editor
{
    /// <summary>
    /// Editor-only implementation behind AssemblyConfigurator's VInspector buttons.
    /// </summary>
    public static class AssemblyConfiguratorEditorBackend
    {
        private const string DirectionVisualRootName = "__DreamVR_DirectionVisuals";

        public static void Configure(AssemblyConfigurator configurator)
        {
            if (configurator == null)
            {
                throw new ArgumentNullException(nameof(configurator));
            }

            GameObject root = configurator.gameObject;
            string modelPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                throw new InvalidOperationException($"{root.name} 不是模型预制体实例，无法定位配置目录。");
            }

            string directory = Path.GetDirectoryName(modelPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException($"无法解析模型目录：{modelPath}");
            }

            TextAsset planAsset = ResolvePlanAsset(configurator, directory);
            PersistDataReferences(
                configurator,
                planAsset,
                ResolveReferenceImage(directory, planAsset));
            IReadOnlyList<DisassemblyStep> steps = DisassemblyPlanParser.Parse(planAsset.text);
            Transform indexedParent = ResolveIndexedParent(root.transform, steps);
            RemoveStalePartConfigurations(indexedParent, steps);
            Transform directionVisualRoot = RecreateDirectionVisualRoot(root.transform);

            var parts = new List<AssemblyPart>(steps.Count);
            foreach (DisassemblyStep step in steps)
            {
                Transform partTransform = indexedParent.GetChild(step.ChildIndex);
                AssemblyPart part = ConfigurePart(
                    partTransform,
                    step,
                    indexedParent,
                    directionVisualRoot,
                    configurator);
                parts.Add(part);
                Debug.Log(
                    $"[DreamVR] round{step.Round} part[{step.PartNumber}] -> "
                    + $"child[{step.ChildIndex}] {partTransform.name}，自由移动，方向仅作提示数据："
                    + step.HintLocalDirection,
                    partTransform);
            }

            AssemblyController controller = GetOrAddComponent<AssemblyController>(root);
            controller.Configure(parts, configurator.Condition);
            controller.ResetAllImmediate();
            MarkDirty(controller);

            DisableRootWideHighlight(root, parts);
            BindUndoButton(configurator, controller);
            Validate(configurator);

            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorUtility.SetDirty(configurator);
            if (configurator.SaveSceneAfterConfigure)
            {
                EditorSceneManager.SaveScene(root.scene);
                AssetDatabase.SaveAssets();
            }

            Debug.Log(
                $"[DreamVR] 已按 1 基序号配置 {parts.Count} 个自由抓取零件；"
                + $"索引父物体={indexedParent.name}；实验条件={configurator.Condition}；"
                + "BigRedButton 已绑定单步撤回。",
                root);
        }

        public static void Validate(AssemblyConfigurator configurator)
        {
            if (configurator == null)
            {
                throw new ArgumentNullException(nameof(configurator));
            }

            GameObject root = configurator.gameObject;
            var errors = new List<string>();
            AssemblyController controller = root.GetComponent<AssemblyController>();
            if (controller == null)
            {
                throw new InvalidOperationException($"{root.name} 缺少 AssemblyController。");
            }

            string modelPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            string directory = Path.GetDirectoryName(modelPath)?.Replace('\\', '/');
            TextAsset planAsset = ResolvePlanAsset(configurator, directory);
            IReadOnlyList<DisassemblyStep> steps = DisassemblyPlanParser.Parse(planAsset.text);
            Transform indexedParent = ResolveIndexedParent(root.transform, steps);
            Dictionary<int, DisassemblyStep> expected = steps.ToDictionary(step => step.ChildIndex);

            if (controller.Condition != configurator.Condition)
            {
                errors.Add($"实验条件应为 {configurator.Condition}，实际为 {controller.Condition}。");
            }

            int firstRound = steps.Min(step => step.Round);
            if (controller.CurrentRound != firstRound)
            {
                errors.Add($"初始轮次应为 {firstRound}，实际为 {controller.CurrentRound}。");
            }

            if (controller.OperationCount != 0)
            {
                errors.Add("初始操作历史应为空。");
            }

            if (controller.Parts.Count != steps.Count)
            {
                errors.Add($"应配置 {steps.Count} 个零件，实际为 {controller.Parts.Count} 个。");
            }

            if (!controller.Parts.Select(part => part.ChildIndex).ToHashSet().SetEquals(expected.Keys))
            {
                errors.Add("AssemblyController 中的零基子物体下标与 1 基 txt 序号不一致。");
            }

            foreach (AssemblyPart part in controller.Parts)
            {
                if (part == null || !expected.TryGetValue(part.ChildIndex, out DisassemblyStep step))
                {
                    errors.Add("AssemblyController 包含空引用或未知零件。");
                    continue;
                }

                bool isCurrentGuidancePart = part.Round == controller.CurrentRound;
                Rigidbody rigidbody = part.GetComponent<Rigidbody>();
                GrabFreeTransformer transformer = part.GetComponent<GrabFreeTransformer>();
                Grabbable grabbable = part.GetComponent<Grabbable>();
                GrabInteractable controllerGrab = part.GetComponent<GrabInteractable>();
                HandGrabInteractable handGrab = part.GetComponent<HandGrabInteractable>();
                HighlightEffect highlight = part.GetComponent<HighlightEffect>();
                AssemblyDirectionIndicator directionIndicator =
                    part.GetComponent<AssemblyDirectionIndicator>();
                Collider[] colliders = part.GetComponentsInChildren<Collider>(includeInactive: true);

                if (part.transform.parent != indexedParent
                    || part.transform.GetSiblingIndex() != step.ChildIndex)
                {
                    errors.Add($"part[{step.PartNumber}] 没有挂载到 child[{step.ChildIndex}] 上。");
                }

                if (part.PartNumber != step.PartNumber
                    || part.ChildIndex != step.PartNumber - 1
                    || part.Round != step.Round
                    || Vector3.Dot(part.HintLocalDirection, step.HintLocalDirection) < 0.999f)
                {
                    errors.Add($"part[{step.PartNumber}] 的序号、轮次或提示方向与 txt 不一致。");
                }

                if (rigidbody == null
                    || colliders.Length == 0
                    || transformer == null
                    || grabbable == null
                    || controllerGrab == null
                    || handGrab == null
                    || highlight == null
                    || directionIndicator == null
                    || directionIndicator.Shaft == null
                    || directionIndicator.Head == null)
                {
                    errors.Add($"part[{step.PartNumber}] 缺少必需的物理、Meta、高亮或 Shapes 箭头组件。");
                    continue;
                }

                if (part.GetComponent<OneGrabTranslateTransformer>() != null)
                {
                    errors.Add($"part[{step.PartNumber}] 仍残留单轴 OneGrabTranslateTransformer。");
                }

                if (!rigidbody.isKinematic || rigidbody.useGravity || !rigidbody.detectCollisions)
                {
                    errors.Add($"part[{step.PartNumber}] 的 Rigidbody 配置不正确。");
                }

                if (grabbable.MaxGrabPoints != 1)
                {
                    errors.Add($"part[{step.PartNumber}] 应限制为单手抓取。");
                }

                if (configurator.DisablePhysicalCollisions && colliders.Any(collider => !collider.isTrigger))
                {
                    errors.Add($"part[{step.PartNumber}] 存在会产生物理阻挡的非 Trigger Collider。");
                }

                if (highlight.seeThrough != SeeThroughMode.WhenHighlighted
                    || highlight.seeThroughIntensity <= 0f)
                {
                    errors.Add($"part[{step.PartNumber}] 未启用高亮透视效果。");
                }

                if (configurator.ColliderMode == AssemblyColliderMode.ConvexMesh
                    && (!part.GetComponentsInChildren<MeshCollider>(includeInactive: true).Any()
                        || part.GetComponentsInChildren<MeshCollider>(includeInactive: true)
                            .Any(collider => !collider.convex)))
                {
                    errors.Add($"part[{step.PartNumber}] 未正确配置凸 MeshCollider。");
                }

                if (!controllerGrab.enabled || !handGrab.enabled || !part.InteractionEnabled)
                {
                    errors.Add($"part[{step.PartNumber}] 应在所有轮次始终可交互。");
                }

                bool shouldShowGuidance = configurator.Condition
                    != InteractionExperimentCondition.NoGuidance
                    && isCurrentGuidancePart;
                if (highlight.highlighted != shouldShowGuidance)
                {
                    errors.Add($"part[{step.PartNumber}] 的初始高亮状态与实验条件不一致。");
                }

                bool shouldShowDirection = configurator.Condition
                    == InteractionExperimentCondition.CurrentPartHighlightAndDirection
                    && isCurrentGuidancePart;
                if (part.DirectionGuidanceVisible != shouldShowDirection
                    || directionIndicator.GuidanceVisible != shouldShowDirection
                    || directionIndicator.Shaft.enabled != shouldShowDirection
                    || directionIndicator.Head.enabled != shouldShowDirection)
                {
                    errors.Add($"part[{step.PartNumber}] 的方向箭头状态与实验条件不一致。");
                }

                if (directionIndicator.DirectionSpace != indexedParent
                    || Vector3.Dot(
                        directionIndicator.LocalDirection,
                        step.HintLocalDirection) < 0.999f)
                {
                    errors.Add($"part[{step.PartNumber}] 的 Shapes 箭头方向与 txt 不一致。");
                }
            }

            foreach (AssemblyPart unexpectedPart in indexedParent
                         .GetComponentsInChildren<AssemblyPart>(includeInactive: true)
                         .Where(part => part.transform.parent != indexedParent
                             || !expected.ContainsKey(part.transform.GetSiblingIndex())))
            {
                errors.Add($"发现未列入 txt 或不在直接子级上的 AssemblyPart：{unexpectedPart.name}。");
            }

            for (int childIndex = 0; childIndex < indexedParent.childCount; childIndex++)
            {
                if (expected.ContainsKey(childIndex))
                {
                    continue;
                }

                GameObject fixedPart = indexedParent.GetChild(childIndex).gameObject;
                if (HasManagedGrabComponents(fixedPart))
                {
                    errors.Add(
                        $"未列入 txt 的固定件 part[{childIndex + 1}] / child[{childIndex}] "
                        + "不应挂载装配抓取组件。");
                }
            }

            if (root.GetComponents<HighlightEffect>().Any(effect => effect.enabled || effect.highlighted))
            {
                errors.Add("装配根物体的整体 HighlightEffect 未完全关闭。");
            }

            InteractableUnityEventWrapper buttonWrapper = ResolveUndoButton(configurator);
            if (buttonWrapper == null || !HasUndoListener(buttonWrapper, controller))
            {
                errors.Add("BigRedButton 没有持久化绑定 AssemblyController.UndoLastOperation。");
            }

            if (buttonWrapper != null && HasResetListener(buttonWrapper, controller))
            {
                errors.Add("BigRedButton 仍残留 AssemblyController.ResetAll 监听器。");
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException("DreamVR 装配场景验证失败：\n- " + string.Join("\n- ", errors));
            }

            Debug.Log(
                $"[DreamVR] 场景验证通过：{controller.Parts.Count} 个自由抓取零件，"
                + "txt 使用 1 基序号，无物理阻挡，撤回按钮已绑定。",
                root);
        }

        private static TextAsset ResolvePlanAsset(AssemblyConfigurator configurator, string directory)
        {
            if (configurator.PlanAsset != null)
            {
                return configurator.PlanAsset;
            }

            if (!configurator.FindPlanNextToModel)
            {
                throw new InvalidOperationException("未指定拆卸顺序 txt，且已关闭自动查找。");
            }

            TextAsset[] candidates = AssetDatabase.FindAssets("t:TextAsset", new[] { directory })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(
                    Path.GetDirectoryName(path)?.Replace('\\', '/'),
                    directory,
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
                .Select(AssetDatabase.LoadAssetAtPath<TextAsset>)
                .Where(asset => asset != null)
                .ToArray();
            if (candidates.Length != 1)
            {
                throw new InvalidOperationException(
                    $"目录 {directory} 中应当恰好有一个拆卸顺序 txt，实际找到 {candidates.Length} 个。");
            }

            return candidates[0];
        }

        private static Texture2D ResolveReferenceImage(
            string directory,
            TextAsset planAsset)
        {
            Texture2D[] candidates = AssetDatabase.FindAssets("t:Texture2D", new[] { directory })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(
                    Path.GetDirectoryName(path)?.Replace('\\', '/'),
                    directory,
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => string.Equals(
                    Path.GetExtension(path),
                    ".png",
                    StringComparison.OrdinalIgnoreCase))
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                .Where(asset => asset != null)
                .ToArray();
            if (candidates.Length == 1)
            {
                return candidates[0];
            }

            string planName = planAsset != null ? planAsset.name : string.Empty;
            return candidates.FirstOrDefault(candidate => string.Equals(
                candidate.name,
                planName,
                StringComparison.OrdinalIgnoreCase));
        }

        private static void PersistDataReferences(
            AssemblyConfigurator configurator,
            TextAsset planAsset,
            Texture2D referenceImage)
        {
            var serialized = new SerializedObject(configurator);
            serialized.FindProperty("_planAsset").objectReferenceValue = planAsset;
            serialized.FindProperty("_referenceImage").objectReferenceValue = referenceImage;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            MarkDirty(configurator);
        }

        private static Transform ResolveIndexedParent(
            Transform root,
            IReadOnlyList<DisassemblyStep> steps)
        {
            int maxIndex = steps.Max(step => step.ChildIndex);
            HashSet<int> indices = steps.Select(step => step.ChildIndex).ToHashSet();
            Transform[] candidates = root
                .GetComponentsInChildren<Transform>(includeInactive: true)
                .Where(candidate => !IsDirectionVisualTransform(candidate))
                .Where(candidate => candidate.childCount > maxIndex)
                .OrderByDescending(candidate => CountRendererHits(candidate, indices))
                .ThenBy(candidate => GetDepth(candidate, root))
                .ToArray();

            if (candidates.Length == 0)
            {
                throw new IndexOutOfRangeException(
                    $"{root.name} 的层级中没有包含 child[{maxIndex}] 的直接子物体集合。");
            }

            Transform best = candidates[0];
            int bestHits = CountRendererHits(best, indices);
            if (bestHits != indices.Count)
            {
                throw new InvalidOperationException(
                    $"无法解析全部 1 基零件序号；最佳父物体 {best.name} 仅命中 {bestHits}/{indices.Count} 个 Renderer。");
            }

            return best;
        }

        private static int CountRendererHits(Transform candidate, IEnumerable<int> indices)
        {
            return indices.Count(index => index >= 0
                && index < candidate.childCount
                && candidate.GetChild(index).GetComponentInChildren<Renderer>(includeInactive: true) != null);
        }

        private static int GetDepth(Transform candidate, Transform root)
        {
            int depth = 0;
            while (candidate != null && candidate != root)
            {
                depth++;
                candidate = candidate.parent;
            }

            return depth;
        }

        private static AssemblyPart ConfigurePart(
            Transform partTransform,
            DisassemblyStep step,
            Transform indexedParent,
            Transform directionVisualRoot,
            AssemblyConfigurator configurator)
        {
            GameObject partObject = partTransform.gameObject;
            RemoveComponents<OneGrabTranslateTransformer>(partObject);
            ConfigureInteractionColliders(partTransform, configurator.ColliderMode);

            Rigidbody rigidbody = GetOrAddComponent<Rigidbody>(partObject);
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            rigidbody.detectCollisions = true;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            GrabFreeTransformer transformer = GetOrAddComponent<GrabFreeTransformer>(partObject);
            transformer.InjectOptionalPositionConstraints(CreateUnconstrainedPosition());
            transformer.InjectOptionalRotationConstraints(CreateUnconstrainedRotation());
            transformer.InjectOptionalScaleConstraints(CreateLockedScale());

            Grabbable grabbable = GetOrAddComponent<Grabbable>(partObject);
            grabbable.MaxGrabPoints = 1;
            grabbable.InjectOptionalOneGrabTransformer(transformer);
            grabbable.InjectOptionalTwoGrabTransformer(null);
            grabbable.InjectOptionalTargetTransform(partTransform);
            grabbable.InjectOptionalRigidbody(rigidbody);
            grabbable.InjectOptionalThrowWhenUnselected(false);
            grabbable.InjectOptionalKinematicWhileSelected(true);

            GrabInteractable controllerGrab = GetOrAddComponent<GrabInteractable>(partObject);
            controllerGrab.MaxSelectingInteractors = 1;
            controllerGrab.InjectRigidbody(rigidbody);
            controllerGrab.InjectOptionalPointableElement(grabbable);
            controllerGrab.UseClosestPointAsGrabSource = true;

            HandGrabInteractable handGrab = GetOrAddComponent<HandGrabInteractable>(partObject);
            handGrab.MaxSelectingInteractors = 1;
            handGrab.InjectRigidbody(rigidbody);
            handGrab.InjectOptionalPointableElement(grabbable);

            HighlightEffect highlight = GetOrAddComponent<HighlightEffect>(partObject);
            highlight.effectTarget = partTransform;
            highlight.outline = 1f;
            highlight.outlineColor = configurator.ContactOutlineColor;
            highlight.outlineWidth = configurator.OutlineWidth;
            highlight.glow = 0f;
            highlight.overlay = 0f;
            highlight.SetHighlighted(false);

            if (!TryCalculateWorldBounds(partTransform, out Bounds worldBounds))
            {
                throw new InvalidOperationException(
                    $"{partTransform.name} 没有可用于定位方向箭头的 Renderer。");
            }

            float partWorldSize = Mathf.Max(
                worldBounds.size.x,
                worldBounds.size.y,
                worldBounds.size.z);
            float arrowLength = Mathf.Max(
                configurator.DirectionArrowMinimumLength,
                partWorldSize * configurator.DirectionArrowLengthMultiplier);
            AssemblyDirectionIndicator directionIndicator =
                GetOrAddComponent<AssemblyDirectionIndicator>(partObject);
            (Line shaft, Cone head) = CreateDirectionShapeComponents(
                directionVisualRoot,
                partTransform,
                step.PartNumber);
            directionIndicator.Configure(
                indexedParent,
                step.HintLocalDirection,
                partTransform.InverseTransformPoint(worldBounds.center),
                shaft,
                head,
                configurator.DirectionArrowColor,
                arrowLength,
                partWorldSize * configurator.DirectionArrowOffsetMultiplier,
                arrowLength * configurator.DirectionArrowThicknessRatio,
                arrowLength * configurator.DirectionArrowHeadLengthRatio,
                arrowLength * configurator.DirectionArrowHeadRadiusRatio);

            AssemblyPart part = GetOrAddComponent<AssemblyPart>(partObject);
            part.Configure(
                step.PartNumber,
                step.ChildIndex,
                step.Round,
                step.HintLocalDirection,
                configurator.MinimumOperationDistance,
                configurator.MinimumOperationAngle,
                rigidbody,
                grabbable,
                controllerGrab,
                handGrab,
                highlight,
                directionIndicator,
                configurator.DisablePhysicalCollisions,
                configurator.ContactOutlineColor,
                configurator.GuidanceOutlineColor,
                configurator.CompletedPartOutlineColor,
                configurator.SeeThroughIntensity,
                configurator.SeeThroughTintAlpha,
                configurator.SeeThroughBorder,
                configurator.SeeThroughBorderWidth);

            MarkDirty(partObject.GetComponents<Component>());
            return part;
        }

        private static Transform RecreateDirectionVisualRoot(Transform assemblyRoot)
        {
            Transform existing = assemblyRoot.Find(DirectionVisualRootName);
            if (existing != null)
            {
                DestroyGameObject(existing.gameObject);
            }

            GameObject visualRoot = CreateGameObject(DirectionVisualRootName);
            visualRoot.layer = assemblyRoot.gameObject.layer;
            visualRoot.transform.SetParent(assemblyRoot, worldPositionStays: false);
            return visualRoot.transform;
        }

        private static (Line shaft, Cone head) CreateDirectionShapeComponents(
            Transform visualRoot,
            Transform partTransform,
            int partNumber)
        {
            GameObject shaftObject = CreateGameObject(
                $"part[{partNumber}] {partTransform.name} - Shapes Shaft");
            shaftObject.layer = partTransform.gameObject.layer;
            shaftObject.transform.SetParent(visualRoot, worldPositionStays: false);
            Line shaft = GetOrAddComponent<Line>(shaftObject);

            GameObject headObject = CreateGameObject(
                $"part[{partNumber}] {partTransform.name} - Shapes Head");
            headObject.layer = partTransform.gameObject.layer;
            headObject.transform.SetParent(visualRoot, worldPositionStays: false);
            Cone head = GetOrAddComponent<Cone>(headObject);

            MarkDirty(shaft, head);
            return (shaft, head);
        }

        private static bool IsDirectionVisualTransform(Transform candidate)
        {
            while (candidate != null)
            {
                if (candidate.name == DirectionVisualRootName)
                {
                    return true;
                }

                candidate = candidate.parent;
            }

            return false;
        }

        private static TransformerUtils.PositionConstraints CreateUnconstrainedPosition()
        {
            return new TransformerUtils.PositionConstraints
            {
                ConstraintsAreRelative = false,
                XAxis = TransformerUtils.ConstrainedAxis.Unconstrained,
                YAxis = TransformerUtils.ConstrainedAxis.Unconstrained,
                ZAxis = TransformerUtils.ConstrainedAxis.Unconstrained
            };
        }

        private static TransformerUtils.RotationConstraints CreateUnconstrainedRotation()
        {
            return new TransformerUtils.RotationConstraints
            {
                XAxis = TransformerUtils.ConstrainedAxis.Unconstrained,
                YAxis = TransformerUtils.ConstrainedAxis.Unconstrained,
                ZAxis = TransformerUtils.ConstrainedAxis.Unconstrained
            };
        }

        private static TransformerUtils.ScaleConstraints CreateLockedScale()
        {
            TransformerUtils.ConstrainedAxis locked = new()
            {
                ConstrainAxis = true,
                AxisRange = new TransformerUtils.FloatRange { Min = 1f, Max = 1f }
            };
            return new TransformerUtils.ScaleConstraints
            {
                ConstraintsAreRelative = true,
                XAxis = locked,
                YAxis = locked,
                ZAxis = locked
            };
        }

        private static void ConfigureInteractionColliders(
            Transform part,
            AssemblyColliderMode colliderMode)
        {
            switch (colliderMode)
            {
                case AssemblyColliderMode.ExistingOrBox:
                    if (part.GetComponentInChildren<Collider>(includeInactive: true) == null)
                    {
                        ConfigureBoxCollider(part);
                    }
                    break;
                case AssemblyColliderMode.BoxBounds:
                    RemoveComponentsInChildren<MeshCollider>(part);
                    ConfigureBoxCollider(part);
                    break;
                case AssemblyColliderMode.ConvexMesh:
                    RemoveComponents<BoxCollider>(part.gameObject);
                    if (ConfigureMeshColliders(part) == 0)
                    {
                        Debug.LogWarning(
                            $"[DreamVR] {part.name} 没有可用 Mesh，已回退为 BoxCollider。",
                            part);
                        ConfigureBoxCollider(part);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(colliderMode));
            }

            foreach (Collider collider in part.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                collider.isTrigger = true;
                MarkDirty(collider);
            }
        }

        private static int ConfigureMeshColliders(Transform part)
        {
            var configuredObjects = new HashSet<GameObject>();
            foreach (MeshFilter meshFilter in part.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                if (meshFilter.sharedMesh == null || !configuredObjects.Add(meshFilter.gameObject))
                {
                    continue;
                }

                ConfigureMeshCollider(meshFilter.gameObject, meshFilter.sharedMesh);
            }

            foreach (SkinnedMeshRenderer renderer in
                     part.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
            {
                if (renderer.sharedMesh == null || !configuredObjects.Add(renderer.gameObject))
                {
                    continue;
                }

                ConfigureMeshCollider(renderer.gameObject, renderer.sharedMesh);
            }

            return configuredObjects.Count;
        }

        private static void ConfigureMeshCollider(GameObject target, Mesh mesh)
        {
            MeshCollider collider = GetOrAddComponent<MeshCollider>(target);
            collider.sharedMesh = mesh;
            collider.convex = true;
            collider.isTrigger = true;
            MarkDirty(collider);
        }

        private static void ConfigureBoxCollider(Transform part)
        {
            if (!TryCalculateBounds(part, part, out Bounds localBounds))
            {
                throw new InvalidOperationException($"{part.name} 没有可用于生成 Collider 的 Renderer。");
            }

            BoxCollider collider = GetOrAddComponent<BoxCollider>(part.gameObject);
            collider.center = localBounds.center;
            collider.size = localBounds.size;
            collider.isTrigger = true;
            MarkDirty(collider);
        }

        private static bool TryCalculateBounds(Transform target, Transform space, out Bounds bounds)
        {
            bool initialized = false;
            bounds = default;
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                Bounds worldBounds = renderer.bounds;
                Vector3 min = worldBounds.min;
                Vector3 max = worldBounds.max;
                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 point = space.InverseTransformPoint(new Vector3(
                                x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y,
                                z == 0 ? min.z : max.z));
                            if (!initialized)
                            {
                                bounds = new Bounds(point, Vector3.zero);
                                initialized = true;
                            }
                            else
                            {
                                bounds.Encapsulate(point);
                            }
                        }
                    }
                }
            }

            return initialized;
        }

        private static bool TryCalculateWorldBounds(Transform target, out Bounds bounds)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            bounds = default;
            bool initialized = false;
            foreach (Renderer renderer in renderers)
            {
                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return initialized;
        }

        private static void RemoveStalePartConfigurations(
            Transform indexedParent,
            IReadOnlyList<DisassemblyStep> steps)
        {
            HashSet<int> expected = steps.Select(step => step.ChildIndex).ToHashSet();
            AssemblyPart[] staleParts = indexedParent
                .GetComponentsInChildren<AssemblyPart>(includeInactive: true)
                .Where(part => part.transform.parent != indexedParent
                    || !expected.Contains(part.transform.GetSiblingIndex()))
                .ToArray();
            foreach (AssemblyPart stalePart in staleParts)
            {
                RemoveManagedPartConfiguration(stalePart.gameObject);
            }
        }

        private static void RemoveManagedPartConfiguration(GameObject partObject)
        {
            HighlightEffect highlight = partObject.GetComponent<HighlightEffect>();
            if (highlight != null)
            {
                highlight.SetHighlighted(false);
            }

            RemoveComponents<GrabInteractable>(partObject);
            RemoveComponents<HandGrabInteractable>(partObject);
            RemoveComponents<Grabbable>(partObject);
            RemoveComponents<GrabFreeTransformer>(partObject);
            RemoveComponents<OneGrabTranslateTransformer>(partObject);
            RemoveComponents<AssemblyDirectionIndicator>(partObject);
            RemoveComponents<AssemblyPart>(partObject);
            RemoveComponents<Rigidbody>(partObject);
            RemoveComponents<HighlightEffect>(partObject);
            RemoveComponentsInChildren<MeshCollider>(partObject.transform);
            RemoveComponentsInChildren<BoxCollider>(partObject.transform);
        }

        private static bool HasManagedGrabComponents(GameObject gameObject)
        {
            return gameObject.GetComponent<AssemblyPart>() != null
                || gameObject.GetComponent<Grabbable>() != null
                || gameObject.GetComponent<GrabInteractable>() != null
                || gameObject.GetComponent<HandGrabInteractable>() != null
                || gameObject.GetComponent<GrabFreeTransformer>() != null
                || gameObject.GetComponent<OneGrabTranslateTransformer>() != null
                || gameObject.GetComponent<AssemblyDirectionIndicator>() != null;
        }

        private static void DisableRootWideHighlight(
            GameObject root,
            IReadOnlyCollection<AssemblyPart> parts)
        {
            HashSet<HighlightEffect> partEffects = parts
                .Select(part => part.GetComponent<HighlightEffect>())
                .Where(effect => effect != null)
                .ToHashSet();
            foreach (HighlightEffect effect in root.GetComponents<HighlightEffect>())
            {
                if (partEffects.Contains(effect))
                {
                    continue;
                }

                effect.SetHighlighted(false);
                effect.enabled = false;
                MarkDirty(effect);
            }
        }

        private static void BindUndoButton(
            AssemblyConfigurator configurator,
            AssemblyController controller)
        {
            InteractableUnityEventWrapper wrapper = ResolveUndoButton(configurator);
            if (wrapper == null)
            {
                throw new InvalidOperationException("场景中找不到 BigRedButton 的 InteractableUnityEventWrapper。");
            }

            bool hasUndo = false;
            for (int index = wrapper.WhenSelect.GetPersistentEventCount() - 1; index >= 0; index--)
            {
                UnityEngine.Object target = wrapper.WhenSelect.GetPersistentTarget(index);
                string method = wrapper.WhenSelect.GetPersistentMethodName(index);
                bool isAssemblyHistoryMethod = method == nameof(AssemblyController.ResetAll)
                    || method == nameof(AssemblyController.UndoLastOperation);
                if (!isAssemblyHistoryMethod
                    || (target != null && target is not AssemblyController))
                {
                    continue;
                }

                bool isCurrentUndo = target is AssemblyController targetController
                    && targetController == controller
                    && method == nameof(AssemblyController.UndoLastOperation);
                if (isCurrentUndo && !hasUndo)
                {
                    hasUndo = true;
                }
                else
                {
                    UnityEventTools.RemovePersistentListener(wrapper.WhenSelect, index);
                }
            }

            if (!hasUndo)
            {
                UnityEventTools.AddPersistentListener(
                    wrapper.WhenSelect,
                    controller.UndoLastOperation);
            }

            SetButtonLabel(wrapper.transform, "Undo");
            var serializedConfigurator = new SerializedObject(configurator);
            serializedConfigurator.FindProperty("_undoButton").objectReferenceValue = wrapper;
            serializedConfigurator.ApplyModifiedPropertiesWithoutUndo();
            MarkDirty(wrapper);
            MarkDirty(configurator);
        }

        private static void SetButtonLabel(Transform wrapperTransform, string label)
        {
            Transform buttonRoot = wrapperTransform;
            while (buttonRoot.parent != null && buttonRoot.name != "BigRedButton")
            {
                buttonRoot = buttonRoot.parent;
            }

            foreach (Component component in buttonRoot.GetComponentsInChildren<Component>(includeInactive: true))
            {
                if (component == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(component);
                SerializedProperty text = serialized.FindProperty("m_text");
                if (text == null || text.propertyType != SerializedPropertyType.String)
                {
                    continue;
                }

                if (component.gameObject.name == "Reset"
                    || text.stringValue == "Reset"
                    || text.stringValue == "Undo")
                {
                    text.stringValue = label;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    component.gameObject.name = label;
                    MarkDirty(component);
                }
            }
        }

        private static InteractableUnityEventWrapper FindBigRedButtonWrapper()
        {
            return UnityEngine.Object
                .FindObjectsByType<InteractableUnityEventWrapper>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault(candidate => HasAncestorNamed(candidate.transform, "BigRedButton"));
        }

        private static InteractableUnityEventWrapper ResolveUndoButton(
            AssemblyConfigurator configurator)
        {
            return configurator.UndoButton != null
                ? configurator.UndoButton
                : FindBigRedButtonWrapper();
        }

        private static bool HasUndoListener(
            InteractableUnityEventWrapper wrapper,
            AssemblyController controller)
        {
            return HasListener(wrapper, controller, nameof(AssemblyController.UndoLastOperation));
        }

        private static bool HasResetListener(
            InteractableUnityEventWrapper wrapper,
            AssemblyController controller)
        {
            return HasListener(wrapper, controller, nameof(AssemblyController.ResetAll));
        }

        private static bool HasListener(
            InteractableUnityEventWrapper wrapper,
            AssemblyController controller,
            string methodName)
        {
            for (int index = 0; index < wrapper.WhenSelect.GetPersistentEventCount(); index++)
            {
                if (wrapper.WhenSelect.GetPersistentTarget(index) == controller
                    && wrapper.WhenSelect.GetPersistentMethodName(index) == methodName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAncestorNamed(Transform transform, string name)
        {
            while (transform != null)
            {
                if (transform.name == name)
                {
                    return true;
                }

                transform = transform.parent;
            }

            return false;
        }

        private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            if (component != null)
            {
                return component;
            }

            return Application.isBatchMode
                ? gameObject.AddComponent<T>()
                : Undo.AddComponent<T>(gameObject);
        }

        private static GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            if (!Application.isBatchMode)
            {
                Undo.RegisterCreatedObjectUndo(gameObject, $"Create {name}");
            }

            return gameObject;
        }

        private static void RemoveComponents<T>(GameObject gameObject) where T : Component
        {
            foreach (T component in gameObject.GetComponents<T>())
            {
                DestroyComponent(component);
            }
        }

        private static void RemoveComponentsInChildren<T>(Transform root) where T : Component
        {
            foreach (T component in root.GetComponentsInChildren<T>(includeInactive: true))
            {
                DestroyComponent(component);
            }
        }

        private static void DestroyComponent(Component component)
        {
            if (component == null)
            {
                return;
            }

            if (Application.isBatchMode)
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
            else
            {
                Undo.DestroyObjectImmediate(component);
            }
        }

        private static void DestroyGameObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (Application.isBatchMode)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
            else
            {
                Undo.DestroyObjectImmediate(gameObject);
            }
        }

        private static void MarkDirty(params Component[] components)
        {
            foreach (Component component in components)
            {
                if (component == null)
                {
                    continue;
                }

                EditorUtility.SetDirty(component);
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }
    }
}
