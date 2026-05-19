using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Local_Judge
{
    public partial class ProblemEditorWindow : Window
    {
        private readonly List<SampleEditorControls> _sampleEditors = new();
        private List<TestCaseDocument> _testCases = new();

        public ProblemDocument Problem { get; private set; }

        public ProblemEditorWindow(ProblemDocument? problem = null)
        {
            InitializeComponent();

            Problem = CloneProblem(problem ?? CreateDefaultProblem());
            LoadProblemToForm(Problem);
        }

        private static ProblemDocument CreateDefaultProblem()
        {
            return new ProblemDocument
            {
                Version = 2,
                TimeLimitMs = 2000,
                MemoryLimitMb = 128,
                Samples = new(),
                TestCases = new()
            };
        }

        private static ProblemDocument CloneProblem(ProblemDocument source)
        {
            return new ProblemDocument
            {
                Version = source.Version <= 0 ? 2 : source.Version,
                Id = source.Id,
                Title = source.Title,
                TimeLimitMs = source.TimeLimitMs <= 0 ? 2000 : source.TimeLimitMs,
                MemoryLimitMb = source.MemoryLimitMb <= 0 ? 128 : source.MemoryLimitMb,
                Description = source.Description,
                InputFormat = source.InputFormat,
                OutputFormat = source.OutputFormat,
                Samples = CloneSamples(source.Samples),
                TestCases = CloneTestCases(source.TestCases)
            };
        }

        private void LoadProblemToForm(ProblemDocument problem)
        {
            ProblemIdTextBox.Text = problem.Id;
            TitleTextBox.Text = problem.Title;
            TimeLimitTextBox.Text = problem.TimeLimitMs.ToString();
            MemoryLimitTextBox.Text = problem.MemoryLimitMb.ToString();
            DescriptionTextBox.Text = problem.Description;
            InputDescriptionTextBox.Text = problem.InputFormat;
            OutputDescriptionTextBox.Text = problem.OutputFormat;

            _sampleEditors.Clear();
            SamplesPanel.Children.Clear();

            List<SampleCaseDocument> samples = CloneSamples(problem.Samples);
            if (samples.Count == 0)
            {
                AddSampleEditor(new SampleCaseDocument());
            }
            else
            {
                foreach (SampleCaseDocument sample in samples)
                {
                    AddSampleEditor(sample);
                }
            }

            _testCases = CloneTestCases(problem.TestCases);
            UpdateTestCaseSummary();
        }

        private void AddSampleButton_Click(object sender, RoutedEventArgs e)
        {
            AddSampleEditor(new SampleCaseDocument());
        }

        private void AddSampleEditor(SampleCaseDocument sample)
        {
            var container = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 16)
            };

            var inputHeader = new DockPanel
            {
                LastChildFill = true
            };

            var inputLabel = new TextBlock
            {
                Style = (Style)FindResource("LabelTextStyle")
            };

            var deleteButton = new Button
            {
                Content = "삭제",
                Width = 58,
                Height = 26,
                Margin = new Thickness(8, 10, 0, 6)
            };

            DockPanel.SetDock(deleteButton, Dock.Right);
            inputHeader.Children.Add(deleteButton);
            inputHeader.Children.Add(inputLabel);

            TextBox inputTextBox = CreateMultilineEditor(sample.Input);

            var outputLabel = new TextBlock
            {
                Style = (Style)FindResource("LabelTextStyle")
            };

            TextBox outputTextBox = CreateMultilineEditor(sample.Output);

            container.Children.Add(inputHeader);
            container.Children.Add(inputTextBox);
            container.Children.Add(outputLabel);
            container.Children.Add(outputTextBox);
            SamplesPanel.Children.Add(container);

            var editor = new SampleEditorControls(
                container,
                inputLabel,
                outputLabel,
                inputTextBox,
                outputTextBox,
                deleteButton);

            deleteButton.Click += (_, _) => RemoveSampleEditor(editor);

            _sampleEditors.Add(editor);
            RefreshSampleEditorLabels();
        }

        private TextBox CreateMultilineEditor(string text)
        {
            return new TextBox
            {
                Style = (Style)FindResource("MultilineInputStyle"),
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.NoWrap,
                MinHeight = 90,
                Text = text
            };
        }

        private void RemoveSampleEditor(SampleEditorControls editor)
        {
            int index = _sampleEditors.IndexOf(editor);
            if (index <= 0)
            {
                return;
            }

            SamplesPanel.Children.Remove(editor.Container);
            _sampleEditors.RemoveAt(index);
            RefreshSampleEditorLabels();
        }

        private void RefreshSampleEditorLabels()
        {
            for (int i = 0; i < _sampleEditors.Count; i++)
            {
                SampleEditorControls editor = _sampleEditors[i];
                int sampleNumber = i + 1;

                editor.InputLabel.Text = $"예제 입력 {sampleNumber}";
                editor.OutputLabel.Text = $"예제 출력 {sampleNumber}";
                editor.DeleteButton.Visibility = i == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void LoadTestCasesZipButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "채점 테스트 ZIP 불러오기",
                Filter = "ZIP 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            try
            {
                _testCases = LoadTestCasesFromZip(dialog.FileName);
                UpdateTestCaseSummary(Path.GetFileName(dialog.FileName));

                MessageBox.Show(
                    $"채점 테스트 {_testCases.Count}개를 등록했습니다.",
                    "채점 테스트 등록",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "채점 테스트 ZIP 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static List<TestCaseDocument> LoadTestCasesFromZip(string filePath)
        {
            using ZipArchive archive = ZipFile.OpenRead(filePath);

            var inputEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            var outputEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                string extension = Path.GetExtension(entry.Name);
                if (!string.Equals(extension, ".in", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".out", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string caseName = Path.GetFileNameWithoutExtension(entry.Name);
                if (string.IsNullOrWhiteSpace(caseName))
                {
                    throw new InvalidOperationException("이름이 비어 있는 .in 또는 .out 파일은 사용할 수 없습니다.");
                }

                Dictionary<string, ZipArchiveEntry> target = string.Equals(extension, ".in", StringComparison.OrdinalIgnoreCase)
                    ? inputEntries
                    : outputEntries;

                if (target.ContainsKey(caseName))
                {
                    throw new InvalidOperationException($"중복된 테스트 파일 이름이 있습니다: {caseName}{extension}");
                }

                target.Add(caseName, entry);
            }

            if (inputEntries.Count == 0 && outputEntries.Count == 0)
            {
                throw new InvalidOperationException("ZIP 안에서 .in 또는 .out 파일을 찾지 못했습니다.");
            }

            List<string> unmatchedFiles = FindUnmatchedTestCaseFiles(inputEntries, outputEntries);
            if (unmatchedFiles.Count > 0)
            {
                string details = string.Join(Environment.NewLine, unmatchedFiles.Take(12));
                if (unmatchedFiles.Count > 12)
                {
                    details += $"{Environment.NewLine}... 외 {unmatchedFiles.Count - 12}개";
                }

                throw new InvalidOperationException(
                    "입력/출력 쌍이 맞지 않는 테스트 파일이 있습니다."
                    + Environment.NewLine
                    + details);
            }

            return inputEntries.Keys
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new TestCaseDocument
                {
                    Input = ReadZipEntryText(inputEntries[name]),
                    Output = ReadZipEntryText(outputEntries[name])
                })
                .ToList();
        }

        private static List<string> FindUnmatchedTestCaseFiles(
            Dictionary<string, ZipArchiveEntry> inputEntries,
            Dictionary<string, ZipArchiveEntry> outputEntries)
        {
            var unmatchedFiles = new List<string>();

            unmatchedFiles.AddRange(
                inputEntries.Keys
                    .Except(outputEntries.Keys, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Select(name => $"{name}.in 파일에 대응하는 {name}.out 파일이 없습니다."));

            unmatchedFiles.AddRange(
                outputEntries.Keys
                    .Except(inputEntries.Keys, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Select(name => $"{name}.out 파일에 대응하는 {name}.in 파일이 없습니다."));

            return unmatchedFiles;
        }

        private static string ReadZipEntryText(ZipArchiveEntry entry)
        {
            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private void UpdateTestCaseSummary(string? sourceFileName = null)
        {
            string sourceText = string.IsNullOrWhiteSpace(sourceFileName)
                ? string.Empty
                : $" ({sourceFileName})";

            TestCaseSummaryTextBlock.Text = $"채점 테스트 {_testCases.Count}개{sourceText}";
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            string id = ProblemIdTextBox.Text.Trim();
            string title = TitleTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(id))
            {
                MessageBox.Show("문제 번호 / ID를 입력하세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                ProblemIdTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("문제 제목을 입력하세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                TitleTextBox.Focus();
                return;
            }

            if (!int.TryParse(TimeLimitTextBox.Text.Trim(), out int timeLimitMs) || timeLimitMs <= 0)
            {
                MessageBox.Show("시간 제한은 1 이상의 정수로 입력하세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                TimeLimitTextBox.Focus();
                return;
            }

            if (!int.TryParse(MemoryLimitTextBox.Text.Trim(), out int memoryLimitMb) || memoryLimitMb <= 0)
            {
                MessageBox.Show("메모리 제한은 1 이상의 정수로 입력하세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                MemoryLimitTextBox.Focus();
                return;
            }

            List<SampleCaseDocument>? samples = TryCollectSamplesFromForm();
            if (samples is null)
            {
                return;
            }

            Problem = new ProblemDocument
            {
                Version = 2,
                Id = id,
                Title = title,
                TimeLimitMs = timeLimitMs,
                MemoryLimitMb = memoryLimitMb,
                Description = DescriptionTextBox.Text,
                InputFormat = InputDescriptionTextBox.Text,
                OutputFormat = OutputDescriptionTextBox.Text,
                Samples = samples,
                TestCases = CloneTestCases(_testCases)
            };

            DialogResult = true;
        }

        private List<SampleCaseDocument>? TryCollectSamplesFromForm()
        {
            var samples = new List<SampleCaseDocument>();

            for (int i = 0; i < _sampleEditors.Count; i++)
            {
                SampleEditorControls editor = _sampleEditors[i];
                string input = editor.InputTextBox.Text;
                string output = editor.OutputTextBox.Text;
                bool hasInput = !string.IsNullOrWhiteSpace(input);
                bool hasOutput = !string.IsNullOrWhiteSpace(output);

                if (hasInput != hasOutput)
                {
                    MessageBox.Show(
                        $"예제 {i + 1}의 입력과 출력은 함께 입력하거나 둘 다 비워두세요.",
                        "입력 확인",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    if (!hasInput)
                    {
                        editor.InputTextBox.Focus();
                    }
                    else
                    {
                        editor.OutputTextBox.Focus();
                    }

                    return null;
                }

                if (hasInput && hasOutput)
                {
                    samples.Add(new SampleCaseDocument
                    {
                        Input = input,
                        Output = output
                    });
                }
            }

            return samples;
        }

        private static List<SampleCaseDocument> CloneSamples(IEnumerable<SampleCaseDocument>? samples)
        {
            return (samples ?? Enumerable.Empty<SampleCaseDocument>())
                .Select(sample => new SampleCaseDocument
                {
                    Input = sample.Input,
                    Output = sample.Output
                })
                .ToList();
        }

        private static List<TestCaseDocument> CloneTestCases(IEnumerable<TestCaseDocument>? testCases)
        {
            return (testCases ?? Enumerable.Empty<TestCaseDocument>())
                .Select(testCase => new TestCaseDocument
                {
                    Input = testCase.Input,
                    Output = testCase.Output
                })
                .ToList();
        }

        private sealed record SampleEditorControls(
            StackPanel Container,
            TextBlock InputLabel,
            TextBlock OutputLabel,
            TextBox InputTextBox,
            TextBox OutputTextBox,
            Button DeleteButton);
    }
}
