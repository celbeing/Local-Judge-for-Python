using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Local_Judge
{
    public partial class ContestCreatorWindow : Window
    {
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ContestPackageWriter _packageWriter;
        private readonly string? _defaultSaveDirectory;

        public ContestCreatorWindow(
            JsonSerializerOptions jsonOptions,
            string? defaultSaveDirectory = null)
        {
            _jsonOptions = jsonOptions;
            _packageWriter = new ContestPackageWriter(jsonOptions);
            _defaultSaveDirectory = defaultSaveDirectory;

            InitializeComponent();
            DataContext = this;

            DateTime now = DateTime.Now;
            DateTime start = new(now.Year, now.Month, now.Day, now.Hour, 0, 0);
            start = start.AddHours(1);
            DateTime end = start.AddHours(2);

            StartDatePicker.SelectedDate = start.Date;
            StartHourComboBox.SelectedItem = start.Hour;
            StartMinuteComboBox.SelectedItem = RoundDownToFiveMinutes(start.Minute);
            EndDatePicker.SelectedDate = end.Date;
            EndHourComboBox.SelectedItem = end.Hour;
            EndMinuteComboBox.SelectedItem = RoundDownToFiveMinutes(end.Minute);
            UpdateProblemCount();
        }

        public ObservableCollection<ContestProblemEditorItem> ContestProblems { get; } = new();
        public ObservableCollection<ContestInfoEditorItem> ContestInfoItems { get; } = new();

        public IReadOnlyList<int> HourOptions { get; } = Enumerable.Range(0, 24).ToList();

        public IReadOnlyList<int> MinuteOptions { get; } = Enumerable.Range(0, 12)
            .Select(index => index * 5)
            .ToList();

        public IReadOnlyList<BalloonColorOption> BalloonColorOptions { get; } =
        [
            new("빨강", "#E74C3C"),
            new("파랑", "#3498DB"),
            new("초록", "#2ECC71"),
            new("노랑", "#F1C40F"),
            new("보라", "#9B59B6"),
            new("주황", "#E67E22"),
            new("청록", "#1ABC9C"),
            new("분홍", "#E84393"),
            new("회색", "#7F8C8D")
        ];

        public string? SavedFilePath { get; private set; }

        private void AddContestInfoButton_Click(object sender, RoutedEventArgs e)
        {
            ContestInfoItems.Add(new ContestInfoEditorItem());
        }

        private void RemoveContestInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: ContestInfoEditorItem item })
            {
                ContestInfoItems.Remove(item);
            }
        }

        private void AddProblemButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "대회 문항 추가",
                Filter = "JSON 문제 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
                Multiselect = true
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            var failedFiles = new List<string>();
            HashSet<string> existingPaths = ContestProblems
                .Select(problem => Path.GetFullPath(problem.SourceFilePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string filePath in dialog.FileNames)
            {
                try
                {
                    string fullPath = Path.GetFullPath(filePath);
                    if (existingPaths.Contains(fullPath))
                    {
                        continue;
                    }

                    ProblemDocument problem = ReadProblem(filePath);
                    ContestProblems.Add(new ContestProblemEditorItem
                    {
                        SourceFilePath = filePath,
                        ProblemId = problem.Id ?? string.Empty,
                        Title = problem.Title ?? string.Empty,
                        BalloonColor = BalloonColorOptions[ContestProblems.Count % BalloonColorOptions.Count].Hex
                    });
                    existingPaths.Add(fullPath);
                }
                catch
                {
                    failedFiles.Add(Path.GetFileName(filePath));
                }
            }

            RenumberProblemLabels();
            UpdateProblemCount();

            if (failedFiles.Count > 0)
            {
                MessageBox.Show(
                    "일부 문제 파일을 추가할 수 없습니다.\n\n" + string.Join(Environment.NewLine, failedFiles.Take(10)),
                    "대회 문항 추가",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RemoveProblemButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProblemDataGrid.SelectedItem is not ContestProblemEditorItem selectedProblem)
            {
                return;
            }

            ContestProblems.Remove(selectedProblem);
            RenumberProblemLabels();
            UpdateProblemCount();
        }

        private void MoveProblemUpButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedProblem(-1);
        }

        private void MoveProblemDownButton_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedProblem(1);
        }

        private void MoveSelectedProblem(int offset)
        {
            if (ProblemDataGrid.SelectedItem is not ContestProblemEditorItem selectedProblem)
            {
                return;
            }

            int currentIndex = ContestProblems.IndexOf(selectedProblem);
            int nextIndex = currentIndex + offset;
            if (currentIndex < 0 || nextIndex < 0 || nextIndex >= ContestProblems.Count)
            {
                return;
            }

            ContestProblems.Move(currentIndex, nextIndex);
            RenumberProblemLabels();
            ProblemDataGrid.SelectedItem = selectedProblem;
            ProblemDataGrid.ScrollIntoView(selectedProblem);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryValidate(out ContestPackageWriteRequest? request))
            {
                return;
            }

            ContestPackageWriteRequest saveRequest = request!;
            var dialog = new SaveFileDialog
            {
                Title = "대회 ZIP 저장",
                Filter = "ZIP 대회 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*",
                DefaultExt = ".zip",
                FileName = CreateDefaultContestFileName(saveRequest.Title)
            };
            ApplyInitialDirectory(dialog, _defaultSaveDirectory);

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            try
            {
                saveRequest.DestinationFilePath = dialog.FileName;
                ContestPackageWriteResult writeResult = _packageWriter.WriteZip(saveRequest);
                SavedFilePath = writeResult.FilePath;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "대회 ZIP 파일을 저장할 수 없습니다.\n\n" + ex.Message,
                    "대회 만들기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool TryValidate(out ContestPackageWriteRequest? request)
        {
            request = null;

            string title = TitleTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show(
                    "대회명을 입력해 주세요.",
                    "대회 만들기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                TitleTextBox.Focus();
                return false;
            }

            if (!TryReadDateTime(
                    StartDatePicker,
                    StartHourComboBox,
                    StartMinuteComboBox,
                    "시작",
                    out DateTimeOffset startsAt)
                || !TryReadDateTime(
                    EndDatePicker,
                    EndHourComboBox,
                    EndMinuteComboBox,
                    "종료",
                    out DateTimeOffset endsAt))
            {
                return false;
            }

            if (endsAt <= startsAt)
            {
                MessageBox.Show(
                    "대회 종료 시각은 시작 시각보다 늦어야 합니다.",
                    "대회 만들기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                EndHourComboBox.Focus();
                return false;
            }

            if (ContestProblems.Count == 0)
            {
                MessageBox.Show(
                    "대회에 포함할 문항을 1개 이상 추가해 주세요.",
                    "대회 만들기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            var problems = new List<ContestPackageProblemSource>();
            foreach (ContestProblemEditorItem item in ContestProblems)
            {
                if (!File.Exists(item.SourceFilePath))
                {
                    MessageBox.Show(
                        $"문제 파일을 찾을 수 없습니다.\n\n{item.SourceFilePath}",
                        "대회 만들기",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                problems.Add(new ContestPackageProblemSource
                {
                    Label = item.Label,
                    Title = item.Title,
                    SourceFilePath = item.SourceFilePath,
                    BalloonColor = item.BalloonColor,
                    Score = 1
                });
            }

            request = new ContestPackageWriteRequest
            {
                Title = title,
                StartsAt = startsAt,
                EndsAt = endsAt,
                AdditionalInfo = ContestInfoItems
                    .Select(item => new ContestInfoDocument
                    {
                        Label = item.Label.Trim(),
                        Text = item.Text.Trim()
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.Label)
                                   || !string.IsNullOrWhiteSpace(item.Text))
                    .ToList(),
                Problems = problems
            };
            return true;
        }

        private bool TryReadDateTime(
            System.Windows.Controls.DatePicker datePicker,
            System.Windows.Controls.ComboBox hourComboBox,
            System.Windows.Controls.ComboBox minuteComboBox,
            string label,
            out DateTimeOffset value)
        {
            value = default;
            if (datePicker.SelectedDate is null)
            {
                MessageBox.Show(
                    $"{label} 날짜를 선택해 주세요.",
                    "대회 만들기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                datePicker.Focus();
                return false;
            }

            if (hourComboBox.SelectedItem is not int hour)
            {
                MessageBox.Show(
                    $"{label} 시를 선택해 주세요.",
                    "대회 만들기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                hourComboBox.Focus();
                return false;
            }

            if (minuteComboBox.SelectedItem is not int minute)
            {
                MessageBox.Show(
                    $"{label} 분을 선택해 주세요.",
                    "대회 만들기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                minuteComboBox.Focus();
                return false;
            }

            TimeSpan time = new(hour, minute, 0);
            DateTime localDateTime = DateTime.SpecifyKind(datePicker.SelectedDate.Value.Date + time, DateTimeKind.Local);
            value = new DateTimeOffset(localDateTime);
            return true;
        }

        private static int RoundDownToFiveMinutes(int minute)
        {
            return Math.Clamp(minute / 5 * 5, 0, 55);
        }

        private ProblemDocument ReadProblem(string filePath)
        {
            string json = File.ReadAllText(filePath);
            ProblemDocument? problem = JsonSerializer.Deserialize<ProblemDocument>(json, _jsonOptions);
            if (problem is null || string.IsNullOrWhiteSpace(problem.Title))
            {
                throw new InvalidOperationException("문제 JSON을 읽을 수 없습니다.");
            }

            return problem;
        }

        private void RenumberProblemLabels()
        {
            for (int i = 0; i < ContestProblems.Count; i++)
            {
                ContestProblems[i].Label = ContestPackageReader.CreateProblemLabel(i);
            }

            ProblemDataGrid.Items.Refresh();
        }

        private void UpdateProblemCount()
        {
            ProblemCountTextBlock.Text = $"문항 {ContestProblems.Count}개";
        }

        private static void ApplyInitialDirectory(FileDialog dialog, string? directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            try
            {
                if (Directory.Exists(directoryPath))
                {
                    dialog.InitialDirectory = directoryPath;
                }
            }
            catch
            {
                // Ignore invalid default paths.
            }
        }

        private static string CreateDefaultContestFileName(string title)
        {
            string baseName = Regex.Replace(title, @"[\\/:*?""<>|]+", "_").Trim();
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "contest";
            }

            return baseName + ".zip";
        }
    }

    public sealed class ContestProblemEditorItem : INotifyPropertyChanged
    {
        private string _label = string.Empty;
        private string _balloonColor = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Label
        {
            get => _label;
            set
            {
                if (_label == value)
                {
                    return;
                }

                _label = value;
                OnPropertyChanged(nameof(Label));
            }
        }

        public string ProblemId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SourceFilePath { get; set; } = string.Empty;
        public string DisplayName => string.IsNullOrWhiteSpace(ProblemId)
            ? Title
            : $"[{ProblemId}] {Title}";

        public string BalloonColor
        {
            get => _balloonColor;
            set
            {
                if (_balloonColor == value)
                {
                    return;
                }

                _balloonColor = value;
                OnPropertyChanged(nameof(BalloonColor));
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ContestInfoEditorItem : INotifyPropertyChanged
    {
        private string _label = string.Empty;
        private string _text = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Label
        {
            get => _label;
            set
            {
                if (_label == value)
                {
                    return;
                }

                _label = value;
                OnPropertyChanged(nameof(Label));
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                if (_text == value)
                {
                    return;
                }

                _text = value;
                OnPropertyChanged(nameof(Text));
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class BalloonColorOption
    {
        public BalloonColorOption(string name, string hex)
        {
            Name = name;
            Hex = hex;
            Brush = CreateBrush(hex);
        }

        public string Name { get; }
        public string Hex { get; }
        public Brush Brush { get; }

        private static Brush CreateBrush(string hex)
        {
            if (ColorConverter.ConvertFromString(hex) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }

            return Brushes.Gray;
        }
    }
}
