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
        private sealed class OperationRecord
        {
            public AssemblyPart Part;
            public AssemblyPartPose BeforePose;
            public int RoundBefore;
            public bool[] CompletionBefore;
        }

        [SerializeField] private InteractionExperimentCondition _condition =
            InteractionExperimentCondition.NoGuidance;
        [SerializeField] private AssemblyPart[] _parts = System.Array.Empty<AssemblyPart>();
        [SerializeField] private int _currentRound;

        private readonly Stack<OperationRecord> _operationHistory = new();
        private Coroutine _resetRoutine;
        private Coroutine _undoRoutine;
        private int _queuedUndoRequests;

        public InteractionExperimentCondition Condition => _condition;

        public int CurrentRound => _currentRound;

        public int CurrentGuidanceRound => _currentRound;

        public AssemblyPart CurrentGuidancePart => GetCurrentGuidancePart();

        public IReadOnlyList<AssemblyPart> Parts => _parts;

        public int OperationCount => _operationHistory.Count;

        public bool CanUndo => _operationHistory.Count > 0;

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
            StopPoseRestoreRoutinesAndRecoverParts();
        }

        public void Configure(
            IReadOnlyList<AssemblyPart> parts,
            InteractionExperimentCondition condition = InteractionExperimentCondition.NoGuidance)
        {
            UnsubscribeParts();
            _parts = parts
                .Where(part => part != null)
                .OrderBy(part => part.Round)
                .ThenBy(part => part.PartNumber)
                .ToArray();
            _condition = condition;
            _operationHistory.Clear();
            SubscribeParts();
        }

        /// <summary>
        /// Restores the most recent completed grab-and-release operation.
        /// This signature is intentionally parameterless for a persistent UnityEvent binding.
        /// </summary>
        public void UndoLastOperation()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                UndoLastOperationImmediate();
                return;
            }

            _queuedUndoRequests++;
            if (_undoRoutine == null)
            {
                _undoRoutine = StartCoroutine(UndoQueuedOperationsRoutine());
            }
        }

        public bool UndoLastOperationImmediate()
        {
            bool canceledPendingOperation = _parts.Any(part => part != null && part.HasPendingOperation);
            foreach (AssemblyPart part in _parts)
            {
                part?.CancelPendingOperation(restoreStartPose: true);
            }

            if (canceledPendingOperation || _operationHistory.Count == 0)
            {
                ApplyPartStates();
                return canceledPendingOperation;
            }

            RestoreOperation(_operationHistory.Pop());
            ApplyPartStates();
            return true;
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
            _operationHistory.Clear();
            _queuedUndoRequests = 0;
            foreach (AssemblyPart part in _parts)
            {
                part?.ResetPart();
            }

            _currentRound = GetFirstRound();
            ApplyPartStates();
        }

        private IEnumerator UndoQueuedOperationsRoutine()
        {
            while (_queuedUndoRequests > 0)
            {
                _queuedUndoRequests--;
                SuspendAllInteractions();
                yield return null;

                bool canceledPendingOperation = _parts.Any(
                    part => part != null && part.HasPendingOperation);
                foreach (AssemblyPart part in _parts)
                {
                    part?.CancelPendingOperation(restoreStartPose: true);
                }

                if (!canceledPendingOperation && _operationHistory.Count > 0)
                {
                    RestoreOperation(_operationHistory.Pop());
                }

                yield return null;
                RestoreAllInteractions();
                ApplyPartStates();
            }

            _undoRoutine = null;
        }

        private IEnumerator ResetAllRoutine()
        {
            SuspendAllInteractions();
            yield return null;

            _operationHistory.Clear();
            _queuedUndoRequests = 0;
            foreach (AssemblyPart part in _parts)
            {
                part?.ResetPart();
            }

            _currentRound = GetFirstRound();
            yield return null;

            RestoreAllInteractions();
            ApplyPartStates();
            _resetRoutine = null;
        }

        private void HandleOperationCommitted(
            AssemblyPart operatedPart,
            AssemblyPartPose beforePose,
            AssemblyPartPose _)
        {
            if (operatedPart == null || !_parts.Contains(operatedPart))
            {
                return;
            }

            _operationHistory.Push(new OperationRecord
            {
                Part = operatedPart,
                BeforePose = beforePose,
                RoundBefore = _currentRound,
                CompletionBefore = _parts.Select(part => part != null && part.IsCompleted).ToArray()
            });

            if (!operatedPart.IsCompleted)
            {
                operatedPart.MarkCompleted(true);
            }

            AdvanceGuidanceRoundPastCompletedRounds();
            ApplyPartStates();
        }

        private void AdvanceGuidanceRoundPastCompletedRounds()
        {
            while (_currentRound > 0)
            {
                AssemblyPart[] currentParts = _parts
                    .Where(part => part != null && part.Round == _currentRound)
                    .ToArray();
                if (currentParts.Length == 0 || currentParts.Any(part => !part.IsCompleted))
                {
                    return;
                }

                _currentRound = _parts
                    .Where(part => part != null && part.Round > _currentRound)
                    .Select(part => part.Round)
                    .DefaultIfEmpty(0)
                    .Min();
            }
        }

        private void RestoreOperation(OperationRecord operation)
        {
            operation.Part?.RestorePose(operation.BeforePose);
            _currentRound = operation.RoundBefore;
            for (int index = 0; index < _parts.Length; index++)
            {
                if (_parts[index] != null)
                {
                    bool completed = index < operation.CompletionBefore.Length
                        && operation.CompletionBefore[index];
                    _parts[index].MarkCompleted(completed);
                }
            }
        }

        private void SuspendAllInteractions()
        {
            foreach (AssemblyPart part in _parts)
            {
                part?.SuspendInteractionForPoseRestore();
            }
        }

        private void RestoreAllInteractions()
        {
            foreach (AssemblyPart part in _parts)
            {
                part?.RestoreInteractionAfterPoseRestore();
            }
        }

        private void StopPoseRestoreRoutinesAndRecoverParts()
        {
            if (_undoRoutine != null)
            {
                StopCoroutine(_undoRoutine);
                _undoRoutine = null;
            }

            if (_resetRoutine != null)
            {
                StopCoroutine(_resetRoutine);
                _resetRoutine = null;
            }

            _queuedUndoRequests = 0;
            bool recoveredSuspension = false;
            foreach (AssemblyPart part in _parts)
            {
                if (part == null || !part.IsInteractionSuspended)
                {
                    continue;
                }

                part.CancelPendingOperation(restoreStartPose: true);
                part.RestoreInteractionAfterPoseRestore();
                recoveredSuspension = true;
            }

            if (recoveredSuspension)
            {
                ApplyPartStates();
            }
        }

        private int GetFirstRound()
        {
            return _parts
                .Where(part => part != null)
                .Select(part => part.Round)
                .DefaultIfEmpty(0)
                .Min();
        }

        private void ApplyPartStates()
        {
            foreach (AssemblyPart part in _parts)
            {
                if (part == null)
                {
                    continue;
                }

                part.SetInteractionEnabled(true);
                part.SetCompletedInteractionAppearance(part.IsCompleted);
            }

            ApplyGuidanceState();
        }

        private void ApplyGuidanceState()
        {
            bool showCurrentParts = _condition != InteractionExperimentCondition.NoGuidance;
            bool showOnlyCurrentPart = _condition == InteractionExperimentCondition.CurrentPartHighlight;
            bool showDirections = _condition
                == InteractionExperimentCondition.CurrentPartHighlightAndDirection;
            AssemblyPart guidancePart = showOnlyCurrentPart ? GetCurrentGuidancePart() : null;
            foreach (AssemblyPart part in _parts)
            {
                if (part == null)
                {
                    continue;
                }

                bool isCurrentIncompletePart = _currentRound > 0
                    && part.Round == _currentRound
                    && !part.IsCompleted;
                bool shouldHighlight = showOnlyCurrentPart
                    ? part == guidancePart
                    : isCurrentIncompletePart;
                part.SetGuidanceHighlighted(showCurrentParts && shouldHighlight);
                part.SetDirectionGuidanceVisible(showDirections && isCurrentIncompletePart);
            }
        }

        private AssemblyPart GetCurrentGuidancePart()
        {
            if (_currentRound <= 0)
            {
                return null;
            }

            return _parts
                .Where(part => part != null
                    && part.Round == _currentRound
                    && !part.IsCompleted)
                .OrderBy(part => part.PartNumber)
                .ThenBy(part => part.ChildIndex)
                .FirstOrDefault();
        }

        private void SubscribeParts()
        {
            foreach (AssemblyPart part in _parts)
            {
                if (part == null)
                {
                    continue;
                }

                part.OperationCommitted -= HandleOperationCommitted;
                part.OperationCommitted += HandleOperationCommitted;
            }
        }

        private void UnsubscribeParts()
        {
            foreach (AssemblyPart part in _parts)
            {
                if (part != null)
                {
                    part.OperationCommitted -= HandleOperationCommitted;
                }
            }
        }
    }
}
