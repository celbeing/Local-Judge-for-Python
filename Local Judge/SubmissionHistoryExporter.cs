using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Local_Judge
{
    public sealed class SubmissionHistoryExporter
    {
        private readonly SubmissionHistoryStore _historyStore;
        private readonly JsonSerializerOptions _jsonOptions;

        public SubmissionHistoryExporter(
            SubmissionHistoryStore historyStore,
            JsonSerializerOptions jsonOptions)
        {
            _historyStore = historyStore;
            _jsonOptions = jsonOptions;
        }

        public SubmissionHistoryExportResult Export(SubmissionHistoryExportRequest request)
        {
            string? directoryPath = Path.GetDirectoryName(request.DestinationFilePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var problemGroups = request.Problems
                .GroupBy(problem => SubmissionHistoryStore.GetProblemKey(problem.Problem))
                .Select(group => group.First())
                .ToList();

            var manifestProblems = new List<SubmissionHistoryExportProblemManifest>();
            int totalAttemptCount = 0;

            using FileStream stream = File.Create(request.DestinationFilePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

            foreach (SubmissionHistoryExportProblem problem in problemGroups)
            {
                string problemKey = SubmissionHistoryStore.GetProblemKey(problem.Problem);
                IReadOnlyList<SubmissionAttemptHistoryItem> attempts = _historyStore.LoadAttemptsForProblem(problem.Problem);
                var files = new List<string>();
                SubmissionAttemptDocument? firstAttempt = attempts
                    .OrderBy(attempt => attempt.Attempt.SubmittedAt)
                    .Select(attempt => attempt.Attempt)
                    .FirstOrDefault();

                foreach (SubmissionAttemptHistoryItem attempt in attempts)
                {
                    string entryName = $"submissions/{problemKey}/{Path.GetFileName(attempt.FilePath)}";
                    AddFileEntry(archive, entryName, attempt.FilePath);
                    files.Add(entryName);
                }

                totalAttemptCount += attempts.Count;
                manifestProblems.Add(new SubmissionHistoryExportProblemManifest
                {
                    ProblemKey = problemKey,
                    DisplayName = problem.DisplayName,
                    Problem = problem.Problem,
                    ProblemFilePath = problem.ProblemFilePath,
                    IdealTimeLimitMs = firstAttempt?.Limits.IdealTimeLimitMs ?? 0,
                    IdealMemoryLimitMb = firstAttempt?.Limits.IdealMemoryLimitMb ?? 0,
                    Score = problem.Score,
                    AttemptCount = attempts.Count,
                    Files = files
                });
            }

            var manifest = new SubmissionHistoryExportManifest
            {
                Version = 1,
                ExportedAt = DateTimeOffset.Now,
                ExportKind = request.ExportKind,
                ExportName = request.ExportName,
                ContestSettings = request.ContestSettings,
                ProblemCount = manifestProblems.Count,
                AttemptCount = totalAttemptCount,
                Problems = manifestProblems
            };

            AddTextEntry(
                archive,
                "manifest.json",
                JsonSerializer.Serialize(manifest, _jsonOptions));

            return new SubmissionHistoryExportResult(
                request.DestinationFilePath,
                manifestProblems.Count,
                totalAttemptCount);
        }

        private static void AddFileEntry(ZipArchive archive, string entryName, string sourceFilePath)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using Stream entryStream = entry.Open();
            using FileStream sourceStream = File.OpenRead(sourceFilePath);
            sourceStream.CopyTo(entryStream);
        }

        private static void AddTextEntry(ZipArchive archive, string entryName, string text)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using Stream entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(text);
        }
    }

    public sealed class SubmissionHistoryExportRequest
    {
        public string DestinationFilePath { get; set; } = string.Empty;
        public string ExportKind { get; set; } = "Problem";
        public string ExportName { get; set; } = string.Empty;
        public SubmissionHistoryContestSettings? ContestSettings { get; set; }
        public List<SubmissionHistoryExportProblem> Problems { get; set; } = new();
    }

    public sealed class SubmissionHistoryExportProblem
    {
        public string DisplayName { get; set; } = string.Empty;
        public SubmissionProblemDocument Problem { get; set; } = new();
        public string ProblemFilePath { get; set; } = string.Empty;
        public int Score { get; set; } = 1;
    }

    public sealed record SubmissionHistoryExportResult(
        string FilePath,
        int ProblemCount,
        int AttemptCount);

    public sealed class SubmissionHistoryExportManifest
    {
        public int Version { get; set; }
        public DateTimeOffset ExportedAt { get; set; }
        public string ExportKind { get; set; } = string.Empty;
        public string ExportName { get; set; } = string.Empty;
        public SubmissionHistoryContestSettings? ContestSettings { get; set; }
        public int ProblemCount { get; set; }
        public int AttemptCount { get; set; }
        public List<SubmissionHistoryExportProblemManifest> Problems { get; set; } = new();
    }

    public sealed class SubmissionHistoryExportProblemManifest
    {
        public string ProblemKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public SubmissionProblemDocument Problem { get; set; } = new();
        public string ProblemFilePath { get; set; } = string.Empty;
        public int IdealTimeLimitMs { get; set; }
        public int IdealMemoryLimitMb { get; set; }
        public int Score { get; set; } = 1;
        public int AttemptCount { get; set; }
        public List<string> Files { get; set; } = new();
    }

    public sealed class SubmissionHistoryContestSettings
    {
        public string PenaltyMode { get; set; } = "ICPC";
        public int WrongSubmissionPenaltyMinutes { get; set; } = 20;
        public bool CountWrongBeforeAcceptedOnly { get; set; } = true;
        public DateTimeOffset? ContestStartedAt { get; set; }
    }
}
