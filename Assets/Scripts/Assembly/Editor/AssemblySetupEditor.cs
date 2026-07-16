using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HighlightPlus;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DreamVR.Assembly.Editor
{
    public static class AssemblySetupEditor
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string DefaultAssemblyName = "71";

        [MenuItem("Tools/DreamVR/装配/配置选中模型")]
        public static void ConfigureSelectedAssembly()
        {
            GameObject root = Selection.activeGameObject;
            if (root == null)
            {
                throw new InvalidOperationException("请先选择装配模型根物体。");
            }

            ConfigureAssembly(root);
            SaveContainingScene(root);
        }

        [MenuItem("Tools/DreamVR/装配/配置 Main 场景中的 71")]
        public static void ConfigureMainSceneAssembly()
        {
            GameObject root = FindGameObjectInScene(SceneManager.GetActiveScene(), DefaultAssemblyName);
            if (root == null)
            {
                throw new InvalidOperationException(
                    $"当前场景中找不到名为 {DefaultAssemblyName} 的物体。请先打开 {MainScenePath}。");
            }

            ConfigureAssembly(root);
            SaveContainingScene(root);
        }

        public static void ConfigureMainSceneFromCommandLine()
        {
            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            GameObject root = FindGameObjectInScene(scene, DefaultAssemblyName);
            if (root == null)
            {
                throw new InvalidOperationException($"{MainScenePath} 中找不到 {DefaultAssemblyName}。");
            }

            ConfigureAssembly(root);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[DreamVR] Main 场景装配配置完成。");
        }

        [MenuItem("Tools/DreamVR/装配/验证 Main 场景中的 71")]
        public static void ValidateMainSceneAssembly()
        {
            GameObject root = FindGameObjectInScene(SceneManager.GetActiveScene(), DefaultAssemblyName);
            if (root == null)
            {
                throw new InvalidOperationException(
                    $"当前场景中找不到名为 {DefaultAssemblyName} 的物体。请先打开 {MainScenePath}。");
            }

            ValidateAssembly(root);
        }

        public static void ValidateMainSceneFromCommandLine()
        {
            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            GameObject root = FindGameObjectInScene(scene, DefaultAssemblyName);
            if (root == null)
            {
                throw new InvalidOperationException($"{MainScenePath} 中找不到 {DefaultAssemblyName}。");
            }

            ValidateAssembly(root);
        }

        public static void ConfigureAssembly(GameObject root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

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

            TextAsset planAsset = FindSinglePlanAsset(directory);
            IReadOnlyList<DisassemblyStep> steps = DisassemblyPlanParser.Parse(planAsset.text);
            Transform indexedParent = ResolveIndexedParent(root.transform, steps);

            if (!TryCalculateBounds(indexedParent, indexedParent, out Bounds assemblyBounds))
            {
                throw new InvalidOperationException($"{indexedParent.name} 及其子物体中没有 Renderer。");
            }

            IReadOnlyDictionary<int, float> distances = CalculateTravelDistances(
                indexedParent,
                assemblyBounds,
                steps);

            var parts = new List<AssemblyPart>(steps.Count);
            foreach (DisassemblyStep step in steps)
            {
                Transform partTransform = indexedParent.GetChild(step.ChildIndex);
                AssemblyPart part = ConfigurePart(partTransform, step, distances[step.ChildIndex]);
                parts.Add(part);
                Debug.Log(
                    $"[DreamVR] round{step.Round} child[{step.ChildIndex}] "
                    + $"{partTransform.name}: {step.LocalDirection}, max={distances[step.ChildIndex]:0.####}",
                    partTransform);
            }

            AssemblyController controller = GetOrAddComponent<AssemblyController>(root);
            controller.Configure(parts, InteractionExperimentCondition.NoGuidance);
            controller.ResetAllImmediate();
            EditorUtility.SetDirty(controller);
            PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            MarkDirty(parts.SelectMany(part => part.GetComponents<Component>()));

            DisableRootWideHighlight(root, parts);
            BindResetButton(controller);
            ValidateAssembly(root);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log(
                $"[DreamVR] 已配置 {parts.Count} 个可动零件；索引父物体={indexedParent.name}；"
                + "实验条件=NoGuidance。未列出的子物体（包括下标 5）保持固定。",
                root);
        }

        public static void ValidateAssembly(GameObject root)
        {
            var errors = new List<string>();
            AssemblyController controller = root.GetComponent<AssemblyController>();
            if (controller == null)
            {
                throw new InvalidOperationException($"{root.name} 缺少 AssemblyController。");
            }

            string modelPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            string directory = Path.GetDirectoryName(modelPath)?.Replace('\\', '/');
            TextAsset planAsset = FindSinglePlanAsset(directory);
            IReadOnlyList<DisassemblyStep> steps = DisassemblyPlanParser.Parse(planAsset.text);
            Transform indexedParent = ResolveIndexedParent(root.transform, steps);
            Dictionary<int, DisassemblyStep> expected = steps.ToDictionary(step => step.ChildIndex);
            Dictionary<int, AssemblyPart> actualByIndex = controller.Parts
                .Where(part => part != null)
                .GroupBy(part => part.ChildIndex)
                .ToDictionary(group => group.Key, group => group.First());

            if (controller.Condition != InteractionExperimentCondition.NoGuidance)
            {
                errors.Add($"实验条件应为 NoGuidance，实际为 {controller.Condition}。");
            }

            if (controller.CurrentRound != steps.Min(step => step.Round))
            {
                errors.Add($"初始轮次应为 1，实际为 {controller.CurrentRound}。");
            }

            if (controller.Parts.Count != steps.Count)
            {
                errors.Add($"应配置 {steps.Count} 个零件，实际为 {controller.Parts.Count} 个。");
            }

            if (!controller.Parts.Select(part => part.ChildIndex).ToHashSet().SetEquals(expected.Keys))
            {
                errors.Add("AssemblyController 中的子物体下标与 txt 不一致。");
            }

            foreach (AssemblyPart part in controller.Parts)
            {
                if (part == null || !expected.TryGetValue(part.ChildIndex, out DisassemblyStep step))
                {
                    errors.Add("AssemblyController 包含空引用或未知零件。");
                    continue;
                }

                bool shouldBeEnabled = part.Round == controller.CurrentRound;
                Rigidbody rigidbody = part.GetComponent<Rigidbody>();
                OneGrabTranslateTransformer transformer =
                    part.GetComponent<OneGrabTranslateTransformer>();
                GrabInteractable controllerGrab = part.GetComponent<GrabInteractable>();
                HandGrabInteractable handGrab = part.GetComponent<HandGrabInteractable>();
                HighlightEffect highlight = part.GetComponent<HighlightEffect>();

                if (part.transform.parent != indexedParent
                    || part.transform.GetSiblingIndex() != part.ChildIndex)
                {
                    errors.Add($"child[{part.ChildIndex}] 没有挂载到 txt 指定的直接子物体上。");
                }

                if (part.Round != step.Round || Vector3.Dot(part.LocalDirection, step.LocalDirection) < 0.999f)
                {
                    errors.Add($"child[{part.ChildIndex}] 的轮次或方向不匹配 txt。");
                }

                if (part.MaxDistance <= 0f)
                {
                    errors.Add($"child[{part.ChildIndex}] 的最大移动距离无效。");
                }

                if (rigidbody == null
                    || part.GetComponentInChildren<Collider>(includeInactive: true) == null
                    || transformer == null
                    || part.GetComponent<Grabbable>() == null
                    || controllerGrab == null
                    || handGrab == null
                    || highlight == null)
                {
                    errors.Add($"child[{part.ChildIndex}] 缺少必需的物理、Meta 或高亮组件。");
                    continue;
                }

                if (!rigidbody.isKinematic || rigidbody.useGravity)
                {
                    errors.Add($"child[{part.ChildIndex}] 的 Rigidbody 应为 Kinematic 且关闭重力。");
                }

                if (!ConstraintsMatch(transformer.Constraints, step, part.MaxDistance))
                {
                    errors.Add($"child[{part.ChildIndex}] 的 Meta 单轴移动约束与 txt 或最大距离不一致。");
                }

                if (controllerGrab.enabled != shouldBeEnabled || handGrab.enabled != shouldBeEnabled)
                {
                    errors.Add($"child[{part.ChildIndex}] 的初始交互启用状态不正确。");
                }

                if (highlight.highlighted)
                {
                    errors.Add($"child[{part.ChildIndex}] 在 NoGuidance 初始状态下不应高亮。");
                }
            }

            foreach (AssemblyPart unexpectedPart in indexedParent
                         .GetComponentsInChildren<AssemblyPart>(includeInactive: true)
                         .Where(part => part.transform.parent != indexedParent
                             || !expected.ContainsKey(part.transform.GetSiblingIndex())))
            {
                errors.Add($"发现未列入 txt 或不在直接子级上的 AssemblyPart：{unexpectedPart.name}。");
            }

            foreach (IGrouping<(DisassemblyAxis Axis, int Sign), DisassemblyStep> directionGroup in
                     steps.GroupBy(step => (step.Axis, step.Sign)))
            {
                DisassemblyStep[] ordered = directionGroup.OrderBy(step => step.Round).ToArray();
                for (int outerIndex = 0; outerIndex < ordered.Length; outerIndex++)
                {
                    for (int innerIndex = outerIndex + 1; innerIndex < ordered.Length; innerIndex++)
                    {
                        DisassemblyStep outer = ordered[outerIndex];
                        DisassemblyStep inner = ordered[innerIndex];
                        if (outer.Round >= inner.Round
                            || !actualByIndex.TryGetValue(outer.ChildIndex, out AssemblyPart outerPart)
                            || !actualByIndex.TryGetValue(inner.ChildIndex, out AssemblyPart innerPart))
                        {
                            continue;
                        }

                        if (outerPart.MaxDistance + 0.0001f < innerPart.MaxDistance)
                        {
                            errors.Add(
                                $"同方向外层 child[{outer.ChildIndex}] 的距离 {outerPart.MaxDistance:0.####} "
                                + $"小于内层 child[{inner.ChildIndex}] 的距离 {innerPart.MaxDistance:0.####}。");
                        }
                    }
                }
            }

            if (indexedParent.childCount > 5)
            {
                GameObject fixedPart = indexedParent.GetChild(5).gameObject;
                if (fixedPart.GetComponent<AssemblyPart>() != null
                    || fixedPart.GetComponent<Grabbable>() != null
                    || fixedPart.GetComponent<GrabInteractable>() != null
                    || fixedPart.GetComponent<HandGrabInteractable>() != null)
                {
                    errors.Add("固定件 child[5] 不应挂载装配抓取组件。");
                }
            }

            if (root.GetComponents<HighlightEffect>().Any(effect => effect.enabled || effect.highlighted))
            {
                errors.Add("装配根物体的整体 HighlightEffect 未完全关闭。");
            }

            InteractableUnityEventWrapper resetWrapper = UnityEngine.Object
                .FindObjectsByType<InteractableUnityEventWrapper>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault(candidate => HasAncestorNamed(candidate.transform, "BigRedButton"));
            if (resetWrapper == null || !HasResetListener(resetWrapper, controller))
            {
                errors.Add("BigRedButton 没有持久化绑定 AssemblyController.ResetAll。");
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException("DreamVR 装配场景验证失败：\n- " + string.Join("\n- ", errors));
            }

            Debug.Log(
                $"[DreamVR] 场景验证通过：{controller.Parts.Count} 个可动零件，"
                + $"当前 round={controller.CurrentRound}，child[5] 固定，重置按钮已绑定。",
                root);
        }

        private static TextAsset FindSinglePlanAsset(string directory)
        {
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { directory });
            TextAsset[] candidates = guids
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Where(path => string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), directory,
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

        private static Transform ResolveIndexedParent(
            Transform root,
            IReadOnlyList<DisassemblyStep> steps)
        {
            int maxIndex = steps.Max(step => step.ChildIndex);
            HashSet<int> indices = steps.Select(step => step.ChildIndex).ToHashSet();
            Transform[] candidates = root
                .GetComponentsInChildren<Transform>(includeInactive: true)
                .Where(candidate => candidate.childCount > maxIndex)
                .OrderByDescending(candidate => CountRendererHits(candidate, indices))
                .ThenBy(candidate => GetDepth(candidate, root))
                .ToArray();

            if (candidates.Length == 0)
            {
                throw new IndexOutOfRangeException(
                    $"{root.name} 的层级中没有包含下标 {maxIndex} 的直接子物体集合。");
            }

            Transform best = candidates[0];
            int bestHits = CountRendererHits(best, indices);
            if (bestHits != indices.Count)
            {
                throw new InvalidOperationException(
                    $"无法可靠解析全部零件下标：最佳父物体 {best.name} 只命中 {bestHits}/{indices.Count} 个 Renderer。");
            }

            return best;
        }

        private static int CountRendererHits(Transform candidate, IEnumerable<int> indices)
        {
            return indices.Count(index =>
                index < candidate.childCount
                && candidate.GetChild(index).GetComponentInChildren<Renderer>(includeInactive: true) != null);
        }

        private static int GetDepth(Transform transform, Transform root)
        {
            int depth = 0;
            while (transform != null && transform != root)
            {
                transform = transform.parent;
                depth++;
            }

            return depth;
        }

        private static IReadOnlyDictionary<int, float> CalculateTravelDistances(
            Transform indexedParent,
            Bounds assemblyBounds,
            IReadOnlyList<DisassemblyStep> steps)
        {
            var candidates = new List<TravelDistanceCandidate>(steps.Count);
            foreach (DisassemblyStep step in steps)
            {
                Transform part = indexedParent.GetChild(step.ChildIndex);
                if (!TryCalculateBounds(part, indexedParent, out Bounds partBounds))
                {
                    throw new InvalidOperationException($"child[{step.ChildIndex}] {part.name} 没有 Renderer。");
                }

                float assemblyMin = GetAxisMin(assemblyBounds, step.Axis);
                float assemblyMax = GetAxisMax(assemblyBounds, step.Axis);
                float partMin = GetAxisMin(partBounds, step.Axis);
                float partMax = GetAxisMax(partBounds, step.Axis);
                float partSize = GetAxisSize(partBounds, step.Axis);
                float assemblySize = GetAxisSize(assemblyBounds, step.Axis);
                float margin = Mathf.Max(partSize * 0.1f, assemblySize * 0.02f);

                float requiredDistance = step.Sign > 0
                    ? assemblyMax - partMin + margin
                    : partMax - assemblyMin + margin;
                candidates.Add(new TravelDistanceCandidate(step, requiredDistance));
            }

            return DisassemblyTravelCalculator.EnforceOuterRoundsNotShorter(candidates);
        }

        private static AssemblyPart ConfigurePart(
            Transform partTransform,
            DisassemblyStep step,
            float maxDistance)
        {
            GameObject partObject = partTransform.gameObject;
            EnsureCollider(partTransform);

            Rigidbody rigidbody = GetOrAddComponent<Rigidbody>(partObject);
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            rigidbody.detectCollisions = true;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            OneGrabTranslateTransformer transformer = GetOrAddComponent<OneGrabTranslateTransformer>(partObject);
            transformer.Constraints = CreateConstraints(step, maxDistance);

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
            highlight.outlineColor = new Color(0.15f, 0.85f, 1f, 1f);
            highlight.outlineWidth = 0.35f;
            highlight.glow = 0f;
            highlight.overlay = 0f;
            highlight.SetHighlighted(false);

            AssemblyPart part = GetOrAddComponent<AssemblyPart>(partObject);
            part.Configure(
                step.ChildIndex,
                step.Round,
                step.LocalDirection,
                maxDistance,
                rigidbody,
                grabbable,
                controllerGrab,
                handGrab,
                highlight);

            MarkDirty(partObject.GetComponents<Component>());
            return part;
        }

        private static OneGrabTranslateTransformer.OneGrabTranslateConstraints CreateConstraints(
            DisassemblyStep step,
            float maxDistance)
        {
            var constraints = new OneGrabTranslateTransformer.OneGrabTranslateConstraints
            {
                ConstraintsAreRelative = true,
                MinX = Constraint(0f),
                MaxX = Constraint(0f),
                MinY = Constraint(0f),
                MaxY = Constraint(0f),
                MinZ = Constraint(0f),
                MaxZ = Constraint(0f)
            };

            float minimum = step.Sign < 0 ? -maxDistance : 0f;
            float maximum = step.Sign > 0 ? maxDistance : 0f;
            switch (step.Axis)
            {
                case DisassemblyAxis.X:
                    constraints.MinX.Value = minimum;
                    constraints.MaxX.Value = maximum;
                    break;
                case DisassemblyAxis.Y:
                    constraints.MinY.Value = minimum;
                    constraints.MaxY.Value = maximum;
                    break;
                case DisassemblyAxis.Z:
                    constraints.MinZ.Value = minimum;
                    constraints.MaxZ.Value = maximum;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return constraints;
        }

        private static FloatConstraint Constraint(float value)
        {
            return new FloatConstraint { Constrain = true, Value = value };
        }

        private static bool ConstraintsMatch(
            OneGrabTranslateTransformer.OneGrabTranslateConstraints constraints,
            DisassemblyStep step,
            float maxDistance)
        {
            if (constraints == null || !constraints.ConstraintsAreRelative)
            {
                return false;
            }

            float minimum = step.Sign < 0 ? -maxDistance : 0f;
            float maximum = step.Sign > 0 ? maxDistance : 0f;
            return ConstraintMatches(constraints.MinX, step.Axis == DisassemblyAxis.X ? minimum : 0f)
                && ConstraintMatches(constraints.MaxX, step.Axis == DisassemblyAxis.X ? maximum : 0f)
                && ConstraintMatches(constraints.MinY, step.Axis == DisassemblyAxis.Y ? minimum : 0f)
                && ConstraintMatches(constraints.MaxY, step.Axis == DisassemblyAxis.Y ? maximum : 0f)
                && ConstraintMatches(constraints.MinZ, step.Axis == DisassemblyAxis.Z ? minimum : 0f)
                && ConstraintMatches(constraints.MaxZ, step.Axis == DisassemblyAxis.Z ? maximum : 0f);
        }

        private static bool ConstraintMatches(FloatConstraint constraint, float expected)
        {
            return constraint != null
                && constraint.Constrain
                && Mathf.Abs(constraint.Value - expected) <= 0.0001f;
        }

        private static void EnsureCollider(Transform part)
        {
            if (part.GetComponentInChildren<Collider>(includeInactive: true) != null)
            {
                return;
            }

            if (!TryCalculateBounds(part, part, out Bounds localBounds))
            {
                throw new InvalidOperationException($"{part.name} 没有可用于生成 Collider 的 Renderer。");
            }

            BoxCollider collider = GetOrAddComponent<BoxCollider>(part.gameObject);
            collider.center = localBounds.center;
            collider.size = localBounds.size;
            EditorUtility.SetDirty(collider);
        }

        private static bool TryCalculateBounds(Transform target, Transform space, out Bounds bounds)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            bool initialized = false;
            bounds = default;

            foreach (Renderer renderer in renderers)
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
                            Vector3 worldPoint = new(
                                x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y,
                                z == 0 ? min.z : max.z);
                            Vector3 localPoint = space.InverseTransformPoint(worldPoint);
                            if (!initialized)
                            {
                                bounds = new Bounds(localPoint, Vector3.zero);
                                initialized = true;
                            }
                            else
                            {
                                bounds.Encapsulate(localPoint);
                            }
                        }
                    }
                }
            }

            return initialized;
        }

        private static void DisableRootWideHighlight(GameObject root, IReadOnlyCollection<AssemblyPart> parts)
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
                EditorUtility.SetDirty(effect);
                PrefabUtility.RecordPrefabInstancePropertyModifications(effect);
            }
        }

        private static void BindResetButton(AssemblyController controller)
        {
            InteractableUnityEventWrapper wrapper = UnityEngine.Object
                .FindObjectsByType<InteractableUnityEventWrapper>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault(candidate => HasAncestorNamed(candidate.transform, "BigRedButton"));

            if (wrapper == null)
            {
                throw new InvalidOperationException("场景中找不到 BigRedButton 的 InteractableUnityEventWrapper。");
            }

            for (int index = 0; index < wrapper.WhenSelect.GetPersistentEventCount(); index++)
            {
                if (wrapper.WhenSelect.GetPersistentTarget(index) == controller
                    && wrapper.WhenSelect.GetPersistentMethodName(index) == nameof(AssemblyController.ResetAll))
                {
                    return;
                }
            }

            UnityEventTools.AddPersistentListener(wrapper.WhenSelect, controller.ResetAll);
            EditorUtility.SetDirty(wrapper);
            PrefabUtility.RecordPrefabInstancePropertyModifications(wrapper);
        }

        private static bool HasResetListener(
            InteractableUnityEventWrapper wrapper,
            AssemblyController controller)
        {
            for (int index = 0; index < wrapper.WhenSelect.GetPersistentEventCount(); index++)
            {
                if (wrapper.WhenSelect.GetPersistentTarget(index) == controller
                    && wrapper.WhenSelect.GetPersistentMethodName(index) == nameof(AssemblyController.ResetAll))
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

        private static GameObject FindGameObjectInScene(Scene scene, string objectName)
        {
            return scene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(includeInactive: true))
                .Select(transform => transform.gameObject)
                .FirstOrDefault(gameObject => gameObject.name == objectName);
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

        private static void MarkDirty(IEnumerable<Component> components)
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

        private static void SaveContainingScene(GameObject root)
        {
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            AssetDatabase.SaveAssets();
        }

        private static float GetAxisMin(Bounds bounds, DisassemblyAxis axis)
        {
            return axis switch
            {
                DisassemblyAxis.X => bounds.min.x,
                DisassemblyAxis.Y => bounds.min.y,
                DisassemblyAxis.Z => bounds.min.z,
                _ => throw new ArgumentOutOfRangeException(nameof(axis))
            };
        }

        private static float GetAxisMax(Bounds bounds, DisassemblyAxis axis)
        {
            return axis switch
            {
                DisassemblyAxis.X => bounds.max.x,
                DisassemblyAxis.Y => bounds.max.y,
                DisassemblyAxis.Z => bounds.max.z,
                _ => throw new ArgumentOutOfRangeException(nameof(axis))
            };
        }

        private static float GetAxisSize(Bounds bounds, DisassemblyAxis axis)
        {
            return axis switch
            {
                DisassemblyAxis.X => bounds.size.x,
                DisassemblyAxis.Y => bounds.size.y,
                DisassemblyAxis.Z => bounds.size.z,
                _ => throw new ArgumentOutOfRangeException(nameof(axis))
            };
        }
    }
}
