using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DreamVR.Assembly
{
    public enum DisassemblyAxis
    {
        X,
        Y,
        Z
    }

    [Serializable]
    public sealed class DisassemblyStep
    {
        public DisassemblyStep(int round, int childIndex, DisassemblyAxis axis, int sign)
        {
            if (round < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(round));
            }

            if (childIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(childIndex));
            }

            if (sign != -1 && sign != 1)
            {
                throw new ArgumentOutOfRangeException(nameof(sign));
            }

            Round = round;
            ChildIndex = childIndex;
            Axis = axis;
            Sign = sign;
        }

        public int Round { get; }

        public int ChildIndex { get; }

        public DisassemblyAxis Axis { get; }

        public int Sign { get; }

        public Vector3 LocalDirection => Axis switch
        {
            DisassemblyAxis.X => Vector3.right * Sign,
            DisassemblyAxis.Y => Vector3.up * Sign,
            DisassemblyAxis.Z => Vector3.forward * Sign,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public static class DisassemblyPlanParser
    {
        private static readonly Regex RoundPattern = new(
            @"^\s*round(?<round>\d+)\s*:\s*(?<entries>.*?)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex EntryPattern = new(
            @"\(\s*(?<index>\d+)\s*,\s*(?<sign>[+-])\s*(?<axis>[XYZ])\s*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static IReadOnlyList<DisassemblyStep> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new FormatException("拆卸顺序文件为空。");
            }

            var steps = new List<DisassemblyStep>();
            var childIndices = new HashSet<int>();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                Match roundMatch = RoundPattern.Match(line);
                if (!roundMatch.Success)
                {
                    throw new FormatException($"第 {lineIndex + 1} 行格式错误：{line}");
                }

                int round = int.Parse(roundMatch.Groups["round"].Value);
                string entries = roundMatch.Groups["entries"].Value;
                MatchCollection entryMatches = EntryPattern.Matches(entries);
                if (entryMatches.Count == 0)
                {
                    throw new FormatException($"第 {lineIndex + 1} 行没有零件条目。");
                }

                string remainder = EntryPattern.Replace(entries, string.Empty).Replace(",", string.Empty).Trim();
                if (remainder.Length != 0)
                {
                    throw new FormatException($"第 {lineIndex + 1} 行存在无法解析的内容：{remainder}");
                }

                foreach (Match entryMatch in entryMatches)
                {
                    int childIndex = int.Parse(entryMatch.Groups["index"].Value);
                    if (!childIndices.Add(childIndex))
                    {
                        throw new FormatException($"子物体下标 {childIndex} 重复出现。");
                    }

                    int sign = entryMatch.Groups["sign"].Value == "+" ? 1 : -1;
                    DisassemblyAxis axis = Enum.Parse<DisassemblyAxis>(
                        entryMatch.Groups["axis"].Value,
                        ignoreCase: true);
                    steps.Add(new DisassemblyStep(round, childIndex, axis, sign));
                }
            }

            if (steps.Count == 0)
            {
                throw new FormatException("拆卸顺序文件没有有效条目。");
            }

            return steps
                .OrderBy(step => step.Round)
                .ThenBy(step => step.ChildIndex)
                .ToArray();
        }
    }

    public readonly struct TravelDistanceCandidate
    {
        public TravelDistanceCandidate(DisassemblyStep step, float requiredDistance)
        {
            Step = step ?? throw new ArgumentNullException(nameof(step));
            RequiredDistance = Mathf.Max(0.001f, requiredDistance);
        }

        public DisassemblyStep Step { get; }

        public float RequiredDistance { get; }
    }

    public static class DisassemblyTravelCalculator
    {
        public static IReadOnlyDictionary<int, float> EnforceOuterRoundsNotShorter(
            IReadOnlyList<TravelDistanceCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                throw new ArgumentException("至少需要一个移动距离候选项。", nameof(candidates));
            }

            var result = new Dictionary<int, float>();
            foreach (IGrouping<(DisassemblyAxis Axis, int Sign), TravelDistanceCandidate> group in
                     candidates.GroupBy(candidate => (candidate.Step.Axis, candidate.Step.Sign)))
            {
                float innerDistance = 0f;
                foreach (TravelDistanceCandidate candidate in group
                             .OrderByDescending(item => item.Step.Round)
                             .ThenBy(item => item.Step.ChildIndex))
                {
                    innerDistance = Mathf.Max(innerDistance, candidate.RequiredDistance);
                    result[candidate.Step.ChildIndex] = innerDistance;
                }
            }

            return result;
        }
    }
}
