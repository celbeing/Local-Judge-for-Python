using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Local_Judge
{
    public sealed class LessonPackageReader
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public LessonPackageReader(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        public LessonContext OpenZip(string zipFilePath)
        {
            string lessonRootBase = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Local Judge",
                "Lessons");
            Directory.CreateDirectory(lessonRootBase);

            string lessonFolderName = CreateLessonFolderName(zipFilePath);
            string extractRootPath = Path.Combine(lessonRootBase, lessonFolderName);

            if (Directory.Exists(extractRootPath))
            {
                extractRootPath = Path.Combine(lessonRootBase, lessonFolderName + "_" + Guid.NewGuid().ToString("N")[..8]);
            }

            Directory.CreateDirectory(extractRootPath);

            try
            {
                ZipFile.ExtractToDirectory(zipFilePath, extractRootPath);
            }
            catch
            {
                throw new InvalidOperationException("수업 ZIP 파일을 열 수 없습니다.");
            }

            string lessonRootPath = ResolveLessonRootPath(extractRootPath);
            return ReadFolder(lessonRootPath, zipFilePath);
        }

        public LessonContext ReadFolder(string lessonRootPath, string? sourceZipPath = null)
        {
            if (!Directory.Exists(lessonRootPath))
            {
                throw new InvalidOperationException("수업 폴더를 찾을 수 없습니다.");
            }

            List<LessonProblemItem> problems = ReadProblems(lessonRootPath);
            if (problems.Count == 0)
            {
                throw new InvalidOperationException("수업에서 문제 JSON 파일을 찾지 못했습니다.");
            }

            string lessonTitle = Path.GetFileName(lessonRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string lessonId = CreateStableHash(lessonRootPath);
            string submissionsRoot = Path.Combine(lessonRootPath, ".localjudge", "submissions");

            List<LessonSection> sections = problems
                .GroupBy(problem => problem.SectionTitle, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new LessonSection
                {
                    Title = group.Key,
                    Problems = group
                        .OrderBy(problem => problem.RelativePath, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList();

            return new LessonContext
            {
                LessonId = lessonId,
                Title = string.IsNullOrWhiteSpace(lessonTitle) ? "수업" : lessonTitle,
                RootPath = lessonRootPath,
                SourceZipPath = sourceZipPath ?? string.Empty,
                SubmissionsRoot = submissionsRoot,
                Sections = sections
            };
        }

        private List<LessonProblemItem> ReadProblems(string lessonRootPath)
        {
            var problems = new List<LessonProblemItem>();

            foreach (string filePath in Directory.EnumerateFiles(lessonRootPath, "*.json", SearchOption.AllDirectories))
            {
                string relativePath = NormalizeRelativePath(Path.GetRelativePath(lessonRootPath, filePath));
                if (ShouldSkipJsonFile(relativePath))
                {
                    continue;
                }

                try
                {
                    string json = File.ReadAllText(filePath);
                    if (!LooksLikeProblemJson(json))
                    {
                        continue;
                    }

                    ProblemDocument? problem = JsonSerializer.Deserialize<ProblemDocument>(json, _jsonOptions);
                    if (problem is null || string.IsNullOrWhiteSpace(problem.Title))
                    {
                        continue;
                    }

                    NormalizeProblem(problem);
                    SubmissionProblemDocument submissionProblem = CreateSubmissionProblem(problem);
                    string sectionTitle = GetSectionTitle(relativePath);

                    problems.Add(new LessonProblemItem
                    {
                        SectionTitle = sectionTitle,
                        RelativePath = relativePath,
                        FilePath = filePath,
                        Problem = problem,
                        SubmissionProblem = submissionProblem,
                        SubmissionKey = CreateLessonProblemKey(submissionProblem, relativePath)
                    });
                }
                catch
                {
                    // Ignore malformed JSON candidates. If none are valid, the caller reports an invalid lesson.
                }
            }

            return problems;
        }

        private static bool ShouldSkipJsonFile(string relativePath)
        {
            string normalized = NormalizeRelativePath(relativePath);
            string fileName = Path.GetFileName(normalized);
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

            return segments.Any(segment => string.Equals(segment, ".localjudge", StringComparison.OrdinalIgnoreCase))
                   || segments.Any(segment => segment.EndsWith(".assets", StringComparison.OrdinalIgnoreCase))
                   || string.Equals(fileName, "lesson.json", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase);
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

        private static void NormalizeProblem(ProblemDocument problem)
        {
            problem.Samples ??= new();
            problem.TestCases ??= new();
            problem.Assets ??= new();
            problem.AuthorName ??= string.Empty;
            problem.Source ??= string.Empty;
            problem.Description ??= string.Empty;
            problem.InputFormat ??= string.Empty;
            problem.OutputFormat ??= string.Empty;
            problem.StatementFormat = ProblemAssetUtilities.NormalizeStatementFormat(
                problem.StatementFormat,
                problem.Version >= ProblemAssetUtilities.CurrentProblemVersion);
            problem.Version = problem.Version <= 0 ? 3 : Math.Max(problem.Version, 3);
            problem.TimeLimitMs = problem.TimeLimitMs <= 0 ? 2000 : problem.TimeLimitMs;
            problem.MemoryLimitMb = problem.MemoryLimitMb <= 0 ? 128 : problem.MemoryLimitMb;
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

        private static string GetSectionTitle(string relativePath)
        {
            string[] parts = NormalizeRelativePath(relativePath)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length <= 1 ? "문항" : parts[0];
        }

        private static string ResolveLessonRootPath(string extractRootPath)
        {
            string[] files = Directory.GetFiles(extractRootPath)
                .Where(file => !string.Equals(Path.GetFileName(file), ".DS_Store", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            string[] directories = Directory.GetDirectories(extractRootPath)
                .Where(directory => !string.Equals(Path.GetFileName(directory), "__MACOSX", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (files.Length == 0 && directories.Length == 1)
            {
                return directories[0];
            }

            return extractRootPath;
        }

        private static string CreateLessonFolderName(string zipFilePath)
        {
            string baseName = Path.GetFileNameWithoutExtension(zipFilePath);
            baseName = Regex.Replace(baseName ?? string.Empty, @"[\\/:*?""<>|]+", "_").Trim();

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "lesson";
            }

            return $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        public static string CreateLessonProblemKey(SubmissionProblemDocument problem, string relativePath)
        {
            string baseKey = SubmissionHistoryStore.GetProblemKey(problem);
            string pathHash = CreateStableHash(NormalizeRelativePath(relativePath))[..8];
            return $"{baseKey}_{pathHash}";
        }

        private static string CreateStableHash(string text)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)))
                .ToLowerInvariant();
        }

        public static string NormalizeRelativePath(string path)
        {
            return (path ?? string.Empty)
                .Replace('\\', '/')
                .TrimStart('/');
        }
    }

    public sealed class LessonContext
    {
        public string LessonId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string RootPath { get; set; } = string.Empty;
        public string SourceZipPath { get; set; } = string.Empty;
        public string SubmissionsRoot { get; set; } = string.Empty;
        public List<LessonSection> Sections { get; set; } = new();

        public IEnumerable<LessonProblemItem> Problems => Sections.SelectMany(section => section.Problems);
    }

    public sealed class LessonSection
    {
        public string Title { get; set; } = string.Empty;
        public List<LessonProblemItem> Problems { get; set; } = new();
    }

    public sealed class LessonProblemItem
    {
        public string SectionTitle { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public ProblemDocument Problem { get; set; } = new();
        public SubmissionProblemDocument SubmissionProblem { get; set; } = new();
        public string SubmissionKey { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public bool HasAccepted { get; set; }
        public string LastVerdict { get; set; } = string.Empty;
    }
}
