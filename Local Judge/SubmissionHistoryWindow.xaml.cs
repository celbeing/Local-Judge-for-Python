using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Local_Judge
{
    public partial class SubmissionHistoryWindow : Window
    {
        private readonly List<SubmissionAttemptRow> _attemptRows;

        public SubmissionHistoryWindow(
            ProblemDocument problem,
            IReadOnlyList<SubmissionAttemptHistoryItem> historyItems)
        {
            InitializeComponent();

            string problemTitle = string.IsNullOrWhiteSpace(problem.Id)
                ? problem.Title
                : $"[{problem.Id}] {problem.Title}";
            ProblemTitleTextBlock.Text = $"{problemTitle} 제출 이력";

            _attemptRows = historyItems
                .Select(item => new SubmissionAttemptRow(item))
                .ToList();

            AttemptsDataGrid.ItemsSource = _attemptRows;
            EmptyTextBlock.Visibility = _attemptRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SummaryTextBlock.Text = _attemptRows.Count == 0
                ? "저장된 제출이 없습니다."
                : $"총 {_attemptRows.Count}개 제출";

            if (_attemptRows.Count > 0)
            {
                AttemptsDataGrid.SelectedIndex = 0;
            }
        }

        private void AttemptsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AttemptsDataGrid.SelectedItem is not SubmissionAttemptRow row)
            {
                CodeTextBox.Text = string.Empty;
                DetailTextBox.Text = string.Empty;
                TestResultsDataGrid.ItemsSource = null;
                return;
            }

            CodeTextBox.Text = row.Attempt.Code;
            TestResultsDataGrid.ItemsSource = row.Attempt.TestResults
                .Select(result => new SubmissionTestResultRow(result))
                .ToList();
            DetailTextBox.Text = BuildDetailText(row);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string BuildDetailText(SubmissionAttemptRow row)
        {
            SubmissionAttemptDocument attempt = row.Attempt;
            var builder = new StringBuilder();

            builder.AppendLine($"AttemptId: {attempt.AttemptId}");
            builder.AppendLine($"SubmittedAt: {attempt.SubmittedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff}");
            builder.AppendLine($"Language: {GetLanguage(attempt)}");
            builder.AppendLine($"Verdict: {attempt.Verdict}");
            builder.AppendLine($"Passed: {attempt.PassedCount}/{attempt.TotalCount}");
            builder.AppendLine($"HistoryFile: {row.FilePath}");

            if (!string.IsNullOrWhiteSpace(attempt.ProblemFilePath))
            {
                builder.AppendLine($"ProblemFile: {attempt.ProblemFilePath}");
            }

            builder.AppendLine();
            builder.AppendLine("[Problem]");
            builder.AppendLine($"Id: {attempt.Problem.Id}");
            builder.AppendLine($"Title: {attempt.Problem.Title}");
            builder.AppendLine($"Author: {attempt.Problem.AuthorName}");
            builder.AppendLine($"Source: {attempt.Problem.Source}");

            builder.AppendLine();
            builder.AppendLine("[Limits]");
            builder.AppendLine($"IdealTimeLimitMs: {attempt.Limits.IdealTimeLimitMs}");
            builder.AppendLine($"IdealMemoryLimitMb: {attempt.Limits.IdealMemoryLimitMb}");
            builder.AppendLine($"AppliedTimeLimitMs: {attempt.Limits.AppliedTimeLimitMs}");
            builder.AppendLine($"AppliedMemoryLimitMb: {attempt.Limits.AppliedMemoryLimitMb}");
            builder.AppendLine($"OutputLimitBytes: {attempt.Limits.OutputLimitBytes}");

            builder.AppendLine();
            builder.AppendLine("[Benchmark]");
            builder.AppendLine($"IsFallback: {attempt.Benchmark.IsFallback}");
            builder.AppendLine($"TimeMultiplier: {attempt.Benchmark.TimeMultiplier:0.00}");
            builder.AppendLine($"ExtraTimeMs: {attempt.Benchmark.ExtraTimeMs}");
            builder.AppendLine($"ExtraMemoryMb: {attempt.Benchmark.ExtraMemoryMb}");

            return builder.ToString();
        }

        private static string FormatMemoryBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "-";
            }

            return $"{bytes / 1024d / 1024d:0.#} MB";
        }

        private static string GetLanguage(SubmissionAttemptDocument attempt)
        {
            return string.IsNullOrWhiteSpace(attempt.Language) ? "Python" : attempt.Language;
        }

        private static string PreviewText(string text, bool truncated)
        {
            if (string.IsNullOrEmpty(text))
            {
                return truncated ? "<empty, truncated>" : string.Empty;
            }

            string normalized = text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", "\\n");

            if (normalized.Length > 120)
            {
                normalized = normalized[..120] + "...";
            }

            return truncated ? normalized + " (truncated)" : normalized;
        }

        private sealed class SubmissionAttemptRow
        {
            public SubmissionAttemptRow(SubmissionAttemptHistoryItem item)
            {
                FilePath = item.FilePath;
                Attempt = item.Attempt;
            }

            public string FilePath { get; }
            public SubmissionAttemptDocument Attempt { get; }
            public string SubmittedAtText => Attempt.SubmittedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            public string Language => GetLanguage(Attempt);
            public string Verdict => Attempt.Verdict;
            public string PassedText => $"{Attempt.PassedCount}/{Attempt.TotalCount}";
            public string MaxElapsedText => Attempt.TestResults.Count == 0
                ? "-"
                : $"{Attempt.TestResults.Max(result => result.ElapsedMs):0} ms";
            public string MaxMemoryText => Attempt.TestResults.Count == 0
                ? "-"
                : FormatMemoryBytes(Attempt.TestResults.Max(result => result.PeakMemoryBytes));
            public string AppliedLimitsText => $"{Attempt.Limits.AppliedTimeLimitMs} ms / {Attempt.Limits.AppliedMemoryLimitMb} MB";
            public string FileName => Path.GetFileName(FilePath);
        }

        private sealed class SubmissionTestResultRow
        {
            private readonly SubmissionTestResultDocument _result;

            public SubmissionTestResultRow(SubmissionTestResultDocument result)
            {
                _result = result;
            }

            public int TestNumber => _result.TestNumber;
            public string Verdict => _result.Verdict;
            public string ElapsedText => $"{_result.ElapsedMs:0} ms";
            public string MemoryText => FormatMemoryBytes(_result.PeakMemoryBytes);
            public int ExitCode => _result.ExitCode;
            public string OutputSummary => PreviewText(_result.StandardOutput, _result.StandardOutputTruncated);
            public string ErrorSummary => PreviewText(_result.StandardError, _result.StandardErrorTruncated);
        }
    }
}
