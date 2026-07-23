using System.Collections.Generic;
using System.IO;
using System.Linq;
using HighlightPlus;
using NUnit.Framework;
using UnityEngine;

namespace DreamVR.Assembly.Tests
{
    public sealed class DisassemblyPlanTests
    {
        private const string E1Plan =
            "round1: (1, -Z), (7, +Z)\n"
            + "round2: (2, -Z), (6, +Z)\n"
            + "round3: (3, -Z), (4, +Z)";

        [Test]
        public void Configurator_DefaultsUseBlueContactAndSubtleArrow()
        {
            var root = new GameObject("Assembly");

            try
            {
                AssemblyConfigurator configurator = root.AddComponent<AssemblyConfigurator>();
                Color expectedBlue = new(0.05f, 0.5f, 1f, 1f);

                Assert.That(
                    Vector4.Distance(configurator.ContactOutlineColor, expectedBlue),
                    Is.LessThan(0.0001f));
                Assert.That(configurator.DirectionArrowColor.a, Is.EqualTo(0.55f).Within(0.0001f));
                Assert.That(configurator.DirectionArrowLengthMultiplier, Is.EqualTo(0.55f).Within(0.0001f));
                Assert.That(configurator.DirectionArrowMinimumLength, Is.EqualTo(0.05f).Within(0.0001f));
                Assert.That(configurator.DirectionArrowOffsetMultiplier, Is.EqualTo(0.08f).Within(0.0001f));
                Assert.That(configurator.DirectionArrowThicknessRatio, Is.EqualTo(0.0225f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Parse_UsesOneBasedPartNumbersAndZeroBasedUnityChildIndices()
        {
            IReadOnlyList<DisassemblyStep> steps = DisassemblyPlanParser.Parse(E1Plan);

            Assert.That(steps.Count, Is.EqualTo(6));
            Assert.That(steps[0].Round, Is.EqualTo(1));
            Assert.That(steps[0].PartNumber, Is.EqualTo(1));
            Assert.That(steps[0].ChildIndex, Is.EqualTo(0));
            Assert.That(steps[0].HintLocalDirection, Is.EqualTo(Vector3.back));
            Assert.That(steps[1].PartNumber, Is.EqualTo(7));
            Assert.That(steps[1].ChildIndex, Is.EqualTo(6));
            Assert.That(steps[1].HintLocalDirection, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Parse_RejectsZeroPartNumber()
        {
            Assert.Throws<System.FormatException>(() =>
                DisassemblyPlanParser.Parse("round1: (0, -Z)"));
        }

        [Test]
        public void Parse_RejectsDuplicatePartNumber()
        {
            Assert.Throws<System.FormatException>(() =>
                DisassemblyPlanParser.Parse("round1: (1, -Z)\nround2: (1, +Z)"));
        }

        [Test]
        public void Parse_AcceptsCombinedAndCommaSeparatedMultiAxisDirections()
        {
            IReadOnlyList<DisassemblyStep> steps = DisassemblyPlanParser.Parse(
                "round1: (1, +Y-Z), (2, -X, +Z)");

            Assert.That(
                Vector3.Distance(
                    steps[0].HintLocalDirection,
                    new Vector3(0f, 1f, -1f).normalized),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Distance(
                    steps[1].HintLocalDirection,
                    new Vector3(-1f, 0f, 1f).normalized),
                Is.LessThan(0.0001f));
        }

        [Test]
        public void Parse_AllImportedModelPlans()
        {
            string[] planPaths = Directory.GetFiles(
                Path.Combine("Assets", "Art", "Models"),
                "*.txt",
                SearchOption.AllDirectories);

            Assert.That(planPaths.Length, Is.GreaterThanOrEqualTo(13));
            foreach (string planPath in planPaths)
            {
                IReadOnlyList<DisassemblyStep> steps = null;
                Assert.DoesNotThrow(
                    () => steps = DisassemblyPlanParser.Parse(File.ReadAllText(planPath)),
                    planPath);
                Assert.That(steps, Is.Not.Empty, planPath);
                Assert.That(steps.All(step => step.PartNumber >= 1), Is.True, planPath);
            }
        }

        [Test]
        public void CollisionPolicy_UsesTriggersWithoutDisablingMetaColliderQueries()
        {
            var partObject = new GameObject("Part");
            BoxCollider collider = partObject.AddComponent<BoxCollider>();
            Rigidbody rigidbody = partObject.AddComponent<Rigidbody>();

            try
            {
                AssemblyPart part = CreateConfiguredPart(partObject, 1, 0, 1, rigidbody);

                Assert.That(collider.isTrigger, Is.True);
                Assert.That(rigidbody.detectCollisions, Is.True);

                collider.isTrigger = false;
                part.ApplyCollisionPolicy();
                Assert.That(collider.isTrigger, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(partObject);
            }
        }

        [Test]
        public void Controller_UndoIsLifoAndRestoresRoundSnapshot()
        {
            var root = new GameObject("Assembly");
            var firstObject = new GameObject("First");
            var secondObject = new GameObject("Second");
            var thirdObject = new GameObject("Third");
            firstObject.transform.SetParent(root.transform, false);
            secondObject.transform.SetParent(root.transform, false);
            thirdObject.transform.SetParent(root.transform, false);

            try
            {
                AssemblyPart first = CreateConfiguredPart(firstObject, 1, 0, 1);
                AssemblyPart second = CreateConfiguredPart(secondObject, 2, 1, 1);
                AssemblyPart third = CreateConfiguredPart(thirdObject, 3, 2, 2);
                AssemblyController controller = root.AddComponent<AssemblyController>();
                controller.Configure(new[] { first, second, third });
                controller.ResetAllImmediate();

                CommitPose(first, new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 30f));
                Assert.That(controller.CurrentRound, Is.EqualTo(1));
                Assert.That(first.IsCompleted, Is.True);

                CommitPose(second, new Vector3(-2f, 1f, 4f), Quaternion.Euler(40f, 5f, 15f));
                Assert.That(controller.CurrentRound, Is.EqualTo(2));
                Assert.That(controller.OperationCount, Is.EqualTo(2));

                Assert.That(controller.UndoLastOperationImmediate(), Is.True);
                Assert.That(second.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(first.IsCompleted, Is.True);
                Assert.That(controller.CurrentRound, Is.EqualTo(1));

                Assert.That(controller.UndoLastOperationImmediate(), Is.True);
                Assert.That(first.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(controller.OperationCount, Is.Zero);
                Assert.That(controller.UndoLastOperationImmediate(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Controller_AllPartsStayInteractiveAndGuidanceSkipsEarlyCompletedRound()
        {
            var root = new GameObject("Assembly");
            var firstObject = new GameObject("First");
            var futureObject = new GameObject("Future");
            firstObject.transform.SetParent(root.transform, false);
            futureObject.transform.SetParent(root.transform, false);

            try
            {
                AssemblyPart first = CreateConfiguredPart(firstObject, 1, 0, 1);
                AssemblyPart future = CreateConfiguredPart(futureObject, 2, 1, 2);
                AssemblyController controller = root.AddComponent<AssemblyController>();
                controller.Configure(
                    new[] { first, future },
                    InteractionExperimentCondition.CurrentPartHighlightAndDirection);
                controller.ResetAllImmediate();

                Assert.That(first.InteractionEnabled, Is.True);
                Assert.That(future.InteractionEnabled, Is.True);
                Assert.That(first.GuidanceHighlighted, Is.True);
                Assert.That(first.DirectionGuidanceVisible, Is.True);
                Assert.That(future.GuidanceHighlighted, Is.False);

                CommitPose(future, Vector3.up, Quaternion.Euler(0f, 20f, 0f));
                Assert.That(future.IsCompleted, Is.True);
                Assert.That(controller.CurrentGuidanceRound, Is.EqualTo(1));
                Assert.That(first.GuidanceHighlighted, Is.True);

                CommitPose(first, Vector3.right, Quaternion.Euler(10f, 0f, 0f));
                Assert.That(controller.CurrentGuidanceRound, Is.Zero);
                Assert.That(first.GuidanceHighlighted, Is.False);
                Assert.That(first.DirectionGuidanceVisible, Is.False);

                Assert.That(controller.UndoLastOperationImmediate(), Is.True);
                Assert.That(controller.CurrentGuidanceRound, Is.EqualTo(1));
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(future.IsCompleted, Is.True);
                Assert.That(first.GuidanceHighlighted, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Controller_HighlightOnlyConditionDoesNotShowDirection()
        {
            var root = new GameObject("Assembly");
            var partObject = new GameObject("Part");
            partObject.transform.SetParent(root.transform, false);

            try
            {
                AssemblyPart part = CreateConfiguredPart(partObject, 1, 0, 1);
                AssemblyController controller = root.AddComponent<AssemblyController>();
                controller.Configure(
                    new[] { part },
                    InteractionExperimentCondition.CurrentPartHighlight);
                controller.ResetAllImmediate();

                Assert.That(part.InteractionEnabled, Is.True);
                Assert.That(part.GuidanceHighlighted, Is.True);
                Assert.That(part.DirectionGuidanceVisible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void GuidanceHighlight_UsesAStyleSeparateFromContactFeedback()
        {
            var root = new GameObject("Assembly");
            var partObject = new GameObject("Part");
            partObject.transform.SetParent(root.transform, false);

            try
            {
                HighlightEffect highlight = partObject.AddComponent<HighlightEffect>();
                AssemblyPart part = CreateConfiguredPart(
                    partObject,
                    1,
                    0,
                    1,
                    highlightEffect: highlight);
                AssemblyController controller = root.AddComponent<AssemblyController>();
                controller.Configure(
                    new[] { part },
                    InteractionExperimentCondition.CurrentPartHighlight);
                controller.ResetAllImmediate();

                Assert.That(highlight.highlighted, Is.True);
                Assert.That(highlight.outlineColor, Is.EqualTo(Color.green));
                Assert.That(highlight.outlineColor, Is.Not.EqualTo(Color.cyan));
                Assert.That(highlight.seeThrough, Is.EqualTo(SeeThroughMode.WhenHighlighted));
                Assert.That(highlight.seeThroughTintColor, Is.EqualTo(Color.green));
                Assert.That(highlight.seeThroughBorderColor, Is.EqualTo(Color.green));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OperationBelowBothThresholds_IsNotRecorded()
        {
            var root = new GameObject("Assembly");
            var partObject = new GameObject("Part");
            partObject.transform.SetParent(root.transform, false);

            try
            {
                AssemblyPart part = CreateConfiguredPart(partObject, 1, 0, 1);
                AssemblyController controller = root.AddComponent<AssemblyController>();
                controller.Configure(new[] { part });
                controller.ResetAllImmediate();

                Assert.That(part.BeginOperationRecording(), Is.True);
                part.transform.localPosition = Vector3.right * 0.001f;
                part.transform.localRotation = Quaternion.Euler(0f, 0.5f, 0f);
                Assert.That(part.CompleteOperationRecording(), Is.False);
                Assert.That(controller.OperationCount, Is.Zero);
                Assert.That(part.IsCompleted, Is.False);
                Assert.That(part.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(
                    Quaternion.Angle(part.transform.localRotation, Quaternion.identity),
                    Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UndoDuringPendingGrab_DoesNotAlsoPopCommittedHistory()
        {
            var root = new GameObject("Assembly");
            var partObject = new GameObject("Part");
            partObject.transform.SetParent(root.transform, false);

            try
            {
                AssemblyPart part = CreateConfiguredPart(partObject, 1, 0, 1);
                AssemblyController controller = root.AddComponent<AssemblyController>();
                controller.Configure(new[] { part });
                controller.ResetAllImmediate();

                Vector3 committedPosition = new(1f, 2f, 3f);
                CommitPose(part, committedPosition, Quaternion.Euler(10f, 20f, 30f));
                Assert.That(controller.OperationCount, Is.EqualTo(1));

                Assert.That(part.BeginOperationRecording(), Is.True);
                part.transform.localPosition = new Vector3(9f, 8f, 7f);
                Assert.That(controller.UndoLastOperationImmediate(), Is.True);
                Assert.That(part.transform.localPosition, Is.EqualTo(committedPosition));
                Assert.That(controller.OperationCount, Is.EqualTo(1));

                Assert.That(controller.UndoLastOperationImmediate(), Is.True);
                Assert.That(part.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(controller.OperationCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static AssemblyPart CreateConfiguredPart(
            GameObject gameObject,
            int partNumber,
            int childIndex,
            int round,
            Rigidbody rigidbody = null,
            HighlightEffect highlightEffect = null,
            AssemblyDirectionIndicator directionIndicator = null)
        {
            AssemblyPart part = gameObject.AddComponent<AssemblyPart>();
            part.Configure(
                partNumber,
                childIndex,
                round,
                Vector3.forward,
                0.005f,
                2f,
                rigidbody,
                null,
                null,
                null,
                highlightEffect,
                directionIndicator,
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
    }
}
