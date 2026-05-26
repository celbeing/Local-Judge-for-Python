using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Local_Judge
{
    public partial class SubmissionHistoryFileWindow : Window
    {
        private readonly SubmissionHistoryInspectionDocument _document;
        private readonly List<ProblemSummaryRow> _problemRows;

        public SubmissionHistoryFileWindow(SubmissionHistoryInspectionDocument document)
        {
            InitializeComponent();

            _document = document;
            _problemRows = document.Problems
                .Select(problem => new ProblemSummaryRow(problem))
                .ToList();

            TitleTextBlock.Text = string.IsNullOrWhiteSpace(document.Manifest.ExportName)
                ? "제출 이력 파일"
                : document.Manifest.ExportName;
            SummaryTextBlock.Text =
                $"종류: {document.Manifest.ExportKind} / 내보낸 시각: {FormatDateTime(document.Manifest.ExportedAt)} / 문항: {document.Problems.Count}개 / 제출: {_problemRows.Sum(row => row.AttemptCount)}개 / 건너뜀: {document.SkippedFileCount}개";

            ProblemSummaryDataGrid.ItemsSource = _problemRows;
            ConfigureContestSummary();

            if (_problemRows.Count > 0)
            {
                ProblemSummaryDataGrid.SelectedIndex = 0;
            }
        }

        private void ConfigureContestSummary()
        {
            bool isContest = string.Equals(_document.Manifest.ExportKind, "Contest", StringComparison.OrdinalIgnoreCase)
                             && _document.Manifest.ContestSettings is not null;
            if (!isContest)
            {
                ContestSummaryGroupBox.Visibility = Visibility.Collapsed;
                return;
            }

            SubmissionHistoryContestSettings settings = _document.Manifest.ContestSettings!;
            List<ContestProblemRow> rows = _problemRows
                .Select(row => new ContestProblemRow(row, settings))
                .ToList();

            int solvedCount = rows.Count(row => row.Solved);
            int totalScore = rows.Sum(row => row.Score);
            int totalPenalty = rows.Where(row => row.Solved).Sum(row => row.PenaltyMinutes);

            ContestSummaryGroupBox.Visibility = Visibility.Visible;
            ContestSummaryTextBlock.Text =
                $"맞은 문항: {solvedCount}개 / 최종 점수: {totalScore} / 패널티 총합: {totalPenalty}분 / 오답 패널티: {settings.WrongSubmissionPenaltyMinutes}분";
            ContestSummaryDataGrid.ItemsSource = rows;
        }

        private void ProblemSummaryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProblemSummaryDataGrid.SelectedItem is not ProblemSummaryRow row)
            {
                SubmissionsDataGrid.ItemsSource = null;
                CodeTextBox.Text = string.Empty;
                TestResultsDataGrid.ItemsSource = null;
                return;
            }

            SubmissionsDataGrid.ItemsSource = row.SubmissionRows;
            if (row.SubmissionRows.Count > 0)
            {
                SubmissionsDataGrid.SelectedIndex = 0;
            }
            else
            {
                CodeTextBox.Text = string.Empty;
                TestResultsDataGrid.ItemsSource = null;
            }
        }

        private void SubmissionsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SubmissionsDataGrid.SelectedItem is not SubmissionAttemptRow row)
            {
                CodeTextBox.Text = string.Empty;
                TestResultsDataGrid.ItemsSource = null;
                return;
            }

            CodeTextBox.Text = row.Attempt.Code;
            TestResultsDataGrid.ItemsSource = row.Attempt.TestResults
                .Select(result => new SubmissionTestResultRow(result))
                .ToList();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string FormatDateTime(DateTimeOffset value)
        {
            return value == default
                ? "-"
                : value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string FormatMemoryBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "-";
            }

            return $"{bytes / 1024d / 1024d:0.#} MB";
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

        private sealed class ProblemSummaryRow
        {
            public ProblemSummaryRow(SubmissionHistoryInspectionProblem problem)
            {
                Problem = problem;
                SubmissionRows = problem.Attempts
                    .Select(item => new SubmissionAttemptRow(item))
                    .ToList();
            }

            public SubmissionHistoryInspectionProblem Problem { get; }
            public List<SubmissionAttemptRow> SubmissionRows { get; }
            public string ProblemName => string.IsNullOrWhiteSpace(Problem.Problem.Id)
                ? Problem.Problem.Title
                : $"[{Problem.Problem.Id}] {Problem.Problem.Title}";
            public string ProblemId => string.IsNullOrWhiteSpace(Problem.Problem.Id) ? "-" : Problem.Problem.Id;
            public string Title => Problem.Problem.Title;
            public string TimeLimitText => Problem.IdealTimeLimitMs > 0 ? $"{Problem.IdealTimeLimitMs} ms" : "-";
            public string MemoryLimitText => Problem.IdealMemoryLimitMb > 0 ? $"{Problem.IdealMemoryLimitMb} MB" : "-";
            public int AttemptCount => SubmissionRows.Count;
            public int Score => Problem.Score;
            public SubmissionAttemptRow? FirstAccepted => SubmissionRows.FirstOrDefault(row => row.IsAccepted);
            public string FirstAcceptedAttemptText
            {
                get
                {
                    SubmissionAttemptRow? firstAccepted = FirstAccepted;
                    if (firstAccepted is null)
                    {
                        return "-";
                    }

                    int index = SubmissionRows.IndexOf(firstAccepted);
                    return (index + 1).ToString();
                }
            }
            public string BestVerdict => FirstAccepted is not null
                ? "AC"
                : SubmissionRows.LastOrDefault()?.Verdict ?? "-";
            public string MaxElapsedText => SubmissionRows.Count == 0
                ? "-"
                : $"{SubmissionRows.Max(row => row.MaxElapsedMs):0} ms";
            public string MaxMemoryText => SubmissionRows.Count == 0
                ? "-"
                : FormatMemoryBytes(SubmissionRows.Max(row => row.MaxMemoryBytes));
        }

        private sealed class SubmissionAttemptRow
        {
            public SubmissionAttemptRow(SubmissionAttemptHistoryItem item)
            {
                FilePath = item.FilePath;
                Attempt = item.Attempt;
                if (string.IsNullOrWhiteSpace(Attempt.Language))
                {
                    Attempt.Language = "Python";
                }
            }

            public string FilePath { get; }
            public SubmissionAttemptDocument Attempt { get; }
            public bool IsAccepted => string.Equals(Attempt.Verdict, "AC", StringComparison.OrdinalIgnoreCase);
            public bool IsWrongBeforeAccepted => !IsAccepted;
            public string SubmittedAtText => FormatDateTime(Attempt.SubmittedAt);
            public string Language => string.IsNullOrWhiteSpace(Attempt.Language) ? "Python" : Attempt.Language;
            public string Verdict => Attempt.Verdict;
            public string PassedText => $"{Attempt.PassedCount}/{Attempt.TotalCount}";
            public double MaxElapsedMs => Attempt.TestResults.Count == 0
                ? 0
                : Attempt.TestResults.Max(result => result.ElapsedMs);
            public long MaxMemoryBytes => Attempt.TestResults.Count == 0
                ? 0
                : Attempt.TestResults.Max(result => result.PeakMemoryBytes);
            public string MaxElapsedText => MaxElapsedMs <= 0 ? "-" : $"{MaxElapsedMs:0} ms";
            public string MaxMemoryText => FormatMemoryBytes(MaxMemoryBytes);
            public string AppliedLimitsText => $"{Attempt.Limits.AppliedTimeLimitMs} ms / {Attempt.Limits.AppliedMemoryLimitMb} MB";
        }

        private sealed class ContestProblemRow
        {
            public ContestProblemRow(ProblemSummaryRow problem, SubmissionHistoryContestSettings settings)
            {
                ProblemName = problem.ProblemName;
                AttemptCount = problem.AttemptCount;

                SubmissionAttemptRow? firstAccepted = problem.FirstAccepted;
                if (firstAccepted is null)
                {
                    FirstAcceptedAttemptText = "-";
                    PenaltyText = "-";
                    Score = 0;
                    return;
                }

                Solved = true;
                int acceptedIndex = problem.SubmissionRows.IndexOf(firstAccepted);
                int wrongBeforeAccepted = settings.CountWrongBeforeAcceptedOnly
                    ? problem.SubmissionRows
                        .Take(acceptedIndex)
                        .Count(row => !row.IsAccepted)
                    : problem.SubmissionRows.Count(row => !row.IsAccepted);
                int elapsedMinutes = settings.ContestStartedAt is null
                    ? 0
                    : Math.Max(0, (int)Math.Floor((firstAccepted.Attempt.SubmittedAt - settings.ContestStartedAt.Value).TotalMinutes));

                FirstAcceptedAttemptText = (acceptedIndex + 1).ToString();
                PenaltyMinutes = elapsedMinutes + wrongBeforeAccepted * settings.WrongSubmissionPenaltyMinutes;
                PenaltyText = $"{PenaltyMinutes}분";
                Score = problem.Score;
            }

            public string ProblemName { get; }
            public int AttemptCount { get; }
            public bool Solved { get; }
            public string FirstAcceptedAttemptText { get; } = "-";
            public int PenaltyMinutes { get; }
            public string PenaltyText { get; } = "-";
            public int Score { get; }
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
