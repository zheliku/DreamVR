using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DreamVR.Assembly
{
    public enum InteractionExperimentCondition
    {
        NoGuidance = 0,
        CurrentPartHighlight = 1,
        CurrentPartHighlightAndDirection = 2
    }

    [DisallowMultipleComponent]
    public sealed class AssemblyController : MonoBehaviour
    {
        [SerializeField] private InteractionExperimentCondition _condition =
            InteractionExperimentCondition.NoGuidance;
        [SerializeField] private AssemblyPart[] _parts = System.Array.Empty<AssemblyPart>();
        [SerializeField] private int _currentRound;

        private Coroutine _resetRoutine;

        public InteractionExperimentCondition Condition => _condition;

        public int CurrentRound => _currentRound;

        public IReadOnlyList<AssemblyPart> Parts => _parts;

        private void OnEnable()
        {
            SubscribeParts();
        }

        private void Start()
        {
            ResetAllImmediate();
        }

        private void OnDisable()
        {
            UnsubscribeParts();
        }

        public void Configure(
            IReadOnlyList<AssemblyPart> parts,
            InteractionExperimentCondition condition = InteractionExperimentCondition.NoGuidance)
        {
            UnsubscribeParts();
            _parts = parts
                .Where(part => part != null)
                .OrderBy(part => part.Round)
                .ThenBy(part => part.ChildIndex)
                .ToArray();
            _condition = condition;
            SubscribeParts();
        }

        public void ResetAll()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                ResetAllImmediate();
                return;
            }

            if (_resetRoutine != null)
            {
                StopCoroutine(_resetRoutine);
            }

            _resetRoutine = StartCoroutine(ResetAllRoutine());
        }

        public void ResetAllImmediate()
        {
            foreach (AssemblyPart part in _parts)
            {
                part?.ResetPart();
            }

            _currentRound = GetFirstRound();
            ApplyRoundAvailability();
        }

        private IEnumerator ResetAllRoutine()
        {
            foreach (AssemblyPart part in _parts)
            {
                part?.SetInteractionEnabled(false);
                part?.SetGuidanceHighlighted(false);
            }

            yield return null;

            foreach (AssemblyPart part in _parts)
            {
                part?.ResetPart();
            }

            _currentRound = GetFirstRound();
            yield return null;
            ApplyRoundAvailability();
            _resetRoutine = null;
        }

        private void HandlePartReleasedAtEnd(AssemblyPart completedPart)
        {
            if (completedPart == null || completedPart.Round != _currentRound)
            {
                return;
            }

            completedPart.MarkCompleted(true);

            bool roundComplete = _parts
                .Where(part => part != null && part.Round == _currentRound)
                .All(part => part.IsCompleted);
            if (!roundComplete)
            {
                ApplyGuidanceState();
                return;
            }

            _currentRound = _parts
                .Where(part => part != null && part.Round > _currentRound)
                .Select(part => part.Round)
                .DefaultIfEmpty(0)
                .Min();
            ApplyRoundAvailability();
        }

        private int GetFirstRound()
        {
            return _parts
                .Where(part => part != null)
                .Select(part => part.Round)
                .DefaultIfEmpty(0)
                .Min();
        }

        private void ApplyRoundAvailability()
        {
            foreach (AssemblyPart part in _parts)
            {
                if (part == null)
                {
                    continue;
                }

                bool isCurrent = _currentRound > 0 && part.Round == _currentRound && !part.IsCompleted;
                part.SetInteractionEnabled(isCurrent);
            }

            ApplyGuidanceState();
        }

        private void ApplyGuidanceState()
        {
            bool showCurrentParts = _condition != InteractionExperimentCondition.NoGuidance;
            foreach (AssemblyPart part in _parts)
            {
                if (part == null)
                {
                    continue;
                }

                part.SetGuidanceHighlighted(
                    showCurrentParts
                    && _currentRound > 0
                    && part.Round == _currentRound
                    && !part.IsCompleted);
            }
        }

        private void SubscribeParts()
        {
            foreach (AssemblyPart part in _parts)
            {
                if (part == null)
                {
                    continue;
                }

                part.ReleasedAtEnd -= HandlePartReleasedAtEnd;
                part.ReleasedAtEnd += HandlePartReleasedAtEnd;
            }
        }

        private void UnsubscribeParts()
        {
            foreach (AssemblyPart part in _parts)
            {
                if (part != null)
                {
                    part.ReleasedAtEnd -= HandlePartReleasedAtEnd;
                }
            }
        }
    }
}
