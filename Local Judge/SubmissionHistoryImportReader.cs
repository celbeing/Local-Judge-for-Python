using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Local_Judge
{
    public sealed class SubmissionHistoryImportReader
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public SubmissionHistoryImportReader(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        public SubmissionHistoryInspectionDocument ReadZip(string filePath)
        {
            using ZipArchive archive = ZipFile.OpenRead(filePath);
            ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json")
                ?? throw new InvalidOperationException("manifest.json을 찾을 수 없습니다.");

            SubmissionHistoryExportManifest manifest = ReadJsonEntry<SubmissionHistoryExportManifest>(manifestEntry)
                ?? throw new InvalidOperationException("manifest.json을 읽었지만 내용이 비어 있습니다.");

            var problems = new List<SubmissionHistoryInspectionProblem>();
            int skippedFileCount = 0;

            foreach (SubmissionHistoryExportProblemManifest problemManifest in manifest.Problems)
            {
                var attempts = new List<SubmissionAttemptHistoryItem>();

                foreach (string entryName in problemManifest.Files)
                {
                    ZipArchiveEntry? attemptEntry = archive.GetEntry(entryName);
                    if (attemptEntry is null)
                    {
                        skippedFileCount++;
                        continue;
                    }

                    try
                    {
                        SubmissionAttemptDocument? attempt = ReadJsonEntry<SubmissionAttemptDocument>(attemptEntry);
                        if (attempt is null)
                        {
                            skippedFileCount++;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(attempt.Language))
                        {
                            attempt.Language = "Python";
                        }

                        attempts.Add(new SubmissionAttemptHistoryItem(entryName, attempt));
                    }
                    catch
                    {
                        skippedFileCount++;
                    }
                }

                attempts = attempts
                    .OrderBy(item => item.Attempt.SubmittedAt)
                    .ToList();

                SubmissionAttemptDocument? firstAttempt = attempts.FirstOrDefault()?.Attempt;
                int idealTimeLimitMs = problemManifest.IdealTimeLimitMs > 0
                    ? problemManifest.IdealTimeLimitMs
                    : firstAttempt?.Limits.IdealTimeLimitMs ?? 0;
                int idealMemoryLimitMb = problemManifest.IdealMemoryLimitMb > 0
                    ? problemManifest.IdealMemoryLimitMb
                    : firstAttempt?.Limits.IdealMemoryLimitMb ?? 0;

                problems.Add(new SubmissionHistoryInspectionProblem
                {
                    ProblemKey = problemManifest.ProblemKey,
                    DisplayName = problemManifest.DisplayName,
                    Problem = problemManifest.Problem,
                    ProblemFilePath = problemManifest.ProblemFilePath,
                    IdealTimeLimitMs = idealTimeLimitMs,
                    IdealMemoryLimitMb = idealMemoryLimitMb,
                    Score = problemManifest.Score <= 0 ? 1 : problemManifest.Score,
                    Attempts = attempts
                });
            }

            return new SubmissionHistoryInspectionDocument
            {
                FilePath = filePath,
                Manifest = manifest,
                Problems = problems,
                SkippedFileCount = skippedFileCount
            };
        }

        private T? ReadJsonEntry<T>(ZipArchiveEntry entry)
        {
            using Stream stream = entry.Open();
            return JsonSerializer.Deserialize<T>(stream, _jsonOptions);
        }
    }

    public sealed class SubmissionHistoryInspectionDocument
    {
        public string FilePath { get; set; } = string.Empty;
        public SubmissionHistoryExportManifest Manifest { get; set; } = new();
        public List<SubmissionHistoryInspectionProblem> Problems { get; set; } = new();
        public int SkippedFileCount { get; set; }
    }

    public sealed class SubmissionHistoryInspectionProblem
    {
        public string ProblemKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public SubmissionProblemDocument Problem { get; set; } = new();
        public string ProblemFilePath { get; set; } = string.Empty;
        public int IdealTimeLimitMs { get; set; }
        public int IdealMemoryLimitMb { get; set; }
        public int Score { get; set; } = 1;
        public List<SubmissionAttemptHistoryItem> Attempts { get; set; } = new();
    }
}
