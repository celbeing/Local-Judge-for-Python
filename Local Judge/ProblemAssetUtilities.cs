using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Local_Judge
{
    public static class ProblemAssetUtilities
    {
        public const int CurrentProblemVersion = 4;
        public const string MarkdownAssetPrefix = "assets/";

        private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".gif",
            ".webp"
        };

        public static string NormalizeStatementFormat(string? format, bool defaultToMarkdownLatex)
        {
            if (string.Equals(format, ProblemStatementFormats.MarkdownLatex, StringComparison.OrdinalIgnoreCase))
            {
                return ProblemStatementFormats.MarkdownLatex;
            }

            if (string.Equals(format, ProblemStatementFormats.Plain, StringComparison.OrdinalIgnoreCase))
            {
                return ProblemStatementFormats.Plain;
            }

            return defaultToMarkdownLatex
                ? ProblemStatementFormats.MarkdownLatex
                : ProblemStatementFormats.Plain;
        }

        public static bool IsSupportedImageFile(string filePath)
        {
            return SupportedImageExtensions.Contains(Path.GetExtension(filePath));
        }

        public static string GetContentType(string filePath)
        {
            return Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };
        }

        public static string GetAssetFolderPath(string problemFilePath)
        {
            string directory = Path.GetDirectoryName(problemFilePath) ?? AppContext.BaseDirectory;
            string fileName = Path.GetFileNameWithoutExtension(problemFilePath);
            return Path.Combine(directory, fileName + ".assets");
        }

        public static string ToMarkdownAssetPath(string fileName)
        {
            return MarkdownAssetPrefix + fileName.Replace('\\', '/');
        }

        public static string ToAssetFileName(string relativePath)
        {
            string normalized = (relativePath ?? string.Empty).Replace('\\', '/');
            if (normalized.StartsWith(MarkdownAssetPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[MarkdownAssetPrefix.Length..];
            }

            return Path.GetFileName(normalized);
        }

        public static string CreateSafeAssetFileName(string sourceFilePath, ISet<string> reservedNames)
        {
            string extension = Path.GetExtension(sourceFilePath);
            string baseName = Path.GetFileNameWithoutExtension(sourceFilePath);
            baseName = Regex.Replace(baseName, @"[^\w\-]+", "_").Trim('_');

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "image";
            }

            string candidate = baseName + extension.ToLowerInvariant();
            int index = 2;

            while (reservedNames.Contains(candidate))
            {
                candidate = $"{baseName}_{index}{extension.ToLowerInvariant()}";
                index++;
            }

            reservedNames.Add(candidate);
            return candidate;
        }

        public static void CopyAssetFolder(string? sourceFolderPath, string targetFolderPath)
        {
            if (string.IsNullOrWhiteSpace(sourceFolderPath) || !Directory.Exists(sourceFolderPath))
            {
                return;
            }

            Directory.CreateDirectory(targetFolderPath);

            foreach (string sourceFilePath in Directory.EnumerateFiles(sourceFolderPath, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceFolderPath, sourceFilePath);
                string targetFilePath = Path.Combine(targetFolderPath, relativePath);
                string? targetDirectory = Path.GetDirectoryName(targetFilePath);

                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(sourceFilePath, targetFilePath, overwrite: true);
            }
        }

        public static List<ProblemAssetDocument> CloneAssets(IEnumerable<ProblemAssetDocument>? assets)
        {
            return (assets ?? Enumerable.Empty<ProblemAssetDocument>())
                .Select(asset => new ProblemAssetDocument
                {
                    Id = asset.Id,
                    FileName = asset.FileName,
                    RelativePath = asset.RelativePath,
                    ContentType = asset.ContentType
                })
                .ToList();
        }
    }
}
