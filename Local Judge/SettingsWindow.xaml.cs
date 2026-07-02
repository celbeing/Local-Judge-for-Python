using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Local_Judge
{
    public partial class SettingsWindow : Window
    {
        private const string ThemePreviewEditorHostName = "localjudge.settings-theme-preview";
        private const string ThemePreviewSampleCode =
            "# 테마 미리보기: 주석\n" +
            "def solve(limit):\n" +
            "    total = sum(x for x in range(limit) if x % 2 == 0)\n" +
            "    status = \"pass\" if total >= 10 else \"retry\"\n" +
            "    return status, total\n";

        private bool _isLoadingSettings = true;
        private bool _isThemePreviewInitialized;
        private bool _isThemePreviewReady;
        private string _originalProblemSaveDirectory = string.Empty;
        private string _originalDefaultProblemAuthorName = string.Empty;
        private string _originalDefaultProblemSource = string.Empty;
        private string _originalSubmissionHistoryExportDirectory = string.Empty;
        private string _originalEditorTheme = LocalJudgeUserSettings.DefaultEditorTheme;
        private bool _originalAutoSaveDraftsEnabled = true;
        private int _originalAutoSaveDraftIntervalSeconds = LocalJudgeUserSettings.DefaultAutoSaveDraftIntervalSeconds;
        private List<EditorThemeOption> _editorThemeOptions = new();

        public SettingsWindow(LocalJudgeUserSettings settings)
        {
            InitializeComponent();

            settings.Normalize();
            _originalProblemSaveDirectory = settings.ProblemSaveDirectory ?? string.Empty;
            _originalDefaultProblemAuthorName = settings.DefaultProblemAuthorName ?? string.Empty;
            _originalDefaultProblemSource = settings.DefaultProblemSource ?? string.Empty;
            _originalSubmissionHistoryExportDirectory = settings.SubmissionHistoryExportDirectory ?? string.Empty;
            _originalEditorTheme = settings.EditorTheme;
            _originalAutoSaveDraftsEnabled = settings.AutoSaveDraftsEnabled;
            _originalAutoSaveDraftIntervalSeconds = settings.AutoSaveDraftIntervalSeconds;
            _editorThemeOptions = LoadEditorThemeOptions();
            EditorThemeComboBox.ItemsSource = _editorThemeOptions;

            ProblemSaveDirectoryTextBox.Text = _originalProblemSaveDirectory;
            DefaultProblemAuthorNameTextBox.Text = _originalDefaultProblemAuthorName;
            DefaultProblemSourceTextBox.Text = _originalDefaultProblemSource;
            SubmissionHistoryExportDirectoryTextBox.Text = _originalSubmissionHistoryExportDirectory;
            SelectEditorTheme(_originalEditorTheme);
            AutoSaveDraftsCheckBox.IsChecked = _originalAutoSaveDraftsEnabled;
            AutoSaveIntervalTextBox.Text = _originalAutoSaveDraftIntervalSeconds.ToString();

            ProblemSaveDirectoryTextBox.TextChanged += (_, _) => UpdateSaveButtonState();
            DefaultProblemAuthorNameTextBox.TextChanged += (_, _) => UpdateSaveButtonState();
            DefaultProblemSourceTextBox.TextChanged += (_, _) => UpdateSaveButtonState();
            SubmissionHistoryExportDirectoryTextBox.TextChanged += (_, _) => UpdateSaveButtonState();
            AutoSaveIntervalTextBox.TextChanged += (_, _) => UpdateSaveButtonState();

            _isLoadingSettings = false;
            UpdateAutoSaveControls();
            UpdateSaveButtonState();

            Loaded += async (_, _) => await InitializeThemePreviewAsync();
        }

        public string ProblemSaveDirectory { get; private set; } = string.Empty;
        public string DefaultProblemAuthorName { get; private set; } = string.Empty;
        public string DefaultProblemSource { get; private set; } = string.Empty;
        public string SubmissionHistoryExportDirectory { get; private set; } = string.Empty;
        public string EditorTheme { get; private set; } = LocalJudgeUserSettings.DefaultEditorTheme;
        public bool AutoSaveDraftsEnabled { get; private set; } = true;
        public int AutoSaveDraftIntervalSeconds { get; private set; } = LocalJudgeUserSettings.DefaultAutoSaveDraftIntervalSeconds;

        private void BrowseProblemSaveDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            string? selectedPath = SelectFolder(
                "문항 저장 경로 선택",
                ProblemSaveDirectoryTextBox.Text);
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                ProblemSaveDirectoryTextBox.Text = selectedPath;
            }
        }

        private void BrowseSubmissionHistoryExportDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            string? selectedPath = SelectFolder(
                "제출 이력 내보내기 경로 선택",
                SubmissionHistoryExportDirectoryTextBox.Text);
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                SubmissionHistoryExportDirectoryTextBox.Text = selectedPath;
            }
        }

        private void ClearProblemSaveDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            ProblemSaveDirectoryTextBox.Text = string.Empty;
        }

        private void ClearSubmissionHistoryExportDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            SubmissionHistoryExportDirectoryTextBox.Text = string.Empty;
        }

        private void AutoSaveDraftsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateAutoSaveControls();
            UpdateSaveButtonState();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProblemSaveDirectory = NormalizeDirectoryPath(ProblemSaveDirectoryTextBox.Text);
                DefaultProblemAuthorName = NormalizeOptionalText(DefaultProblemAuthorNameTextBox.Text);
                DefaultProblemSource = NormalizeOptionalText(DefaultProblemSourceTextBox.Text);
                SubmissionHistoryExportDirectory = NormalizeDirectoryPath(SubmissionHistoryExportDirectoryTextBox.Text);
                EditorTheme = GetSelectedEditorTheme();
                AutoSaveDraftsEnabled = AutoSaveDraftsCheckBox.IsChecked == true;
                AutoSaveDraftIntervalSeconds = ParseAutoSaveIntervalSeconds(AutoSaveIntervalTextBox.Text);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "환경 설정",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void UpdateAutoSaveControls()
        {
            bool isEnabled = AutoSaveDraftsCheckBox.IsChecked == true;
            AutoSaveIntervalTextBox.IsEnabled = isEnabled;
            AutoSaveIntervalUnitTextBlock.IsEnabled = isEnabled;
            AutoSaveIntervalHelpTextBlock.IsEnabled = isEnabled;
        }

        private void SettingsInput_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSaveButtonState();
            ApplyThemePreviewTheme();
        }

        private async Task InitializeThemePreviewAsync()
        {
            if (_isThemePreviewInitialized)
            {
                return;
            }

            try
            {
                await ThemePreviewWebView.EnsureCoreWebView2Async();

                string editorFolderPath = Path.Combine(AppContext.BaseDirectory, "Editor");
                if (!Directory.Exists(editorFolderPath))
                {
                    MessageBox.Show(
                        $"테마 미리보기 리소스를 찾을 수 없습니다.\n\n{editorFolderPath}",
                        "테마 미리보기 초기화 실패",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                ThemePreviewWebView.CoreWebView2.WebMessageReceived += ThemePreviewWebView_WebMessageReceived;
                ThemePreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    ThemePreviewEditorHostName,
                    editorFolderPath,
                    CoreWebView2HostResourceAccessKind.Allow);

                _isThemePreviewInitialized = true;
                ThemePreviewWebView.Source = new Uri($"https://{ThemePreviewEditorHostName}/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"테마 미리보기 초기화 중 오류가 발생했습니다.\n\n{ex.Message}",
                    "테마 미리보기 초기화 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ThemePreviewWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
                JsonElement root = document.RootElement;

                if (!root.TryGetProperty("type", out JsonElement typeElement))
                {
                    return;
                }

                if (typeElement.GetString() == "editorReady")
                {
                    _isThemePreviewReady = true;
                    ApplyThemePreviewTheme();
                    SetThemePreviewCode();
                    SetThemePreviewReadOnly(true);
                }
            }
            catch
            {
                // 테마 미리보기 메시지는 사용자 설정 저장 흐름에 영향을 주지 않습니다.
            }
        }

        private void ApplyThemePreviewTheme()
        {
            if (!_isThemePreviewReady || ThemePreviewWebView.CoreWebView2 is null)
            {
                return;
            }

            string script = JsonSerializer.Serialize(new
            {
                type = "setTheme",
                theme = GetSelectedEditorTheme()
            });

            ThemePreviewWebView.CoreWebView2.PostWebMessageAsJson(script);
        }

        private void SetThemePreviewCode()
        {
            if (!_isThemePreviewReady || ThemePreviewWebView.CoreWebView2 is null)
            {
                return;
            }

            string script = JsonSerializer.Serialize(new
            {
                type = "setCode",
                code = ThemePreviewSampleCode
            });

            ThemePreviewWebView.CoreWebView2.PostWebMessageAsJson(script);
        }

        private void SetThemePreviewReadOnly(bool readOnly)
        {
            if (!_isThemePreviewReady || ThemePreviewWebView.CoreWebView2 is null)
            {
                return;
            }

            string script = JsonSerializer.Serialize(new
            {
                type = "setReadOnly",
                readOnly
            });

            ThemePreviewWebView.CoreWebView2.PostWebMessageAsJson(script);
        }

        private void UpdateSaveButtonState()
        {
            if (_isLoadingSettings)
            {
                return;
            }

            SaveButton.IsEnabled = HasSettingsChanges();
        }

        private bool HasSettingsChanges()
        {
            return !string.Equals(ProblemSaveDirectoryTextBox.Text ?? string.Empty, _originalProblemSaveDirectory, StringComparison.Ordinal)
                   || !string.Equals(NormalizeOptionalText(DefaultProblemAuthorNameTextBox.Text), _originalDefaultProblemAuthorName, StringComparison.Ordinal)
                   || !string.Equals(NormalizeOptionalText(DefaultProblemSourceTextBox.Text), _originalDefaultProblemSource, StringComparison.Ordinal)
                   || !string.Equals(SubmissionHistoryExportDirectoryTextBox.Text ?? string.Empty, _originalSubmissionHistoryExportDirectory, StringComparison.Ordinal)
                   || !string.Equals(GetSelectedEditorTheme(), _originalEditorTheme, StringComparison.Ordinal)
                   || (AutoSaveDraftsCheckBox.IsChecked == true) != _originalAutoSaveDraftsEnabled
                   || !string.Equals((AutoSaveIntervalTextBox.Text ?? string.Empty).Trim(), _originalAutoSaveDraftIntervalSeconds.ToString(), StringComparison.Ordinal);
        }

        private void SelectEditorTheme(string theme)
        {
            string normalizedTheme = LocalJudgeUserSettings.NormalizeEditorTheme(theme);
            foreach (EditorThemeOption option in _editorThemeOptions)
            {
                if (string.Equals(option.Id, normalizedTheme, StringComparison.Ordinal))
                {
                    EditorThemeComboBox.SelectedValue = option.Id;
                    return;
                }
            }

            EditorThemeComboBox.SelectedValue = LocalJudgeUserSettings.DefaultEditorTheme;
        }

        private string GetSelectedEditorTheme()
        {
            if (EditorThemeComboBox.SelectedValue is string selectedTheme)
            {
                return LocalJudgeUserSettings.NormalizeEditorTheme(selectedTheme);
            }

            return LocalJudgeUserSettings.DefaultEditorTheme;
        }

        private static List<EditorThemeOption> LoadEditorThemeOptions()
        {
            List<EditorThemeOption> fallbackOptions = CreateFallbackEditorThemeOptions();
            string catalogPath = Path.Combine(AppContext.BaseDirectory, "Editor", "themes", "themeCatalog.json");

            if (!File.Exists(catalogPath))
            {
                return fallbackOptions;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(catalogPath));
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return fallbackOptions;
                }

                var options = new List<EditorThemeOption>();
                var usedIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement item in document.RootElement.EnumerateArray())
                {
                    string id = item.TryGetProperty("id", out JsonElement idElement)
                        ? LocalJudgeUserSettings.NormalizeEditorTheme(idElement.GetString())
                        : string.Empty;
                    string label = item.TryGetProperty("label", out JsonElement labelElement)
                        ? labelElement.GetString() ?? string.Empty
                        : string.Empty;

                    if (string.IsNullOrWhiteSpace(id)
                        || string.IsNullOrWhiteSpace(label)
                        || !usedIds.Add(id))
                    {
                        continue;
                    }

                    options.Add(new EditorThemeOption(id, label));
                }

                return options.Count == 0 ? fallbackOptions : options;
            }
            catch
            {
                return fallbackOptions;
            }
        }

        private static List<EditorThemeOption> CreateFallbackEditorThemeOptions()
        {
            return new List<EditorThemeOption>
            {
                new(LocalJudgeUserSettings.DefaultEditorTheme, "어둡게"),
                new("localJudgeLight", "밝게"),
                new("hc-black", "고대비 검정"),
                new("hc-light", "고대비 밝게")
            };
        }

        private sealed record EditorThemeOption(string Id, string Label);

        private string? SelectFolder(string title, string currentPath)
        {
            var dialog = new OpenFolderDialog
            {
                Title = title,
                Multiselect = false
            };

            if (TryGetExistingDirectory(currentPath, out string existingDirectory))
            {
                dialog.InitialDirectory = existingDirectory;
            }

            bool? result = dialog.ShowDialog(this);
            return result == true ? dialog.FolderName : null;
        }

        private static string NormalizeDirectoryPath(string path)
        {
            string trimmedPath = (path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                return string.Empty;
            }

            string fullPath = Path.GetFullPath(trimmedPath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        private static string NormalizeOptionalText(string text)
        {
            return (text ?? string.Empty).Trim();
        }

        private static int ParseAutoSaveIntervalSeconds(string text)
        {
            if (!int.TryParse((text ?? string.Empty).Trim(), out int seconds))
            {
                throw new InvalidOperationException("자동 저장 주기는 초 단위 숫자로 입력해 주세요.");
            }

            if (seconds < LocalJudgeUserSettings.MinAutoSaveDraftIntervalSeconds
                || seconds > LocalJudgeUserSettings.MaxAutoSaveDraftIntervalSeconds)
            {
                throw new InvalidOperationException(
                    $"자동 저장 주기는 {LocalJudgeUserSettings.MinAutoSaveDraftIntervalSeconds}초부터 {LocalJudgeUserSettings.MaxAutoSaveDraftIntervalSeconds}초까지 설정할 수 있습니다.");
            }

            return seconds;
        }

        private static bool TryGetExistingDirectory(string path, out string existingDirectory)
        {
            existingDirectory = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                if (!Directory.Exists(fullPath))
                {
                    return false;
                }

                existingDirectory = fullPath;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
