using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HighlightPlus;
using NUnit.Framework;
using Oculus.Interaction;
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
        public IEnumerator MainScene_BaselineFlowAndResetUseSerializedConfiguration()
        {
            AsyncOperation loadOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Scenes/Main.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            while (!loadOperation.isDone)
            {
                yield return null;
            }

            yield return null;

            Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/Main.unity");
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
            GameObject root = scene
                .GetRootGameObjects()
                .SelectMany(candidate => candidate.GetComponentsInChildren<Transform>(true))
                .Select(candidate => candidate.gameObject)
                .FirstOrDefault(candidate => candidate.name == "71");
            Assert.That(root, Is.Not.Null);

            AssemblyController controller = root.GetComponent<AssemblyController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.Condition, Is.EqualTo(InteractionExperimentCondition.NoGuidance));
            Assert.That(controller.CurrentRound, Is.EqualTo(1));
            Assert.That(controller.Parts.Count, Is.EqualTo(6));

            var initialPositions = new Dictionary<AssemblyPart, Vector3>();
            foreach (AssemblyPart part in controller.Parts)
            {
                initialPositions.Add(part, part.transform.localPosition);
                Assert.That(part.InteractionEnabled, Is.EqualTo(part.Round == 1));
                Assert.That(part.GetComponent<HighlightEffect>().highlighted, Is.False);
            }

            Assert.That(root.transform.GetChild(5).GetComponent<AssemblyPart>(), Is.Null);
            Assert.That(HasResetButtonBinding(controller), Is.True);

            AssemblyPart hoverPart = controller.Parts.First(part => part.Round == 1);
            HighlightEffect hoverHighlight = hoverPart.GetComponent<HighlightEffect>();
            GrabInteractor testInteractor = CreateGrabInteractor(out GameObject interactorObject);
            yield return null;

            testInteractor.ForceSelect(hoverPart.GetComponent<GrabInteractable>());
            testInteractor.ProcessCandidate();
            testInteractor.Hover();
            Assert.That(testInteractor.Interactable.State, Is.EqualTo(InteractableState.Hover));
            Assert.That(hoverHighlight.highlighted, Is.True);

            testInteractor.Select();
            Assert.That(testInteractor.SelectedInteractable.State, Is.EqualTo(InteractableState.Select));
            Assert.That(hoverHighlight.highlighted, Is.True);

            testInteractor.Unselect();
            testInteractor.Unhover();
            Assert.That(hoverHighlight.highlighted, Is.False);
            Object.Destroy(interactorObject);
            yield return null;

            controller.ResetAll();
            yield return null;
            yield return null;
            Assert.That(controller.CurrentRound, Is.EqualTo(1));

            foreach (AssemblyPart part in controller.Parts.Where(candidate => candidate.Round == 1))
            {
                part.transform.localPosition = initialPositions[part]
                    + part.LocalDirection * part.MaxDistance;
                Assert.That(part.EvaluateCompletionAfterRelease(), Is.True);
            }

            Assert.That(controller.CurrentRound, Is.EqualTo(2));
            controller.ResetAll();
            yield return null;
            yield return null;

            Assert.That(controller.CurrentRound, Is.EqualTo(1));
            foreach (AssemblyPart part in controller.Parts)
            {
                Assert.That(
                    Vector3.Distance(part.transform.localPosition, initialPositions[part]),
                    Is.LessThan(0.0001f));
                Assert.That(part.InteractionEnabled, Is.EqualTo(part.Round == 1));
                Assert.That(part.GetComponent<HighlightEffect>().highlighted, Is.False);
            }
        }
#endif

        [UnityTest]
        public IEnumerator Part_LateUpdateConstrainsParentLocalPositionAndRotation()
        {
            var root = new GameObject("Assembly");
            var partObject = new GameObject("Part");
            partObject.transform.SetParent(root.transform, worldPositionStays: false);
            partObject.transform.localPosition = new Vector3(1f, 2f, 3f);
            partObject.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);

            AssemblyPart part = partObject.AddComponent<AssemblyPart>();
            part.Configure(1, 1, Vector3.back, 0.5f, null, null, null, null, null);

            Quaternion initialRotation = partObject.transform.localRotation;
            partObject.transform.localPosition += new Vector3(8f, -4f, -2f);
            partObject.transform.localRotation = Quaternion.identity;

            yield return null;

            Assert.That(
                Vector3.Distance(partObject.transform.localPosition, new Vector3(1f, 2f, 2.5f)),
                Is.LessThan(0.0001f));
            Assert.That(
                Quaternion.Angle(partObject.transform.localRotation, initialRotation),
                Is.LessThan(0.001f));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Controller_WaitsForWholeRoundAndCoroutineResetRestoresAllParts()
        {
            var root = new GameObject("Assembly");
            root.SetActive(false);
            AssemblyPart first = CreatePart(root.transform, "First", 1, 1, 1f);
            AssemblyPart second = CreatePart(root.transform, "Second", 2, 1, 1f);
            AssemblyPart third = CreatePart(root.transform, "Third", 3, 2, 0.5f);
            AssemblyController controller = root.AddComponent<AssemblyController>();
            controller.Configure(new[] { first, second, third });

            root.SetActive(true);
            yield return null;

            Assert.That(controller.CurrentRound, Is.EqualTo(1));
            Assert.That(first.InteractionEnabled, Is.True);
            Assert.That(second.InteractionEnabled, Is.True);
            Assert.That(third.InteractionEnabled, Is.False);

            first.transform.localPosition = Vector3.forward;
            Assert.That(first.EvaluateCompletionAfterRelease(), Is.True);
            Assert.That(controller.CurrentRound, Is.EqualTo(1));
            Assert.That(first.InteractionEnabled, Is.False);
            Assert.That(second.InteractionEnabled, Is.True);

            second.transform.localPosition = Vector3.forward;
            Assert.That(second.EvaluateCompletionAfterRelease(), Is.True);
            Assert.That(controller.CurrentRound, Is.EqualTo(2));
            Assert.That(third.InteractionEnabled, Is.True);

            third.transform.localPosition = Vector3.forward * 0.25f;
            controller.ResetAll();
            yield return null;
            yield return null;

            Assert.That(controller.CurrentRound, Is.EqualTo(1));
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(third.IsCompleted, Is.False);
            Assert.That(first.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(second.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(third.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(first.InteractionEnabled, Is.True);
            Assert.That(second.InteractionEnabled, Is.True);
            Assert.That(third.InteractionEnabled, Is.False);

            Object.Destroy(root);
            yield return null;
        }

        private static AssemblyPart CreatePart(
            Transform parent,
            string name,
            int childIndex,
            int round,
            float maxDistance)
        {
            var partObject = new GameObject(name);
            partObject.transform.SetParent(parent, worldPositionStays: false);
            AssemblyPart part = partObject.AddComponent<AssemblyPart>();
            part.Configure(
                childIndex,
                round,
                Vector3.forward,
                maxDistance,
                null,
                null,
                null,
                null,
                null);
            return part;
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

        private static bool HasResetButtonBinding(AssemblyController controller)
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
                        && wrapper.WhenSelect.GetPersistentMethodName(index)
                            == nameof(AssemblyController.ResetAll))
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
    }

    internal sealed class TestSelector : MonoBehaviour, ISelector
    {
        public event Action WhenSelected = delegate { };

        public event Action WhenUnselected = delegate { };
    }
}
