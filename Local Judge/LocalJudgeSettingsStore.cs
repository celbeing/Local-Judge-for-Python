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
                return JsonSerializer.Deserialize<LocalJudgeUserSettings>(json, _jsonOptions)
                       ?? new LocalJudgeUserSettings();
            }
            catch
            {
                return new LocalJudgeUserSettings();
            }
        }

        public void Save(LocalJudgeUserSettings settings)
        {
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
        public string PythonExecutablePath { get; set; } = string.Empty;
    }
}
