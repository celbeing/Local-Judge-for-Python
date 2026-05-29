using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Local_Judge
{
    public sealed class LessonResultInspectionReader
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public LessonResultInspectionReader(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        public SubmissionHistoryInspectionDocument ReadZip(string filePath)
        {
            ZipArchive archive;
            try
            {
                archive = ZipFile.OpenRead(filePath);
            }
            catch
            {
                throw new InvalidLessonResultFileException();
            }

            using (archive)
            {
                string rootPrefix = DetectSingleRootPrefix(archive);
                List<LessonResultProblemRecord> problems = ReadProblems(archive, rootPrefix);

                if (problems.Count == 0)
                {
                    throw new InvalidLessonResultFileException();
                }

                List<ZipArchiveEntry> submissionEntries = archive.Entries
                    .Where(entry => IsSubmissionEntry(entry, rootPrefix))
                    .ToList();

                if (submissionEntries.Count == 0)
                {
                    throw new LessonResultNoSubmissionsException();
                }

                Dictionary<string, LessonResultProblemRecord> byRelativePath = problems
                    .GroupBy(problem => NormalizeArchivePath(problem.RelativePath), StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() == 1)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                Dictionary<string, List<LessonResultProblemRecord>> byProblemKey = problems
                    .GroupBy(problem => problem.ProblemKey, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

                int skippedFileCount = 0;
                foreach (ZipArchiveEntry entry in submissionEntries)
                {
                    try
                    {
                        SubmissionAttemptDocument? attempt = ReadJsonEntry<SubmissionAttemptDocument>(entry);
                        if (attempt is null || !LooksLikeSubmissionAttempt(attempt))
                        {
                            skippedFileCount++;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(attempt.Language))
                        {
                            attempt.Language = "Python";
                        }

                        LessonResultProblemRecord? problem = FindProblemForAttempt(
                            attempt,
                            byRelativePath,
                            byProblemKey);
                        if (problem is null)
                        {
                            skippedFileCount++;
                            continue;
                        }

                        string entryPath = NormalizeArchivePath(RemoveRootPrefix(entry.FullName, rootPrefix));
                        problem.Attempts.Add(new SubmissionAttemptHistoryItem(entryPath, attempt));
                    }
                    catch
                    {
                        skippedFileCount++;
                    }
                }

                int matchedAttemptCount = problems.Sum(problem => problem.Attempts.Count);
                if (matchedAttemptCount == 0)
                {
                    throw new LessonResultNoSubmissionsException();
                }

                string exportName = Path.GetFileNameWithoutExtension(filePath);
                return new SubmissionHistoryInspectionDocument
                {
                    FilePath = filePath,
                    Manifest = new SubmissionHistoryExportManifest
                    {
                        Version = 1,
                        ExportedAt = DateTimeOffset.Now,
                        ExportKind = "LessonResult",
                        ExportName = $"수업 결과: {exportName}",
                        ProblemCount = problems.Count,
                        AttemptCount = matchedAttemptCount
                    },
                    Problems = problems
                        .OrderBy(problem => problem.RelativePath, StringComparer.OrdinalIgnoreCase)
                        .Select(problem => new SubmissionHistoryInspectionProblem
                        {
                            ProblemKey = problem.ProblemKey,
                            DisplayName = problem.DisplayName,
                            Problem = problem.SubmissionProblem,
                            ProblemFilePath = problem.RelativePath,
                            IdealTimeLimitMs = problem.ProblemDocument.TimeLimitMs,
                            IdealMemoryLimitMb = problem.ProblemDocument.MemoryLimitMb,
                            Score = 1,
                            Attempts = problem.Attempts
                                .OrderBy(item => item.Attempt.SubmittedAt)
                                .ToList()
                        })
                        .ToList(),
                    SkippedFileCount = skippedFileCount
                };
            }
        }

        private List<LessonResultProblemRecord> ReadProblems(ZipArchive archive, string rootPrefix)
        {
            var problems = new List<LessonResultProblemRecord>();

            foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => IsProblemEntry(entry, rootPrefix)))
            {
                try
                {
                    string json = ReadEntryText(entry);
                    if (!LooksLikeProblemJson(json))
                    {
                        continue;
                    }

                    ProblemDocument? problem = JsonSerializer.Deserialize<ProblemDocument>(json, _jsonOptions);
                    if (problem is null || string.IsNullOrWhiteSpace(problem.Title))
                    {
                        continue;
                    }

                    problem.AuthorName ??= string.Empty;
                    problem.Source ??= string.Empty;
                    problem.TimeLimitMs = problem.TimeLimitMs <= 0 ? 2000 : problem.TimeLimitMs;
                    problem.MemoryLimitMb = problem.MemoryLimitMb <= 0 ? 128 : problem.MemoryLimitMb;

                    string relativePath = NormalizeArchivePath(RemoveRootPrefix(entry.FullName, rootPrefix));
                    SubmissionProblemDocument submissionProblem = CreateSubmissionProblem(problem);
                    string problemKey = SubmissionHistoryStore.GetProblemKey(submissionProblem);
                    string sectionTitle = GetSectionTitle(relativePath);

                    problems.Add(new LessonResultProblemRecord
                    {
                        RelativePath = relativePath,
                        SectionTitle = sectionTitle,
                        ProblemDocument = problem,
                        SubmissionProblem = submissionProblem,
                        ProblemKey = problemKey,
                        DisplayName = string.IsNullOrWhiteSpace(sectionTitle)
                            ? FormatProblemName(submissionProblem)
                            : $"{sectionTitle} / {FormatProblemName(submissionProblem)}"
                    });
                }
                catch
                {
                    // Malformed problem candidates are ignored. If none are valid, the ZIP is invalid.
                }
            }

            return problems;
        }

        private static bool IsProblemEntry(ZipArchiveEntry entry, string rootPrefix)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)
                || !string.Equals(Path.GetExtension(entry.Name), ".json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string path = NormalizeArchivePath(RemoveRootPrefix(entry.FullName, rootPrefix));
            if (path.StartsWith(".localjudge/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fileName = Path.GetFileName(path);
            return !string.Equals(fileName, "lesson.json", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSubmissionEntry(ZipArchiveEntry entry, string rootPrefix)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)
                || !string.Equals(Path.GetExtension(entry.Name), ".json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string path = NormalizeArchivePath(RemoveRootPrefix(entry.FullName, rootPrefix));
            return path.StartsWith(".localjudge/submissions/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeProblemJson(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && (root.TryGetProperty("title", out _)
                       || root.TryGetProperty("description", out _)
                       || root.TryGetProperty("samples", out _)
                       || root.TryGetProperty("testCases", out _));
        }

        private static bool LooksLikeSubmissionAttempt(SubmissionAttemptDocument attempt)
        {
            return attempt.Problem is not null
                   && (!string.IsNullOrWhiteSpace(attempt.Problem.Id)
                       || !string.IsNullOrWhiteSpace(attempt.Problem.Title))
                   && (!string.IsNullOrWhiteSpace(attempt.Verdict)
                       || attempt.TestResults.Count > 0
                       || !string.IsNullOrWhiteSpace(attempt.Code));
        }

        private static LessonResultProblemRecord? FindProblemForAttempt(
            SubmissionAttemptDocument attempt,
            Dictionary<string, LessonResultProblemRecord> byRelativePath,
            Dictionary<string, List<LessonResultProblemRecord>> byProblemKey)
        {
            foreach (string candidatePath in GetAttemptProblemPathCandidates(attempt))
            {
                if (byRelativePath.TryGetValue(candidatePath, out LessonResultProblemRecord? exactMatch))
                {
                    return exactMatch;
                }

                List<LessonResultProblemRecord> suffixMatches = byRelativePath
                    .Where(pair => pair.Key.EndsWith("/" + candidatePath, StringComparison.OrdinalIgnoreCase)
                                   || pair.Key.EndsWith(candidatePath, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Value)
                    .Distinct()
                    .ToList();
                if (suffixMatches.Count == 1)
                {
                    return suffixMatches[0];
                }
            }

            string problemKey = SubmissionHistoryStore.GetProblemKey(attempt.Problem);
            if (byProblemKey.TryGetValue(problemKey, out List<LessonResultProblemRecord>? keyMatches)
                && keyMatches.Count == 1)
            {
                return keyMatches[0];
            }

            return null;
        }

        private static IEnumerable<string> GetAttemptProblemPathCandidates(SubmissionAttemptDocument attempt)
        {
            foreach (string rawPath in new[] { attempt.ProblemRelativePath, attempt.ProblemFilePath })
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                string normalized = NormalizeArchivePath(rawPath);
                if (!Path.IsPathRooted(rawPath))
                {
                    yield return normalized;
                }

                string fileName = Path.GetFileName(normalized);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    yield return fileName;
                }
            }
        }

        private T? ReadJsonEntry<T>(ZipArchiveEntry entry)
        {
            string json = ReadEntryText(entry);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }

        private static string ReadEntryText(ZipArchiveEntry entry)
        {
            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static SubmissionProblemDocument CreateSubmissionProblem(ProblemDocument problem)
        {
            return new SubmissionProblemDocument
            {
                Id = problem.Id ?? string.Empty,
                Title = problem.Title ?? string.Empty,
                AuthorName = problem.AuthorName ?? string.Empty,
                Source = problem.Source ?? string.Empty
            };
        }

        private static string FormatProblemName(SubmissionProblemDocument problem)
        {
            return string.IsNullOrWhiteSpace(problem.Id)
                ? problem.Title
                : $"[{problem.Id}] {problem.Title}";
        }

        private static string GetSectionTitle(string relativePath)
        {
            string[] parts = NormalizeArchivePath(relativePath)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length <= 1 ? string.Empty : parts[0];
        }

        private static string DetectSingleRootPrefix(ZipArchive archive)
        {
            List<string> paths = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => NormalizeArchivePath(entry.FullName))
                .Where(path => !IsIgnoredZipMetadataPath(path))
                .Where(path => path.Contains('/'))
                .ToList();

            if (paths.Count == 0)
            {
                return string.Empty;
            }

            string firstSegment = paths[0].Split('/')[0];
            bool allShareRoot = archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => NormalizeArchivePath(entry.FullName))
                .Where(path => !IsIgnoredZipMetadataPath(path))
                .All(path => path.StartsWith(firstSegment + "/", StringComparison.OrdinalIgnoreCase));

            return allShareRoot ? firstSegment + "/" : string.Empty;
        }

        private static string RemoveRootPrefix(string path, string rootPrefix)
        {
            string normalized = NormalizeArchivePath(path);
            return !string.IsNullOrEmpty(rootPrefix)
                   && normalized.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                ? normalized[rootPrefix.Length..]
                : normalized;
        }

        private static string NormalizeArchivePath(string path)
        {
            return (path ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('/');
        }

        private static bool IsIgnoredZipMetadataPath(string path)
        {
            return path.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(Path.GetFileName(path), ".DS_Store", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class LessonResultProblemRecord
        {
            public string RelativePath { get; set; } = string.Empty;
            public string SectionTitle { get; set; } = string.Empty;
            public ProblemDocument ProblemDocument { get; set; } = new();
            public SubmissionProblemDocument SubmissionProblem { get; set; } = new();
            public string ProblemKey { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public List<SubmissionAttemptHistoryItem> Attempts { get; } = new();
        }
    }

    public sealed class InvalidLessonResultFileException : Exception
    {
        public InvalidLessonResultFileException()
            : base("잘못된 파일입니다.")
        {
        }
    }

    public sealed class LessonResultNoSubmissionsException : Exception
    {
        public LessonResultNoSubmissionsException()
            : base("제출 기록이 없습니다.")
        {
        }
    }
}
