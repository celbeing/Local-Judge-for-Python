using System;
using System.IO;

namespace Local_Judge
{
    public static class EmbeddedPythonRuntime
    {
        public const string RelativeDirectory = @"Runtime\Python313";
        public const string ExecutableFileName = "python.exe";
        public const string FallbackCommand = "python";

        public static string GetExecutablePath()
        {
            return Path.Combine(AppContext.BaseDirectory, RelativeDirectory, ExecutableFileName);
        }

        public static string ResolveDefaultExecutablePath()
        {
            string embeddedPath = GetExecutablePath();
            return File.Exists(embeddedPath)
                ? embeddedPath
                : FallbackCommand;
        }

        public static bool IsAvailable()
        {
            return File.Exists(GetExecutablePath());
        }

        public static bool IsEmbeddedPath(string? executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(executablePath),
                    Path.GetFullPath(GetExecutablePath()),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string FormatDisplayName(string executablePath)
        {
            return IsEmbeddedPath(executablePath)
                ? $"Embedded Python 3.13 ({executablePath})"
                : executablePath;
        }
    }
}
