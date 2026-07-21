using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DreamVR.Assembly
{
    [Serializable]
    public sealed class DisassemblyStep
    {
        public DisassemblyStep(int round, int partNumber, Vector3 hintLocalDirection)
        {
            if (round < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(round));
            }

            if (partNumber < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(partNumber));
            }

            if (hintLocalDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                throw new ArgumentException("提示方向不能为零向量。", nameof(hintLocalDirection));
            }

            Round = round;
            PartNumber = partNumber;
            HintLocalDirection = hintLocalDirection.normalized;
        }

        public int Round { get; }

        /// <summary>The human-facing identifier written in the txt file. It is one-based.</summary>
        public int PartNumber { get; }

        /// <summary>The zero-based direct-child index used by Unity's Transform API.</summary>
        public int ChildIndex => PartNumber - 1;

        /// <summary>
        /// Direction metadata in the indexed parent's local space. It drives visual guidance only.
        /// </summary>
        public Vector3 HintLocalDirection { get; }
    }

    public static class DisassemblyPlanParser
    {
        private static readonly Regex RoundPattern = new(
            @"^\s*round(?<round>\d+)\s*:\s*(?<entries>.*?)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex EntryPattern = new(
            @"\((?<content>[^()]*)\)",
            RegexOptions.Compiled);

        private static readonly Regex DirectionComponentPattern = new(
            @"(?<sign>[+-])(?<axis>[XYZ])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static IReadOnlyList<DisassemblyStep> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new FormatException("拆卸顺序文件为空。");
            }

            var steps = new List<DisassemblyStep>();
            var partNumbers = new HashSet<int>();
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
                    string content = entryMatch.Groups["content"].Value;
                    string[] fields = content
                        .Split(',')
                        .Select(field => field.Trim())
                        .Where(field => field.Length > 0)
                        .ToArray();
                    if (fields.Length < 2 || !int.TryParse(fields[0], out int partNumber))
                    {
                        throw new FormatException(
                            $"第 {lineIndex + 1} 行零件条目格式错误：({content})");
                    }

                    if (partNumber < 1)
                    {
                        throw new FormatException(
                            $"零件序号必须从 1 开始，不能使用 {partNumber}。");
                    }

                    if (!partNumbers.Add(partNumber))
                    {
                        throw new FormatException($"零件序号 {partNumber} 重复出现。");
                    }

                    string directionExpression = string.Concat(fields.Skip(1))
                        .Replace(" ", string.Empty)
                        .ToUpperInvariant();
                    Vector3 direction = ParseDirection(
                        directionExpression,
                        lineIndex + 1,
                        content);
                    steps.Add(new DisassemblyStep(round, partNumber, direction));
                }
            }

            if (steps.Count == 0)
            {
                throw new FormatException("拆卸顺序文件没有有效条目。");
            }

            return steps
                .OrderBy(step => step.Round)
                .ThenBy(step => step.PartNumber)
                .ToArray();
        }

        private static Vector3 ParseDirection(
            string expression,
            int lineNumber,
            string originalEntry)
        {
            MatchCollection matches = DirectionComponentPattern.Matches(expression);
            string parsedExpression = string.Concat(
                matches.Cast<Match>().Select(match => match.Value.ToUpperInvariant()));
            if (matches.Count == 0 || parsedExpression != expression)
            {
                throw new FormatException(
                    $"第 {lineNumber} 行方向格式错误：({originalEntry})");
            }

            var usedAxes = new HashSet<char>();
            Vector3 direction = Vector3.zero;
            foreach (Match match in matches)
            {
                char axis = char.ToUpperInvariant(match.Groups["axis"].Value[0]);
                if (!usedAxes.Add(axis))
                {
                    throw new FormatException(
                        $"第 {lineNumber} 行方向轴 {axis} 重复：({originalEntry})");
                }

                float sign = match.Groups["sign"].Value == "+" ? 1f : -1f;
                direction += axis switch
                {
                    'X' => Vector3.right * sign,
                    'Y' => Vector3.up * sign,
                    'Z' => Vector3.forward * sign,
                    _ => throw new ArgumentOutOfRangeException()
                };
            }

            return direction.normalized;
        }
    }
}
