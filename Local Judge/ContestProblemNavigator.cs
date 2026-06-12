using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Local_Judge
{
    public sealed class ContestProblemNavigator
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public ContestProblemNavigator(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        public IReadOnlyList<TreeViewItem> CreateProblemTreeItems(
            ContestContext contest,
            string? selectedProblemRelativePath)
        {
            var items = new List<TreeViewItem>();
            foreach (ContestProblemItem problem in contest.Problems)
            {
                UpdateProblemStatus(contest, problem);
                items.Add(new TreeViewItem
                {
                    Header = CreateProblemTreeHeader(problem),
                    Tag = problem,
                    IsSelected = string.Equals(
                        problem.RelativePath,
                        selectedProblemRelativePath,
                        StringComparison.OrdinalIgnoreCase)
                });
            }

            return items;
        }

        public void UpdateProblemStatus(ContestContext contest, ContestProblemItem problem)
        {
            IReadOnlyList<SubmissionAttemptHistoryItem> attempts = LoadAttempts(contest, problem);
            problem.AttemptCount = attempts.Count;
            problem.HasAccepted = attempts.Any(item => string.Equals(item.Attempt.Verdict, "AC", StringComparison.OrdinalIgnoreCase));
            problem.LastVerdict = attempts
                .OrderBy(item => item.Attempt.SubmittedAt)
                .LastOrDefault()
                ?.Attempt
                .Verdict ?? string.Empty;
        }

        public IReadOnlyList<SubmissionAttemptHistoryItem> LoadAttempts(
            ContestContext contest,
            ContestProblemItem problem)
        {
            var attempts = new List<SubmissionAttemptHistoryItem>();
            var seenAttemptKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string submissionsRoot in GetSubmissionRoots(contest))
            {
                string problemDirectory = Path.Combine(submissionsRoot, problem.SubmissionKey);
                if (!Directory.Exists(problemDirectory))
                {
                    continue;
                }

                foreach (string filePath in Directory.EnumerateFiles(problemDirectory, "*.json"))
                {
                    try
                    {
                        string json = File.ReadAllText(filePath);
                        SubmissionAttemptDocument? attempt = JsonSerializer.Deserialize<SubmissionAttemptDocument>(json, _jsonOptions);
                        if (attempt is null)
                        {
                            continue;
                        }

                        attempt.ContestId = contest.ContestId;
                        attempt.ContestTitle = contest.Title;
                        attempt.ContestProblemLabel = problem.Label;
                        attempt.ContestProblemRelativePath = problem.RelativePath;

                        string attemptKey = string.IsNullOrWhiteSpace(attempt.AttemptId)
                            ? Path.GetFileNameWithoutExtension(filePath)
                            : attempt.AttemptId;
                        if (!seenAttemptKeys.Add(attemptKey))
                        {
                            continue;
                        }

                        attempts.Add(new SubmissionAttemptHistoryItem(filePath, attempt));
                    }
                    catch
                    {
                        // Malformed contest submission files should not block valid attempts.
                    }
                }
            }

            return attempts
                .OrderByDescending(item => item.Attempt.SubmittedAt)
                .ToList();
        }

        public ContestSubmissionSaveResult SaveSubmissionAttempt(
            SubmissionAttemptDocument attempt,
            ContestContext contest,
            ContestProblemItem contestProblem)
        {
            attempt.ContestId = contest.ContestId;
            attempt.ContestTitle = contest.Title;
            attempt.ContestProblemLabel = contestProblem.Label;
            attempt.ContestProblemRelativePath = contestProblem.RelativePath;

            string attemptId = string.IsNullOrWhiteSpace(attempt.AttemptId)
                ? SubmissionHistoryStore.CreateAttemptId(attempt.SubmittedAt)
                : Regex.Replace(attempt.AttemptId, @"[\\/:*?""<>|]+", "_");
            string json = JsonSerializer.Serialize(attempt, _jsonOptions);
            string? primarySavedPath = null;
            var failures = new List<string>();

            foreach (string submissionsRoot in GetSubmissionRoots(contest))
            {
                try
                {
                    string problemDirectory = Path.Combine(submissionsRoot, contestProblem.SubmissionKey);
                    Directory.CreateDirectory(problemDirectory);

                    string filePath = Path.Combine(problemDirectory, attemptId + ".json");
                    File.WriteAllText(filePath, json);
                    primarySavedPath ??= filePath;
                }
                catch (Exception ex)
                {
                    failures.Add($"{submissionsRoot}: {ex.Message}");
                }
            }

            if (primarySavedPath is null)
            {
                throw new InvalidOperationException(
                    "\uB300\uD68C \uC81C\uCD9C \uC774\uB825\uC744 \uC800\uC7A5\uD558\uC9C0 \uBABB\uD588\uC2B5\uB2C8\uB2E4.\n"
                    + string.Join("\n", failures));
            }

            return new ContestSubmissionSaveResult(primarySavedPath, failures);
        }

        public ContestProblemItem? ResolveCurrentProblem(
            ContestContext contest,
            ContestProblemItem? currentContestProblem,
            ProblemDocument? currentProblem,
            string? currentProblemFilePath)
        {
            if (currentProblem is null)
            {
                return null;
            }

            if (currentContestProblem is not null
                && IsSamePath(currentContestProblem.FilePath, currentProblemFilePath))
            {
                return currentContestProblem;
            }

            if (!string.IsNullOrWhiteSpace(currentProblemFilePath))
            {
                ContestProblemItem? pathMatch = contest.Problems.FirstOrDefault(problem =>
                    IsSamePath(problem.FilePath, currentProblemFilePath));
                if (pathMatch is not null)
                {
                    return pathMatch;
                }
            }

            List<ContestProblemItem> documentMatches = contest.Problems
                .Where(problem => ReferenceEquals(problem.Problem, currentProblem))
                .ToList();
            return documentMatches.Count == 1
                ? documentMatches[0]
                : null;
        }

        public static string FormatProblemTreeText(ContestProblemItem problem)
        {
            string name = FormatProblemName(problem);
            if (problem.HasAccepted)
            {
                return $"{name} (AC)";
            }

            if (problem.AttemptCount == 0 || string.IsNullOrWhiteSpace(problem.LastVerdict))
            {
                return name;
            }

            return $"{name} ({problem.LastVerdict})";
        }

        public static string FormatProblemName(ContestProblemItem problem)
        {
            string title = string.IsNullOrWhiteSpace(problem.Problem.Id)
                ? problem.Problem.Title
                : $"[{problem.Problem.Id}] {problem.Problem.Title}";
            return $"{problem.Label}. {title}";
        }

        public static StackPanel CreateProblemTreeHeader(ContestProblemItem problem)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            panel.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = GetBalloonBrush(problem),
                Stroke = Brushes.DimGray,
                StrokeThickness = 0.5,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "\uD48D\uC120 \uC0C9"
            });
            panel.Children.Add(new TextBlock
            {
                Text = FormatProblemTreeText(problem),
                Foreground = GetProblemBrush(problem),
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        private static IEnumerable<string> GetSubmissionRoots(ContestContext contest)
        {
            if (!string.IsNullOrWhiteSpace(contest.SubmissionsRoot))
            {
                yield return contest.SubmissionsRoot;
            }

            if (!string.IsNullOrWhiteSpace(contest.SessionSubmissionsRoot)
                && !string.Equals(contest.SessionSubmissionsRoot, contest.SubmissionsRoot, StringComparison.OrdinalIgnoreCase))
            {
                yield return contest.SessionSubmissionsRoot;
            }
        }

        private static Brush GetProblemBrush(ContestProblemItem problem)
        {
            if (problem.HasAccepted)
            {
                return Brushes.ForestGreen;
            }

            if (problem.AttemptCount > 0)
            {
                return Brushes.Firebrick;
            }

            return Brushes.Black;
        }

        private static Brush GetBalloonBrush(ContestProblemItem problem)
        {
            string colorText = string.IsNullOrWhiteSpace(problem.BalloonColor)
                ? "#7F8C8D"
                : problem.BalloonColor.Trim();

            try
            {
                if (ColorConverter.ConvertFromString(colorText) is Color color)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    return brush;
                }
            }
            catch
            {
                // Invalid contest metadata falls back to a neutral color.
            }

            return Brushes.Gray;
        }

        private static bool IsSamePath(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed record ContestSubmissionSaveResult(
        string FilePath,
        IReadOnlyList<string> Failures);
}
