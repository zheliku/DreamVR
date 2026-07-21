using System;
using System.Collections;
using System.Linq;
using HighlightPlus;
using NUnit.Framework;
using Oculus.Interaction;
using Shapes;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DreamVR.Assembly.Tests
{
    public sealed class AssemblyRuntimeTests
    {
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator MainScene_UsesOneBasedFreeGrabConfigurationAndUndoBinding()
        {
            AsyncOperation loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/Main.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            while (!loadOperation.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return null;

            Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/Main.unity");
            GameObject root = scene
                .GetRootGameObjects()
                .SelectMany(candidate => candidate.GetComponentsInChildren<Transform>(true))
                .Select(candidate => candidate.gameObject)
                .FirstOrDefault(candidate => candidate.name == "71");
            Assert.That(root, Is.Not.Null);

            AssemblyController controller = root.GetComponent<AssemblyController>();
            AssemblyConfigurator configurator = root.GetComponent<AssemblyConfigurator>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(configurator, Is.Not.Null);
            Assert.That(controller.Condition, Is.EqualTo(InteractionExperimentCondition.NoGuidance));
            Assert.That(controller.CurrentRound, Is.EqualTo(1));
            Assert.That(controller.Parts.Count, Is.EqualTo(6));
            Assert.That(controller.Parts.Select(part => part.PartNumber), Is.EquivalentTo(new[] { 1, 2, 3, 4, 6, 7 }));
            Assert.That(controller.Parts.Select(part => part.ChildIndex), Is.EquivalentTo(new[] { 0, 1, 2, 3, 5, 6 }));
            Assert.That(root.transform.GetChild(4).GetComponent<AssemblyPart>(), Is.Null);
            Assert.That(root.transform.GetChild(7).GetComponent<AssemblyPart>(), Is.Null);
            Assert.That(HasUndoButtonBinding(controller), Is.True);
            Assert.That(HasResetButtonBinding(controller), Is.False);

            foreach (AssemblyPart part in controller.Parts)
            {
                Assert.That(part.PartNumber, Is.EqualTo(part.ChildIndex + 1));
                Assert.That(part.GetComponent<GrabFreeTransformer>(), Is.Not.Null);
                Assert.That(part.GetComponent<OneGrabTranslateTransformer>(), Is.Null);
                Assert.That(part.GetComponent<Rigidbody>().isKinematic, Is.True);
                Assert.That(part.GetComponent<Rigidbody>().useGravity, Is.False);
                Assert.That(
                    part.GetComponentsInChildren<Collider>(true).All(collider => collider.isTrigger),
                    Is.True);
                Assert.That(part.InteractionEnabled, Is.True);
                Assert.That(part.GuidanceHighlighted, Is.False);
                Assert.That(part.DirectionGuidanceVisible, Is.False);
                Assert.That(part.GetComponent<HighlightEffect>().highlighted, Is.False);
            }

            AssemblyPart hoverPart = controller.Parts.First(part => part.Round == 1);
            HighlightEffect hoverHighlight = hoverPart.GetComponent<HighlightEffect>();
            GrabInteractor testInteractor = CreateGrabInteractor(out GameObject interactorObject);
            yield return null;

            testInteractor.ForceSelect(hoverPart.GetComponent<GrabInteractable>());
            testInteractor.ProcessCandidate();
            testInteractor.Hover();
            Assert.That(hoverHighlight.highlighted, Is.True);
            Assert.That(
                ColorDistance(hoverHighlight.outlineColor, configurator.ContactOutlineColor),
                Is.LessThan(0.0001f));
            testInteractor.Select();
            Assert.That(hoverHighlight.highlighted, Is.True);
            testInteractor.Unselect();
            testInteractor.Unhover();
            Assert.That(hoverHighlight.highlighted, Is.False);
            Object.Destroy(interactorObject);
            yield return null;

            controller.ResetAllImmediate();
            AssemblyPart[] firstRound = controller.Parts.Where(part => part.Round == 1).ToArray();
            Vector3 secondPartInitialPosition = firstRound[1].transform.localPosition;
            CommitPose(firstRound[0], new Vector3(0.3f, -0.2f, 0.5f), Quaternion.Euler(17f, 29f, 41f));
            CommitPose(firstRound[1], new Vector3(-0.4f, 0.6f, -0.1f), Quaternion.Euler(35f, 7f, 82f));
            Assert.That(controller.CurrentRound, Is.EqualTo(2));
            Assert.That(controller.OperationCount, Is.EqualTo(2));

            controller.UndoLastOperation();
            yield return null;
            yield return null;
            yield return null;

            Assert.That(controller.CurrentRound, Is.EqualTo(1));
            Assert.That(firstRound[0].IsCompleted, Is.True);
            Assert.That(firstRound[1].IsCompleted, Is.False);
            Assert.That(firstRound[1].transform.localPosition, Is.EqualTo(secondPartInitialPosition));
            Assert.That(controller.OperationCount, Is.EqualTo(1));

            GrabInteractor completedInteractor = CreateGrabInteractor(
                out GameObject completedInteractorObject);
            yield return null;
            HighlightEffect completedHighlight = firstRound[0].GetComponent<HighlightEffect>();
            completedInteractor.ForceSelect(firstRound[0].GetComponent<GrabInteractable>());
            completedInteractor.ProcessCandidate();
            completedInteractor.Hover();
            Assert.That(completedHighlight.highlighted, Is.True);
            Assert.That(
                ColorDistance(
                    completedHighlight.outlineColor,
                    configurator.CompletedPartOutlineColor),
                Is.LessThan(0.0001f));
            completedInteractor.Unhover();
            Object.Destroy(completedInteractorObject);
            yield return null;
        }
#endif

        [UnityTest]
        public IEnumerator Part_AllowsArbitraryTranslationAndRotationAcrossFrames()
        {
            var root = new GameObject("Assembly");
            var partObject = new GameObject("Part");
            partObject.transform.SetParent(root.transform, false);
            AssemblyPart part = CreatePart(partObject, 1, 0, 1);
            AssemblyController controller = root.AddComponent<AssemblyController>();
            controller.Configure(new[] { part });
            yield return null;
            controller.ResetAllImmediate();

            Assert.That(part.BeginOperationRecording(), Is.True);
            Vector3 expectedPosition = new(2.3f, -4.1f, 7.7f);
            Quaternion expectedRotation = Quaternion.Euler(31f, 73f, 19f);
            partObject.transform.localPosition = expectedPosition;
            partObject.transform.localRotation = expectedRotation;

            yield return null;
            yield return null;

            Assert.That(Vector3.Distance(partObject.transform.localPosition, expectedPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(partObject.transform.localRotation, expectedRotation), Is.LessThan(0.001f));
            Assert.That(part.CompleteOperationRecording(), Is.True);
            Assert.That(controller.OperationCount, Is.EqualTo(1));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UndoWhileGrabIsPending_CancelsAndRestoresPendingStartPose()
        {
            var root = new GameObject("Assembly");
            var partObject = new GameObject("Part");
            partObject.transform.SetParent(root.transform, false);
            AssemblyPart part = CreatePart(partObject, 1, 0, 1);
            AssemblyController controller = root.AddComponent<AssemblyController>();
            controller.Configure(new[] { part });
            yield return null;
            controller.ResetAllImmediate();

            Assert.That(part.BeginOperationRecording(), Is.True);
            partObject.transform.localPosition = new Vector3(9f, 8f, 7f);
            partObject.transform.localRotation = Quaternion.Euler(80f, 70f, 60f);
            controller.UndoLastOperation();

            yield return null;
            yield return null;
            yield return null;

            Assert.That(partObject.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(Quaternion.Angle(partObject.transform.localRotation, Quaternion.identity), Is.LessThan(0.001f));
            Assert.That(part.HasPendingOperation, Is.False);
            Assert.That(controller.OperationCount, Is.Zero);
            Assert.That(part.InteractionEnabled, Is.True);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisablingControllerDuringUndo_RecoversSuspendedParts()
        {
            var root = new GameObject("Assembly");
            var partObject = new GameObject("Part");
            partObject.transform.SetParent(root.transform, false);
            AssemblyPart part = CreatePart(partObject, 1, 0, 1);
            AssemblyController controller = root.AddComponent<AssemblyController>();
            controller.Configure(new[] { part });
            yield return null;
            controller.ResetAllImmediate();

            Assert.That(part.BeginOperationRecording(), Is.True);
            partObject.transform.localPosition = new Vector3(4f, 5f, 6f);
            controller.UndoLastOperation();
            Assert.That(part.IsInteractionSuspended, Is.True);

            controller.enabled = false;
            Assert.That(part.IsInteractionSuspended, Is.False);
            Assert.That(part.HasPendingOperation, Is.False);
            Assert.That(partObject.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(part.InteractionEnabled, Is.True);

            yield return null;
            Assert.That(part.IsInteractionSuspended, Is.False);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DirectionIndicator_PreservesParentLocalDirectionAndVisibility()
        {
            var root = new GameObject("Assembly");
            var partObject = new GameObject("Part");
            partObject.transform.SetParent(root.transform, false);
            AssemblyDirectionIndicator indicator =
                partObject.AddComponent<AssemblyDirectionIndicator>();
            var shaftObject = new GameObject("Shapes Shaft");
            shaftObject.transform.SetParent(root.transform, false);
            Line shaft = shaftObject.AddComponent<Line>();
            var headObject = new GameObject("Shapes Head");
            headObject.transform.SetParent(root.transform, false);
            Cone head = headObject.AddComponent<Cone>();
            Vector3 direction = new Vector3(1f, 1f, -1f).normalized;
            indicator.Configure(
                root.transform,
                direction,
                Vector3.zero,
                shaft,
                head,
                Color.yellow,
                0.2f,
                0.02f,
                0.006f,
                0.05f,
                0.02f);

            Assert.That(indicator.DirectionSpace, Is.SameAs(root.transform));
            Assert.That(Vector3.Distance(indicator.LocalDirection, direction), Is.LessThan(0.0001f));
            Assert.That(indicator.Shaft, Is.SameAs(shaft));
            Assert.That(indicator.Head, Is.SameAs(head));
            Assert.That(indicator.GuidanceVisible, Is.False);
            Assert.That(indicator.enabled, Is.False);
            Assert.That(shaft.enabled, Is.False);
            Assert.That(head.enabled, Is.False);

            AssemblyPart part = partObject.AddComponent<AssemblyPart>();
            part.Configure(
                1,
                0,
                1,
                direction,
                0.005f,
                2f,
                null,
                null,
                null,
                null,
                null,
                indicator,
                true,
                Color.cyan,
                Color.green,
                Color.yellow,
                0.8f,
                0.35f,
                0.8f,
                0.45f);
            AssemblyController controller = root.AddComponent<AssemblyController>();
            controller.Configure(
                new[] { part },
                InteractionExperimentCondition.CurrentPartHighlightAndDirection);
            yield return null;
            controller.ResetAllImmediate();

            Assert.That(indicator.GuidanceVisible, Is.True);
            Assert.That(indicator.enabled, Is.True);
            Assert.That(shaft.enabled, Is.True);
            Assert.That(head.enabled, Is.True);

            CommitPose(part, Vector3.right, Quaternion.identity);
            Assert.That(indicator.GuidanceVisible, Is.False);
            Assert.That(indicator.enabled, Is.False);
            Assert.That(shaft.enabled, Is.False);
            Assert.That(head.enabled, Is.False);

            Object.Destroy(root);
            yield return null;
        }

        private static AssemblyPart CreatePart(
            GameObject gameObject,
            int partNumber,
            int childIndex,
            int round)
        {
            AssemblyPart part = gameObject.AddComponent<AssemblyPart>();
            part.Configure(
                partNumber,
                childIndex,
                round,
                Vector3.forward,
                0.005f,
                2f,
                null,
                null,
                null,
                null,
                null,
                null,
                true,
                Color.cyan,
                Color.green,
                Color.yellow,
                0.8f,
                0.35f,
                0.8f,
                0.45f);
            return part;
        }

        private static void CommitPose(
            AssemblyPart part,
            Vector3 localPosition,
            Quaternion localRotation)
        {
            Assert.That(part.BeginOperationRecording(), Is.True);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            Assert.That(part.CompleteOperationRecording(), Is.True);
        }

        private static GrabInteractor CreateGrabInteractor(out GameObject interactorObject)
        {
            interactorObject = new GameObject("TestGrabInteractor");
            interactorObject.SetActive(false);
            Rigidbody rigidbody = interactorObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            SphereCollider collider = interactorObject.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            var selector = interactorObject.AddComponent<TestSelector>();
            GrabInteractor interactor = interactorObject.AddComponent<GrabInteractor>();
            interactor.InjectAllGrabInteractor(selector, rigidbody);
            interactorObject.SetActive(true);
            return interactor;
        }

        private static bool HasUndoButtonBinding(AssemblyController controller)
        {
            return HasButtonBinding(controller, nameof(AssemblyController.UndoLastOperation));
        }

        private static bool HasResetButtonBinding(AssemblyController controller)
        {
            return HasButtonBinding(controller, nameof(AssemblyController.ResetAll));
        }

        private static bool HasButtonBinding(AssemblyController controller, string methodName)
        {
            foreach (InteractableUnityEventWrapper wrapper in
                     Object.FindObjectsByType<InteractableUnityEventWrapper>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (!HasAncestorNamed(wrapper.transform, "BigRedButton"))
                {
                    continue;
                }

                for (int index = 0; index < wrapper.WhenSelect.GetPersistentEventCount(); index++)
                {
                    if (wrapper.WhenSelect.GetPersistentTarget(index) == controller
                        && wrapper.WhenSelect.GetPersistentMethodName(index) == methodName)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasAncestorNamed(Transform transform, string expectedName)
        {
            while (transform != null)
            {
                if (transform.name == expectedName)
                {
                    return true;
                }

                transform = transform.parent;
            }

            return false;
        }

        private static float ColorDistance(Color first, Color second)
        {
            return new Vector4(
                first.r - second.r,
                first.g - second.g,
                first.b - second.b,
                first.a - second.a).magnitude;
        }
    }

    internal sealed class TestSelector : MonoBehaviour, ISelector
    {
        public event Action WhenSelected = delegate { };

        public event Action WhenUnselected = delegate { };
    }
}
