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
    public sealed class ContestPackageReader
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public ContestPackageReader(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        public ContestContext OpenZip(string zipFilePath)
        {
            string contestRootBase = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Local Judge",
                "Contests");
            Directory.CreateDirectory(contestRootBase);

            string contestFolderName = CreateContestFolderName(zipFilePath);
            string extractRootPath = Path.Combine(contestRootBase, contestFolderName);

            if (Directory.Exists(extractRootPath))
            {
                extractRootPath = Path.Combine(contestRootBase, contestFolderName + "_" + Guid.NewGuid().ToString("N")[..8]);
            }

            Directory.CreateDirectory(extractRootPath);

            try
            {
                ZipFile.ExtractToDirectory(zipFilePath, extractRootPath);
            }
            catch
            {
                throw new InvalidOperationException("대회 ZIP 파일을 열 수 없습니다.");
            }

            string contestRootPath = ResolveContestRootPath(extractRootPath);
            return ReadFolder(contestRootPath, zipFilePath);
        }

        public ContestManifestDocument ReadManifestFromZip(string zipFilePath)
        {
            try
            {
                using ZipArchive archive = ZipFile.OpenRead(zipFilePath);
                ZipArchiveEntry manifestEntry = FindContestManifestEntry(archive)
                    ?? throw new InvalidOperationException("contest.json 파일을 찾지 못했습니다.");

                using Stream stream = manifestEntry.Open();
                ContestManifestDocument manifest = JsonSerializer.Deserialize<ContestManifestDocument>(stream, _jsonOptions)
                    ?? throw new InvalidOperationException("contest.json을 읽을 수 없습니다.");
                ValidateContestManifest(manifest);
                return manifest;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("contest.json 형식이 올바르지 않습니다.", ex);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException("대회 ZIP 파일을 열 수 없습니다.", ex);
            }
        }

        public ContestContext ReadFolder(string contestRootPath, string? sourceZipPath = null)
        {
            if (!Directory.Exists(contestRootPath))
            {
                throw new InvalidOperationException("대회 폴더를 찾을 수 없습니다.");
            }

            string contestManifestPath = Path.Combine(contestRootPath, "contest.json");
            if (!File.Exists(contestManifestPath))
            {
                throw new InvalidOperationException("contest.json 파일을 찾지 못했습니다.");
            }

            ContestManifestDocument manifest = ReadContestManifest(contestManifestPath);
            ValidateContestManifest(manifest);

            List<ContestProblemItem> problems = ReadProblems(contestRootPath, manifest);
            if (problems.Count == 0)
            {
                throw new InvalidOperationException("대회에서 문제 JSON 파일을 찾지 못했습니다.");
            }

            string title = string.IsNullOrWhiteSpace(manifest.Title) ? "대회" : manifest.Title.Trim();
            string contestId = CreateStableHash(string.Join(
                "\n",
                title,
                manifest.StartsAt.ToString("O"),
                manifest.EndsAt.ToString("O"),
                Path.GetFullPath(contestRootPath)));

            return new ContestContext
            {
                ContestId = contestId,
                Title = title,
                StartsAt = manifest.StartsAt,
                EndsAt = manifest.EndsAt,
                Venue = manifest.Venue ?? string.Empty,
                Organizer = manifest.Organizer ?? string.Empty,
                Prize = manifest.Prize ?? string.Empty,
                Caption = manifest.Caption ?? string.Empty,
                AdditionalInfo = NormalizeContestInfo(manifest),
                WrongSubmissionPenaltyMinutes = manifest.WrongSubmissionPenaltyMinutes <= 0
                    ? 20
                    : manifest.WrongSubmissionPenaltyMinutes,
                RootPath = contestRootPath,
                SourceZipPath = sourceZipPath ?? string.Empty,
                SubmissionsRoot = Path.Combine(contestRootPath, ".localjudge", "submissions"),
                Problems = problems
            };
        }

        private ContestManifestDocument ReadContestManifest(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<ContestManifestDocument>(json, _jsonOptions)
                       ?? throw new InvalidOperationException("contest.json을 읽을 수 없습니다.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("contest.json 형식이 올바르지 않습니다.", ex);
            }
        }

        private static ZipArchiveEntry? FindContestManifestEntry(ZipArchive archive)
        {
            ZipArchiveEntry? rootEntry = archive.GetEntry("contest.json");
            if (rootEntry is not null)
            {
                return rootEntry;
            }

            return archive.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .FirstOrDefault(entry =>
                    string.Equals(Path.GetFileName(entry.FullName), "contest.json", StringComparison.OrdinalIgnoreCase));
        }

        private static void ValidateContestManifest(ContestManifestDocument manifest)
        {
            if (manifest.StartsAt == default)
            {
                throw new InvalidOperationException("contest.json에 startsAt 값이 필요합니다.");
            }

            if (manifest.EndsAt == default)
            {
                throw new InvalidOperationException("contest.json에 endsAt 값이 필요합니다.");
            }

            if (manifest.EndsAt <= manifest.StartsAt)
            {
                throw new InvalidOperationException("대회 종료 시각은 시작 시각보다 늦어야 합니다.");
            }
        }

        private static List<ContestInfoDocument> NormalizeContestInfo(ContestManifestDocument manifest)
        {
            var items = new List<ContestInfoDocument>();
            items.AddRange((manifest.AdditionalInfo ?? new List<ContestInfoDocument>())
                .Where(item => item is not null)
                .Select(item => new ContestInfoDocument
                {
                    Label = item.Label ?? string.Empty,
                    Text = item.Text ?? string.Empty
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Label)
                               || !string.IsNullOrWhiteSpace(item.Text)));

            AddLegacyContestInfo(items, "장소", manifest.Venue);
            AddLegacyContestInfo(items, "주최", manifest.Organizer);
            AddLegacyContestInfo(items, "상품", manifest.Prize);
            AddLegacyContestInfo(items, "안내", manifest.Caption);
            return items;
        }

        private static void AddLegacyContestInfo(
            List<ContestInfoDocument> items,
            string label,
            string? text)
        {
            if (string.IsNullOrWhiteSpace(text)
                || items.Any(item => string.Equals(item.Label, label, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            items.Add(new ContestInfoDocument
            {
                Label = label,
                Text = text.Trim()
            });
        }

        private List<ContestProblemItem> ReadProblems(string contestRootPath, ContestManifestDocument manifest)
        {
            List<string> orderedRelativePaths = GetProblemRelativePaths(contestRootPath, manifest);
            var problems = new List<ContestProblemItem>();

            for (int i = 0; i < orderedRelativePaths.Count; i++)
            {
                string relativePath = NormalizeRelativePath(orderedRelativePaths[i]);
                string filePath = Path.Combine(contestRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

                try
                {
                    string json = File.ReadAllText(filePath);
                    ProblemDocument? problem = JsonSerializer.Deserialize<ProblemDocument>(json, _jsonOptions);
                    if (problem is null || string.IsNullOrWhiteSpace(problem.Title))
                    {
                        continue;
                    }

                    NormalizeProblem(problem);
                    SubmissionProblemDocument submissionProblem = CreateSubmissionProblem(problem);
                    ContestProblemManifestItem? problemManifest = manifest.Problems.FirstOrDefault(item =>
                        string.Equals(
                            NormalizeRelativePath(GetProblemManifestPath(item)),
                            relativePath,
                            StringComparison.OrdinalIgnoreCase));

                    string label = string.IsNullOrWhiteSpace(problemManifest?.Label)
                        ? CreateProblemLabel(i)
                        : problemManifest!.Label.Trim();

                    problems.Add(new ContestProblemItem
                    {
                        Label = label,
                        RelativePath = relativePath,
                        FilePath = filePath,
                        Problem = problem,
                        SubmissionProblem = submissionProblem,
                        SubmissionKey = CreateContestProblemKey(submissionProblem, relativePath),
                        Score = problemManifest?.Score > 0 ? problemManifest.Score : 1,
                        BalloonColor = string.IsNullOrWhiteSpace(problemManifest?.BalloonColor)
                            ? GetDefaultBalloonColor(i)
                            : problemManifest!.BalloonColor.Trim()
                    });
                }
                catch
                {
                    // Ignore malformed problem candidates. If none are valid, the caller reports an invalid contest.
                }
            }

            return problems;
        }

        private List<string> GetProblemRelativePaths(string contestRootPath, ContestManifestDocument manifest)
        {
            if (manifest.Problems.Count > 0)
            {
                return manifest.Problems
                    .Select(GetProblemManifestPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(NormalizeRelativePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return Directory.EnumerateFiles(contestRootPath, "*.json", SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(Path.GetRelativePath(contestRootPath, path)))
                .Where(IsProblemJsonCandidate)
                .OrderBy(path => path, NaturalStringComparer.Instance)
                .ToList();
        }

        private static string GetProblemManifestPath(ContestProblemManifestItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Path))
            {
                return item.Path;
            }

            if (!string.IsNullOrWhiteSpace(item.File))
            {
                return item.File;
            }

            return item.ProblemFilePath ?? string.Empty;
        }

        private static bool IsProblemJsonCandidate(string relativePath)
        {
            string normalized = NormalizeRelativePath(relativePath);
            string fileName = Path.GetFileName(normalized);
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

            return !segments.Any(segment => string.Equals(segment, ".localjudge", StringComparison.OrdinalIgnoreCase))
                   && !segments.Any(segment => segment.EndsWith(".assets", StringComparison.OrdinalIgnoreCase))
                   && !string.Equals(fileName, "contest.json", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(fileName, "lesson.json", StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase);
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

        private static string ResolveContestRootPath(string extractRootPath)
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

        private static string CreateContestFolderName(string zipFilePath)
        {
            string baseName = Path.GetFileNameWithoutExtension(zipFilePath);
            baseName = Regex.Replace(baseName ?? string.Empty, @"[\\/:*?""<>|]+", "_").Trim();

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "contest";
            }

            return $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        public static string CreateContestProblemKey(SubmissionProblemDocument problem, string relativePath)
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

        public static string CreateProblemLabel(int zeroBasedIndex)
        {
            int value = zeroBasedIndex;
            var builder = new StringBuilder();

            do
            {
                builder.Insert(0, (char)('A' + value % 26));
                value = value / 26 - 1;
            }
            while (value >= 0);

            return builder.ToString();
        }

        private static string GetDefaultBalloonColor(int index)
        {
            string[] colors =
            [
                "#E74C3C",
                "#3498DB",
                "#2ECC71",
                "#F1C40F",
                "#9B59B6",
                "#E67E22",
                "#1ABC9C",
                "#E84393",
                "#7F8C8D"
            ];

            return colors[index % colors.Length];
        }
    }

    public sealed class ContestManifestDocument
    {
        public string Title { get; set; } = string.Empty;
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public string Venue { get; set; } = string.Empty;
        public string Organizer { get; set; } = string.Empty;
        public string Prize { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public List<ContestInfoDocument> AdditionalInfo { get; set; } = new();
        public int WrongSubmissionPenaltyMinutes { get; set; } = 20;
        public List<ContestProblemManifestItem> Problems { get; set; } = new();
    }

    public sealed class ContestInfoDocument
    {
        public string Label { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    public sealed class ContestProblemManifestItem
    {
        public string Path { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string ProblemFilePath { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string BalloonColor { get; set; } = string.Empty;
        public int Score { get; set; } = 1;
    }

    public sealed class ContestContext
    {
        public string ContestId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public string Venue { get; set; } = string.Empty;
        public string Organizer { get; set; } = string.Empty;
        public string Prize { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public List<ContestInfoDocument> AdditionalInfo { get; set; } = new();
        public int WrongSubmissionPenaltyMinutes { get; set; } = 20;
        public string RootPath { get; set; } = string.Empty;
        public string SourceZipPath { get; set; } = string.Empty;
        public string SubmissionsRoot { get; set; } = string.Empty;
        public List<ContestProblemItem> Problems { get; set; } = new();
    }

    public sealed class ContestProblemItem
    {
        public string Label { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public ProblemDocument Problem { get; set; } = new();
        public SubmissionProblemDocument SubmissionProblem { get; set; } = new();
        public string SubmissionKey { get; set; } = string.Empty;
        public int Score { get; set; } = 1;
        public string BalloonColor { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public bool HasAccepted { get; set; }
        public string LastVerdict { get; set; } = string.Empty;
    }

    public sealed class NaturalStringComparer : IComparer<string>
    {
        public static NaturalStringComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int xIndex = 0;
            int yIndex = 0;
            while (xIndex < x.Length && yIndex < y.Length)
            {
                if (char.IsDigit(x[xIndex]) && char.IsDigit(y[yIndex]))
                {
                    int comparison = CompareNumberToken(x, ref xIndex, y, ref yIndex);
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    continue;
                }

                int charComparison = string.Compare(
                    x,
                    xIndex,
                    y,
                    yIndex,
                    1,
                    StringComparison.OrdinalIgnoreCase);
                if (charComparison != 0)
                {
                    return charComparison;
                }

                xIndex++;
                yIndex++;
            }

            if (xIndex < x.Length)
            {
                return 1;
            }

            if (yIndex < y.Length)
            {
                return -1;
            }

            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareNumberToken(string x, ref int xIndex, string y, ref int yIndex)
        {
            int xStart = xIndex;
            int yStart = yIndex;

            while (xIndex < x.Length && char.IsDigit(x[xIndex]))
            {
                xIndex++;
            }

            while (yIndex < y.Length && char.IsDigit(y[yIndex]))
            {
                yIndex++;
            }

            string xNumber = x[xStart..xIndex].TrimStart('0');
            string yNumber = y[yStart..yIndex].TrimStart('0');
            xNumber = xNumber.Length == 0 ? "0" : xNumber;
            yNumber = yNumber.Length == 0 ? "0" : yNumber;

            if (xNumber.Length != yNumber.Length)
            {
                return xNumber.Length.CompareTo(yNumber.Length);
            }

            int comparison = string.Compare(xNumber, yNumber, StringComparison.Ordinal);
            if (comparison != 0)
            {
                return comparison;
            }

            return (xIndex - xStart).CompareTo(yIndex - yStart);
        }
    }
}
