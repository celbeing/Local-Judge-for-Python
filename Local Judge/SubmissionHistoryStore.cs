using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Local_Judge
{
    public sealed class SubmissionHistoryStore
    {
        public const int MaxCapturedOutputBytes = 16 * 1024;

        private readonly JsonSerializerOptions _jsonOptions;

        public SubmissionHistoryStore(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        public string SaveAttempt(SubmissionAttemptDocument attempt)
        {
            string problemDirectory = GetProblemDirectory(attempt.Problem);

            Directory.CreateDirectory(problemDirectory);

            string attemptId = string.IsNullOrWhiteSpace(attempt.AttemptId)
                ? CreateAttemptId(attempt.SubmittedAt)
                : SanitizePathPart(attempt.AttemptId);
            string filePath = Path.Combine(problemDirectory, attemptId + ".json");

            string json = JsonSerializer.Serialize(attempt, _jsonOptions);
            File.WriteAllText(filePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            return filePath;
        }

        public IReadOnlyList<SubmissionAttemptHistoryItem> LoadAttemptsForProblem(SubmissionProblemDocument problem)
        {
            string problemDirectory = GetProblemDirectory(problem);
            if (!Directory.Exists(problemDirectory))
            {
                return Array.Empty<SubmissionAttemptHistoryItem>();
            }

            var attempts = new List<SubmissionAttemptHistoryItem>();
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

                    attempts.Add(new SubmissionAttemptHistoryItem(filePath, attempt));
                }
                catch
                {
                    // Ignore malformed history files. They should not block viewing valid attempts.
                }
            }

            return attempts
                .OrderByDescending(item => item.Attempt.SubmittedAt)
                .ToList();
        }

        public string GetProblemDirectory(SubmissionProblemDocument problem)
        {
            string problemKey = CreateProblemKey(problem);
            return Path.Combine(GetSubmissionsRoot(), problemKey);
        }

        public static string GetProblemKey(SubmissionProblemDocument problem)
        {
            return CreateProblemKey(problem);
        }

        public static string GetSubmissionsRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Local Judge",
                "Submissions");
        }

        public static string CreateAttemptId(DateTimeOffset submittedAt)
        {
            return submittedAt.LocalDateTime.ToString("yyyyMMdd_HHmmss_fff");
        }

        public static string TruncateCapturedOutput(string text, out bool truncated)
        {
            if (string.IsNullOrEmpty(text))
            {
                truncated = false;
                return string.Empty;
            }

            int byteCount = Encoding.UTF8.GetByteCount(text);
            if (byteCount <= MaxCapturedOutputBytes)
            {
                truncated = false;
                return text;
            }

            var builder = new StringBuilder();
            int capturedBytes = 0;

            foreach (Rune rune in text.EnumerateRunes())
            {
                int runeByteCount = rune.Utf8SequenceLength;
                if (capturedBytes + runeByteCount > MaxCapturedOutputBytes)
                {
                    break;
                }

                builder.Append(rune.ToString());
                capturedBytes += runeByteCount;
            }

            truncated = true;
            return builder.ToString();
        }

        private static string CreateProblemKey(SubmissionProblemDocument problem)
        {
            string displayName = string.IsNullOrWhiteSpace(problem.Id)
                ? problem.Title
                : $"{problem.Id}_{problem.Title}";

            displayName = SanitizePathPart(displayName);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = "problem";
            }

            if (displayName.Length > 80)
            {
                displayName = displayName[..80].TrimEnd('_', '.', ' ');
            }

            string hashSource = string.Join(
                "\n",
                problem.Id ?? string.Empty,
                problem.Title ?? string.Empty,
                problem.AuthorName ?? string.Empty,
                problem.Source ?? string.Empty);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashSource)))[..8].ToLowerInvariant();

            return $"{displayName}_{hash}";
        }

        private static string SanitizePathPart(string text)
        {
            string sanitized = Regex.Replace(text ?? string.Empty, @"[\\/:*?""<>|]+", "_");
            sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
            sanitized = sanitized.TrimEnd('.', ' ');
            return sanitized;
        }
    }

    public sealed record SubmissionAttemptHistoryItem(
        string FilePath,
        SubmissionAttemptDocument Attempt);

    public sealed class SubmissionAttemptDocument
    {
        public int Version { get; set; } = 1;
        public string AttemptId { get; set; } = string.Empty;
        public DateTimeOffset SubmittedAt { get; set; }
        public SubmissionProblemDocument Problem { get; set; } = new();
        public string ProblemFilePath { get; set; } = string.Empty;
        public string Verdict { get; set; } = "JudgingError";
        public int PassedCount { get; set; }
        public int TotalCount { get; set; }
        public string Code { get; set; } = string.Empty;
        public SubmissionLimitDocument Limits { get; set; } = new();
        public SubmissionBenchmarkDocument Benchmark { get; set; } = new();
        public List<SubmissionTestResultDocument> TestResults { get; set; } = new();
    }

    public sealed class SubmissionProblemDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    public sealed class SubmissionLimitDocument
    {
        public int IdealTimeLimitMs { get; set; }
        public int IdealMemoryLimitMb { get; set; }
        public int AppliedTimeLimitMs { get; set; }
        public int AppliedMemoryLimitMb { get; set; }
        public int OutputLimitBytes { get; set; }
    }

    public sealed class SubmissionBenchmarkDocument
    {
        public bool IsFallback { get; set; }
        public double TimeMultiplier { get; set; }
        public int ExtraTimeMs { get; set; }
        public int ExtraMemoryMb { get; set; }
    }

    public sealed class SubmissionTestResultDocument
    {
        public int TestNumber { get; set; }
        public string Verdict { get; set; } = "JudgingError";
        public int ExitCode { get; set; }
        public double ElapsedMs { get; set; }
        public long PeakMemoryBytes { get; set; }
        public string StandardOutput { get; set; } = string.Empty;
        public bool StandardOutputTruncated { get; set; }
        public string StandardError { get; set; } = string.Empty;
        public bool StandardErrorTruncated { get; set; }
    }
}
