using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Local_Judge
{
    public sealed class LocalJudgeSettingsStore
    {
        private const string SettingsFileName = "settings.json";

        private readonly JsonSerializerOptions _jsonOptions;

        public LocalJudgeSettingsStore(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        public LocalJudgeUserSettings Load()
        {
            string filePath = GetSettingsFilePath();
            if (!File.Exists(filePath))
            {
                return new LocalJudgeUserSettings();
            }

            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                LocalJudgeUserSettings settings = JsonSerializer.Deserialize<LocalJudgeUserSettings>(json, _jsonOptions)
                                                  ?? new LocalJudgeUserSettings();
                settings.Normalize();
                return settings;
            }
            catch
            {
                return new LocalJudgeUserSettings();
            }
        }

        public void Save(LocalJudgeUserSettings settings)
        {
            settings.Normalize();
            string filePath = GetSettingsFilePath();
            string? directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(filePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static string GetSettingsFilePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Local Judge",
                SettingsFileName);
        }
    }

    public sealed class LocalJudgeUserSettings
    {
        public const string DefaultPythonEditorCode = "import sys\n\n\ndef main():\n    pass\n\n\nif __name__ == \"__main__\":\n    main()\n";
        public const string DefaultEditorTheme = "localJudgeDark";
        public const string MonacoThemesPrefix = "monaco-themes:";
        public const int DefaultAutoSaveDraftIntervalSeconds = 30;
        public const int MinAutoSaveDraftIntervalSeconds = 5;
        public const int MaxAutoSaveDraftIntervalSeconds = 3600;

        public string PythonExecutablePath { get; set; } = string.Empty;
        public string ProblemSaveDirectory { get; set; } = string.Empty;
        public string DefaultProblemAuthorName { get; set; } = string.Empty;
        public string DefaultProblemSource { get; set; } = string.Empty;
        public string SubmissionHistoryExportDirectory { get; set; } = string.Empty;
        public string EditorDefaultCode { get; set; } = DefaultPythonEditorCode;
        public string EditorTheme { get; set; } = DefaultEditorTheme;
        public bool AutoSaveDraftsEnabled { get; set; } = true;
        public int AutoSaveDraftIntervalSeconds { get; set; } = DefaultAutoSaveDraftIntervalSeconds;

        public void Normalize()
        {
            DefaultProblemAuthorName = (DefaultProblemAuthorName ?? string.Empty).Trim();
            DefaultProblemSource = (DefaultProblemSource ?? string.Empty).Trim();
            EditorDefaultCode ??= DefaultPythonEditorCode;
            EditorTheme = NormalizeEditorTheme(EditorTheme);

            if (AutoSaveDraftIntervalSeconds <= 0)
            {
                AutoSaveDraftIntervalSeconds = DefaultAutoSaveDraftIntervalSeconds;
            }

            AutoSaveDraftIntervalSeconds = Math.Clamp(
                AutoSaveDraftIntervalSeconds,
                MinAutoSaveDraftIntervalSeconds,
                MaxAutoSaveDraftIntervalSeconds);
        }

        public static string NormalizeEditorTheme(string? theme)
        {
            string normalizedTheme = (theme ?? string.Empty).Trim();
            if (normalizedTheme.StartsWith(MonacoThemesPrefix, StringComparison.Ordinal)
                && normalizedTheme.Length > MonacoThemesPrefix.Length)
            {
                return normalizedTheme;
            }

            return normalizedTheme switch
            {
                "localJudgeLight" or "vs" => "localJudgeLight",
                "localJudgeDark" or "vs-dark" => "localJudgeDark",
                "hc-black" => "hc-black",
                "hc-light" => "hc-light",
                _ => DefaultEditorTheme
            };
        }
    }
}
