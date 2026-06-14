using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Local_Judge
{
    public sealed class ContestPackageWriter
    {
        private readonly JsonSerializerOptions _jsonOptions;

        public ContestPackageWriter(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        public ContestPackageWriteResult WriteZip(ContestPackageWriteRequest request)
        {
            if (!ContestTestCaseCrypto.IsValidPin(request.TestCasePassword))
            {
                throw new InvalidOperationException("대회 채점 테스트케이스 암호는 4자리 숫자여야 합니다.");
            }

            string? destinationDirectory = Path.GetDirectoryName(request.DestinationFilePath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (File.Exists(request.DestinationFilePath))
            {
                File.Delete(request.DestinationFilePath);
            }

            var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var problemEntries = new List<ContestProblemPackageEntry>();

            foreach (ContestPackageProblemSource problem in request.Problems)
            {
                string entryName = CreateProblemEntryName(problem, usedEntryNames);
                usedEntryNames.Add(entryName);
                problemEntries.Add(new ContestProblemPackageEntry(problem, entryName));
            }

            var manifest = new ContestManifestDocument
            {
                Title = request.Title,
                StartsAt = request.StartsAt,
                EndsAt = request.EndsAt,
                AdditionalInfo = request.AdditionalInfo,
                WrongSubmissionPenaltyMinutes = request.WrongSubmissionPenaltyMinutes,
                Problems = problemEntries
                    .Select(entry => new ContestProblemManifestItem
                    {
                        Path = entry.EntryName,
                        Label = entry.Problem.Label,
                        Score = entry.Problem.Score,
                        BalloonColor = entry.Problem.BalloonColor
                    })
                    .ToList()
            };

            using FileStream outputStream = File.Create(request.DestinationFilePath);
            using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create);

            AddTextEntry(
                archive,
                "contest.json",
                JsonSerializer.Serialize(manifest, _jsonOptions));

            int fileCount = 1;
            foreach (ContestProblemPackageEntry entry in problemEntries)
            {
                AddEncryptedProblemEntry(archive, entry.EntryName, entry.Problem.SourceFilePath, request.TestCasePassword, _jsonOptions);
                fileCount++;
                fileCount += AddProblemAssets(archive, entry);
            }

            return new ContestPackageWriteResult(
                request.DestinationFilePath,
                problemEntries.Count,
                fileCount);
        }

        private static string CreateProblemEntryName(
            ContestPackageProblemSource problem,
            ISet<string> usedEntryNames)
        {
            string sourceBaseName = Path.GetFileNameWithoutExtension(problem.SourceFilePath);
            string baseName = string.IsNullOrWhiteSpace(sourceBaseName)
                ? problem.Title
                : sourceBaseName;
            baseName = SanitizeEntryNamePart($"{problem.Label}_{baseName}");

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = problem.Label;
            }

            string candidate = $"problems/{baseName}.json";
            int index = 2;
            while (usedEntryNames.Contains(candidate))
            {
                candidate = $"problems/{baseName}_{index}.json";
                index++;
            }

            return candidate;
        }

        private static string SanitizeEntryNamePart(string text)
        {
            string sanitized = Regex.Replace(text ?? string.Empty, @"[\\/:*?""<>|]+", "_");
            sanitized = Regex.Replace(sanitized, @"\s+", "_").Trim('_', '.', ' ');
            return sanitized;
        }

        private static int AddProblemAssets(ZipArchive archive, ContestProblemPackageEntry entry)
        {
            string sourceAssetFolderPath = ProblemAssetUtilities.GetAssetFolderPath(entry.Problem.SourceFilePath);
            if (!Directory.Exists(sourceAssetFolderPath))
            {
                return 0;
            }

            string entryDirectory = Path.GetDirectoryName(entry.EntryName)?.Replace('\\', '/') ?? string.Empty;
            string entryBaseName = Path.GetFileNameWithoutExtension(entry.EntryName);
            string assetEntryRoot = string.IsNullOrWhiteSpace(entryDirectory)
                ? entryBaseName + ".assets"
                : $"{entryDirectory}/{entryBaseName}.assets";

            int fileCount = 0;
            foreach (string sourceFilePath in Directory.EnumerateFiles(sourceAssetFolderPath, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceAssetFolderPath, sourceFilePath).Replace('\\', '/');
                AddFileEntry(archive, $"{assetEntryRoot}/{relativePath}", sourceFilePath);
                fileCount++;
            }

            return fileCount;
        }

        private static void AddFileEntry(ZipArchive archive, string entryName, string sourceFilePath)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using Stream entryStream = entry.Open();
            using FileStream sourceStream = File.OpenRead(sourceFilePath);
            sourceStream.CopyTo(entryStream);
        }

        private static void AddEncryptedProblemEntry(
            ZipArchive archive,
            string entryName,
            string sourceFilePath,
            string testCasePassword,
            JsonSerializerOptions jsonOptions)
        {
            string json = File.ReadAllText(sourceFilePath);
            ProblemDocument problem = JsonSerializer.Deserialize<ProblemDocument>(json, jsonOptions)
                ?? throw new InvalidOperationException($"문제 JSON을 읽을 수 없습니다: {sourceFilePath}");

            problem.TestCases ??= new();
            problem.EncryptedTestCases = ContestTestCaseCrypto.Encrypt(problem.TestCases, testCasePassword, jsonOptions);
            problem.TestCases = new();

            AddTextEntry(archive, entryName, JsonSerializer.Serialize(problem, jsonOptions));
        }

        private static void AddTextEntry(ZipArchive archive, string entryName, string text)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using Stream entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(text);
        }

        private sealed record ContestProblemPackageEntry(
            ContestPackageProblemSource Problem,
            string EntryName);
    }

    public sealed class ContestPackageWriteRequest
    {
        public string DestinationFilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public List<ContestInfoDocument> AdditionalInfo { get; set; } = new();
        public int WrongSubmissionPenaltyMinutes { get; set; } = 20;
        public string TestCasePassword { get; set; } = string.Empty;
        public List<ContestPackageProblemSource> Problems { get; set; } = new();
    }

    public sealed class ContestPackageProblemSource
    {
        public string Label { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SourceFilePath { get; set; } = string.Empty;
        public string BalloonColor { get; set; } = string.Empty;
        public int Score { get; set; } = 1;
    }

    public sealed record ContestPackageWriteResult(
        string FilePath,
        int ProblemCount,
        int FileCount);
}
