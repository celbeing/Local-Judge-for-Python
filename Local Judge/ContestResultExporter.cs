using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Local_Judge
{
    public sealed class ContestResultExporter
    {
        private readonly SubmissionHistoryExporter _historyExporter;
        private readonly ContestProblemNavigator _problemNavigator;

        public ContestResultExporter(
            SubmissionHistoryExporter historyExporter,
            ContestProblemNavigator problemNavigator)
        {
            _historyExporter = historyExporter;
            _problemNavigator = problemNavigator;
        }

        public SubmissionHistoryExportResult Export(ContestContext contest, string destinationFilePath)
        {
            var request = new SubmissionHistoryExportRequest
            {
                DestinationFilePath = destinationFilePath,
                ExportKind = "Contest",
                ExportName = contest.Title,
                ContestSettings = new SubmissionHistoryContestSettings
                {
                    PenaltyMode = "ICPC",
                    WrongSubmissionPenaltyMinutes = contest.WrongSubmissionPenaltyMinutes,
                    CountWrongBeforeAcceptedOnly = true,
                    ContestStartedAt = contest.StartsAt
                }
            };

            foreach (ContestProblemItem problem in contest.Problems)
            {
                request.Problems.Add(new SubmissionHistoryExportProblem
                {
                    ProblemKey = problem.SubmissionKey,
                    DisplayName = ContestProblemNavigator.FormatProblemName(problem),
                    Problem = problem.SubmissionProblem,
                    ProblemFilePath = problem.FilePath,
                    Score = problem.Score,
                    Attempts = _problemNavigator.LoadAttempts(contest, problem)
                });
            }

            return _historyExporter.Export(request);
        }

        public string GetAutoExportDirectory(ContestContext contest, string? configuredExportDirectory)
        {
            if (!string.IsNullOrWhiteSpace(configuredExportDirectory))
            {
                Directory.CreateDirectory(configuredExportDirectory);
                return configuredExportDirectory;
            }

            return Directory.GetParent(contest.RootPath)?.FullName ?? contest.RootPath;
        }

        public static string CreateDefaultExportFileName(ContestContext contest)
        {
            string baseName = Regex.Replace(contest.Title, @"[\\/:*?""<>|]+", "_").Trim();
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "contest";
            }

            return $"{baseName}_result_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        }
    }
}
