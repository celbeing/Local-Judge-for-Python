using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;

namespace Local_Judge
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow(LocalJudgeUserSettings settings)
        {
            InitializeComponent();

            ProblemSaveDirectoryTextBox.Text = settings.ProblemSaveDirectory ?? string.Empty;
            SubmissionHistoryExportDirectoryTextBox.Text = settings.SubmissionHistoryExportDirectory ?? string.Empty;
            AutoSaveDraftsCheckBox.IsChecked = settings.AutoSaveDraftsEnabled;
            AutoSaveIntervalTextBox.Text = settings.AutoSaveDraftIntervalSeconds.ToString();
            UpdateAutoSaveControls();
        }

        public string ProblemSaveDirectory { get; private set; } = string.Empty;
        public string SubmissionHistoryExportDirectory { get; private set; } = string.Empty;
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
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProblemSaveDirectory = NormalizeDirectoryPath(ProblemSaveDirectoryTextBox.Text);
                SubmissionHistoryExportDirectory = NormalizeDirectoryPath(SubmissionHistoryExportDirectoryTextBox.Text);
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
