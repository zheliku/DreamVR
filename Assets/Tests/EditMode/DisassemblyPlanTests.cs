using System.Collections.Generic;
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
        public void Parse_UsesZeroBasedIndicesAndParentLocalDirections()
        {
            IReadOnlyList<DisassemblyStep> steps = DisassemblyPlanParser.Parse(E1Plan);

            Assert.That(steps.Count, Is.EqualTo(6));
            Assert.That(steps[0].Round, Is.EqualTo(1));
            Assert.That(steps[0].ChildIndex, Is.EqualTo(1));
            Assert.That(steps[0].LocalDirection, Is.EqualTo(Vector3.back));
            Assert.That(steps[1].ChildIndex, Is.EqualTo(7));
            Assert.That(steps[1].LocalDirection, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void Parse_RejectsDuplicateChildIndex()
        {
            Assert.Throws<System.FormatException>(() =>
                DisassemblyPlanParser.Parse("round1: (1, -Z)\nround2: (1, +Z)"));
        }

        [Test]
        public void TravelDistance_OuterRoundsAreNeverShorterThanInnerRounds()
        {
            IReadOnlyList<DisassemblyStep> steps = DisassemblyPlanParser.Parse(E1Plan);
            var candidates = new List<TravelDistanceCandidate>
            {
                new(steps[0], 0.2f),
                new(steps[1], 0.3f),
                new(steps[2], 0.4f),
                new(steps[3], 0.35f),
                new(steps[4], 0.6f),
                new(steps[5], 0.5f)
            };

            IReadOnlyDictionary<int, float> distances =
                DisassemblyTravelCalculator.EnforceOuterRoundsNotShorter(candidates);

            Assert.That(distances[1], Is.GreaterThanOrEqualTo(distances[2]));
            Assert.That(distances[2], Is.GreaterThanOrEqualTo(distances[3]));
            Assert.That(distances[7], Is.GreaterThanOrEqualTo(distances[6]));
            Assert.That(distances[6], Is.GreaterThanOrEqualTo(distances[4]));
            Assert.That(distances[3], Is.GreaterThanOrEqualTo(0.6f));
        }

        [Test]
        public void ConstrainLocalPosition_RemovesSidewaysAndBackwardMotion()
        {
            Vector3 initial = new(1f, 2f, 3f);
            Vector3 candidate = new(8f, -4f, 2f);

            Vector3 constrained = AssemblyPart.ConstrainLocalPosition(
                initial,
                candidate,
                Vector3.back,
                0.5f);

            Assert.That(constrained, Is.EqualTo(new Vector3(1f, 2f, 2.5f)));
        }

        [Test]
        public void CollisionPolicy_IgnoresOnlyCollidersInsideTheAssemblyRoot()
        {
            var root = new GameObject("Assembly");
            var firstObject = new GameObject("First");
            var secondObject = new GameObject("Second");
            firstObject.transform.SetParent(root.transform, worldPositionStays: false);
            secondObject.transform.SetParent(root.transform, worldPositionStays: false);
            BoxCollider firstCollider = firstObject.AddComponent<BoxCollider>();
            BoxCollider secondCollider = secondObject.AddComponent<BoxCollider>();

            try
            {
                AssemblyPart first = firstObject.AddComponent<AssemblyPart>();
                first.Configure(
                    1,
                    1,
                    Vector3.forward,
                    1f,
                    0.92f,
                    null,
                    null,
                    null,
                    null,
                    null,
                    root.transform,
                    ignoreInternalAssemblyCollisions: true);

                Assert.That(Physics.GetIgnoreCollision(firstCollider, secondCollider), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Controller_AdvancesRoundsAndResetRestoresInitialState()
        {
            var root = new GameObject("Assembly");
            var roundOneObject = new GameObject("RoundOnePart");
            var roundTwoObject = new GameObject("RoundTwoPart");
            roundOneObject.transform.SetParent(root.transform, worldPositionStays: false);
            roundTwoObject.transform.SetParent(root.transform, worldPositionStays: false);

            try
            {
                AssemblyPart roundOne = roundOneObject.AddComponent<AssemblyPart>();
                AssemblyPart roundTwo = roundTwoObject.AddComponent<AssemblyPart>();
                roundOne.Configure(1, 1, Vector3.forward, 1f, null, null, null, null, null);
                roundTwo.Configure(2, 2, Vector3.forward, 0.5f, null, null, null, null, null);

                AssemblyController controller = root.AddComponent<AssemblyController>();
                controller.Configure(new[] { roundOne, roundTwo });
                controller.ResetAllImmediate();

                Assert.That(controller.CurrentRound, Is.EqualTo(1));
                Assert.That(roundOne.InteractionEnabled, Is.True);
                Assert.That(roundTwo.InteractionEnabled, Is.False);

                roundOneObject.transform.localPosition = Vector3.forward;
                Assert.That(roundOne.EvaluateCompletionAfterRelease(), Is.True);
                Assert.That(roundOne.IsCompleted, Is.True);
                Assert.That(controller.CurrentRound, Is.EqualTo(2));
                Assert.That(roundTwo.InteractionEnabled, Is.True);

                controller.ResetAllImmediate();
                Assert.That(controller.CurrentRound, Is.EqualTo(1));
                Assert.That(roundOne.IsCompleted, Is.False);
                Assert.That(roundOneObject.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(roundOne.InteractionEnabled, Is.True);
                Assert.That(roundTwo.InteractionEnabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
