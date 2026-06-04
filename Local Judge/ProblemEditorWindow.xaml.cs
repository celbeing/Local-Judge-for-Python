using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Local_Judge
{
    public partial class ProblemEditorWindow : Window
    {
        private const string ProblemViewerHostName = "localjudge.problem-viewer";
        private const string ProblemAssetsHostName = "localjudge.problem-assets";

        private readonly List<SampleEditorControls> _sampleEditors = new();
        private readonly List<ProblemAssetDocument> _assets = new();
        private readonly bool _isNewProblem;
        private readonly string? _sourceProblemFilePath;
        private readonly string? _defaultProblemSaveDirectory;
        private readonly string _assetWorkspacePath;
        private readonly DispatcherTimer _previewRefreshTimer;
        private List<TestCaseDocument> _testCases = new();
        private TextBox? _lastStatementTextBox;
        private bool _isPreviewReady;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public ProblemDocument Problem { get; private set; }
        public string? SavedFilePath { get; private set; }
        public string AssetWorkspacePath => _assetWorkspacePath;

        public ProblemEditorWindow(
            ProblemDocument? problem = null,
            string? problemFilePath = null,
            string? defaultProblemSaveDirectory = null)
        {
            _isNewProblem = problem is null;
            _sourceProblemFilePath = problemFilePath;
            _defaultProblemSaveDirectory = defaultProblemSaveDirectory;
            _assetWorkspacePath = Path.Combine(
                Path.GetTempPath(),
                "LocalJudge",
                "ProblemEditorAssets",
                Guid.NewGuid().ToString("N"));
            _previewRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _previewRefreshTimer.Tick += (_, _) =>
            {
                _previewRefreshTimer.Stop();
                RefreshPreview();
            };

            InitializeComponent();

            Directory.CreateDirectory(_assetWorkspacePath);

            Problem = CloneProblem(problem ?? CreateDefaultProblem());
            _assets.AddRange(ProblemAssetUtilities.CloneAssets(Problem.Assets));
            CopyExistingAssetsToWorkspace();

            LoadProblemToForm(Problem);
            ConfigureAttributionFields();
            WirePreviewRefreshEvents();

            Loaded += async (_, _) => await InitializePreviewAsync();
        }

        private static ProblemDocument CreateDefaultProblem()
        {
            return new ProblemDocument
            {
                Version = ProblemAssetUtilities.CurrentProblemVersion,
                StatementFormat = ProblemStatementFormats.MarkdownLatex,
                TimeLimitMs = 2000,
                MemoryLimitMb = 128,
                Assets = new(),
                Samples = new(),
                TestCases = new()
            };
        }

        private static ProblemDocument CloneProblem(ProblemDocument source)
        {
            bool defaultToMarkdownLatex = source.Version >= ProblemAssetUtilities.CurrentProblemVersion;
            string statementFormat = ProblemAssetUtilities.NormalizeStatementFormat(
                source.StatementFormat,
                defaultToMarkdownLatex);

            return new ProblemDocument
            {
                Version = source.Version <= 0 ? 3 : Math.Max(source.Version, 3),
                Id = source.Id ?? string.Empty,
                Title = source.Title ?? string.Empty,
                AuthorName = source.AuthorName ?? string.Empty,
                Source = source.Source ?? string.Empty,
                TimeLimitMs = source.TimeLimitMs <= 0 ? 2000 : source.TimeLimitMs,
                MemoryLimitMb = source.MemoryLimitMb <= 0 ? 128 : source.MemoryLimitMb,
                StatementFormat = statementFormat,
                Description = source.Description ?? string.Empty,
                InputFormat = source.InputFormat ?? string.Empty,
                OutputFormat = source.OutputFormat ?? string.Empty,
                Assets = ProblemAssetUtilities.CloneAssets(source.Assets),
                Samples = CloneSamples(source.Samples),
                TestCases = CloneTestCases(source.TestCases)
            };
        }

        private void CopyExistingAssetsToWorkspace()
        {
            if (string.IsNullOrWhiteSpace(_sourceProblemFilePath))
            {
                return;
            }

            string sourceAssetFolderPath = ProblemAssetUtilities.GetAssetFolderPath(_sourceProblemFilePath);
            ProblemAssetUtilities.CopyAssetFolder(sourceAssetFolderPath, _assetWorkspacePath);
        }

        private async System.Threading.Tasks.Task InitializePreviewAsync()
        {
            try
            {
                await ProblemPreviewWebView.EnsureCoreWebView2Async();

                string viewerFolderPath = Path.Combine(AppContext.BaseDirectory, "ProblemViewer");
                if (!Directory.Exists(viewerFolderPath))
                {
                    MessageBox.Show(
                        $"문제 미리보기 리소스를 찾을 수 없습니다.\n\n{viewerFolderPath}",
                        "미리보기 초기화 실패",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                ProblemPreviewWebView.CoreWebView2.WebMessageReceived += ProblemPreviewWebView_WebMessageReceived;
                ProblemPreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    ProblemViewerHostName,
                    viewerFolderPath,
                    CoreWebView2HostResourceAccessKind.Allow);
                ProblemPreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    ProblemAssetsHostName,
                    _assetWorkspacePath,
                    CoreWebView2HostResourceAccessKind.Allow);

                ProblemPreviewWebView.Source = new Uri($"https://{ProblemViewerHostName}/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"문제 미리보기 초기화 중 오류가 발생했습니다.\n\n{ex.Message}",
                    "미리보기 초기화 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ProblemPreviewWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("type", out JsonElement typeElement)
                    && string.Equals(typeElement.GetString(), "viewerReady", StringComparison.Ordinal))
                {
                    _isPreviewReady = true;
                    RefreshPreview();
                }
            }
            catch
            {
                // Preview messages are non-critical; leave the editor usable.
            }
        }

        private void LoadProblemToForm(ProblemDocument problem)
        {
            ProblemIdTextBox.Text = problem.Id;
            TitleTextBox.Text = problem.Title;
            AuthorNameTextBox.Text = problem.AuthorName;
            SourceTextBox.Text = problem.Source;
            TimeLimitTextBox.Text = problem.TimeLimitMs.ToString();
            MemoryLimitTextBox.Text = problem.MemoryLimitMb.ToString();
            DescriptionTextBox.Text = problem.Description;
            InputDescriptionTextBox.Text = problem.InputFormat;
            OutputDescriptionTextBox.Text = problem.OutputFormat;
            _lastStatementTextBox = DescriptionTextBox;

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

        private void ConfigureAttributionFields()
        {
            bool isReadOnly = !_isNewProblem;
            AuthorNameTextBox.IsReadOnly = isReadOnly;
            SourceTextBox.IsReadOnly = isReadOnly;

            if (isReadOnly)
            {
                AuthorNameTextBox.Background = (Brush)FindResource("PanelHeaderBrush");
                SourceTextBox.Background = (Brush)FindResource("PanelHeaderBrush");
                AttributionNoticeTextBlock.Text = "불러온 문항의 제작자 및 출처는 수정할 수 없습니다.";
            }
            else
            {
                AttributionNoticeTextBlock.Text = "문항 제작자 및 출처는 이후 변경할 수 없습니다.";
            }
        }

        private void WirePreviewRefreshEvents()
        {
            TextBox[] textBoxes =
            {
                ProblemIdTextBox,
                TitleTextBox,
                AuthorNameTextBox,
                SourceTextBox,
                TimeLimitTextBox,
                MemoryLimitTextBox,
                DescriptionTextBox,
                InputDescriptionTextBox,
                OutputDescriptionTextBox
            };

            foreach (TextBox textBox in textBoxes)
            {
                textBox.TextChanged += (_, _) => RequestPreviewRefresh();
            }

            DescriptionTextBox.GotKeyboardFocus += (_, _) => _lastStatementTextBox = DescriptionTextBox;
            InputDescriptionTextBox.GotKeyboardFocus += (_, _) => _lastStatementTextBox = InputDescriptionTextBox;
            OutputDescriptionTextBox.GotKeyboardFocus += (_, _) => _lastStatementTextBox = OutputDescriptionTextBox;
        }

        private void RequestPreviewRefresh()
        {
            if (!_isPreviewReady)
            {
                return;
            }

            _previewRefreshTimer.Stop();
            _previewRefreshTimer.Start();
        }

        private void RefreshPreview()
        {
            if (!_isPreviewReady || ProblemPreviewWebView.CoreWebView2 is null)
            {
                return;
            }

            string json = JsonSerializer.Serialize(new
            {
                type = "renderProblem",
                assetBaseUrl = $"https://{ProblemAssetsHostName}/",
                problem = BuildPreviewPayload()
            }, _jsonOptions);

            ProblemPreviewWebView.CoreWebView2.PostWebMessageAsJson(json);
        }

        private object BuildPreviewPayload()
        {
            _ = int.TryParse(TimeLimitTextBox.Text.Trim(), out int timeLimitMs);
            _ = int.TryParse(MemoryLimitTextBox.Text.Trim(), out int memoryLimitMb);

            return new
            {
                emptyState = false,
                id = ProblemIdTextBox.Text.Trim(),
                title = string.IsNullOrWhiteSpace(TitleTextBox.Text) ? "제목 없는 문제" : TitleTextBox.Text.Trim(),
                authorName = _isNewProblem ? AuthorNameTextBox.Text.Trim() : Problem.AuthorName,
                source = _isNewProblem ? SourceTextBox.Text.Trim() : Problem.Source,
                timeLimitMs,
                memoryLimitMb,
                statementFormat = ProblemStatementFormats.MarkdownLatex,
                description = DescriptionTextBox.Text,
                inputFormat = InputDescriptionTextBox.Text,
                outputFormat = OutputDescriptionTextBox.Text,
                samples = CollectSamplesForPreview(),
                testCaseCount = _testCases.Count
            };
        }

        private List<SampleCaseDocument> CollectSamplesForPreview()
        {
            return _sampleEditors
                .Select(editor => new SampleCaseDocument
                {
                    Input = editor.InputTextBox.Text,
                    Output = editor.OutputTextBox.Text
                })
                .Where(sample => !string.IsNullOrWhiteSpace(sample.Input) || !string.IsNullOrWhiteSpace(sample.Output))
                .ToList();
        }

        private void AddSampleButton_Click(object sender, RoutedEventArgs e)
        {
            AddSampleEditor(new SampleCaseDocument());
            RequestPreviewRefresh();
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

            inputTextBox.TextChanged += (_, _) => RequestPreviewRefresh();
            outputTextBox.TextChanged += (_, _) => RequestPreviewRefresh();

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
            RequestPreviewRefresh();
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

        private void InsertImageButton_Click(object sender, RoutedEventArgs e)
        {
            TextBox targetTextBox = ResolveImageInsertTarget(sender);

            var dialog = new OpenFileDialog
            {
                Title = "문항 이미지 삽입",
                Filter = "이미지 파일 (*.png;*.jpg;*.jpeg;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.webp|모든 파일 (*.*)|*.*"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            if (!ProblemAssetUtilities.IsSupportedImageFile(dialog.FileName))
            {
                MessageBox.Show(
                    "지원하지 않는 이미지 형식입니다. png, jpg, jpeg, gif, webp 파일을 선택하세요.",
                    "이미지 형식 확인",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                Directory.CreateDirectory(_assetWorkspacePath);
                HashSet<string> reservedNames = _assets
                    .Select(asset => asset.FileName)
                    .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (string filePath in Directory.EnumerateFiles(_assetWorkspacePath))
                {
                    reservedNames.Add(Path.GetFileName(filePath));
                }

                string fileName = ProblemAssetUtilities.CreateSafeAssetFileName(dialog.FileName, reservedNames);
                string targetFilePath = Path.Combine(_assetWorkspacePath, fileName);
                File.Copy(dialog.FileName, targetFilePath, overwrite: false);

                string relativePath = ProblemAssetUtilities.ToMarkdownAssetPath(fileName);
                _assets.Add(new ProblemAssetDocument
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FileName = fileName,
                    RelativePath = relativePath,
                    ContentType = ProblemAssetUtilities.GetContentType(fileName)
                });

                string altText = Path.GetFileNameWithoutExtension(fileName);
                InsertTextAtCaret(targetTextBox, $"{Environment.NewLine}![{altText}]({relativePath}){Environment.NewLine}");
                RequestPreviewRefresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"이미지 삽입 중 오류가 발생했습니다.\n\n{ex.Message}",
                    "이미지 삽입 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private TextBox ResolveImageInsertTarget(object sender)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                return tag switch
                {
                    "input" => InputDescriptionTextBox,
                    "output" => OutputDescriptionTextBox,
                    _ => DescriptionTextBox
                };
            }

            return _lastStatementTextBox ?? DescriptionTextBox;
        }

        private static void InsertTextAtCaret(TextBox textBox, string text)
        {
            int start = textBox.SelectionStart;
            string currentText = textBox.Text;
            textBox.Text = currentText.Remove(start, textBox.SelectionLength).Insert(start, text);
            textBox.SelectionStart = start + text.Length;
            textBox.SelectionLength = 0;
            textBox.Focus();
        }

        private void CleanupUnusedAssetsButton_Click(object sender, RoutedEventArgs e)
        {
            HashSet<string> usedAssetPaths = CollectReferencedAssetPaths();
            int removedCount = 0;

            for (int i = _assets.Count - 1; i >= 0; i--)
            {
                ProblemAssetDocument asset = _assets[i];
                if (!usedAssetPaths.Contains(asset.RelativePath))
                {
                    _assets.RemoveAt(i);
                    removedCount++;
                }
            }

            foreach (string filePath in Directory.EnumerateFiles(_assetWorkspacePath))
            {
                string relativePath = ProblemAssetUtilities.ToMarkdownAssetPath(Path.GetFileName(filePath));
                if (!usedAssetPaths.Contains(relativePath))
                {
                    try
                    {
                        File.Delete(filePath);
                        removedCount++;
                    }
                    catch
                    {
                        // A locked temp image should not block editing.
                    }
                }
            }

            MessageBox.Show(
                removedCount == 0
                    ? "정리할 이미지가 없습니다."
                    : $"사용하지 않는 이미지 {removedCount}개를 정리했습니다.",
                "이미지 정리",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RequestPreviewRefresh();
        }

        private HashSet<string> CollectReferencedAssetPaths()
        {
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string text = string.Join(
                Environment.NewLine,
                DescriptionTextBox.Text,
                InputDescriptionTextBox.Text,
                OutputDescriptionTextBox.Text);

            foreach (Match match in Regex.Matches(text, @"!\[[^\]]*\]\((assets/[^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.IgnoreCase))
            {
                references.Add(match.Groups[1].Value.Replace('\\', '/'));
            }

            return references;
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
                RequestPreviewRefresh();

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

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string id = ProblemIdTextBox.Text.Trim();
            string title = TitleTextBox.Text.Trim();
            string authorName = _isNewProblem ? AuthorNameTextBox.Text.Trim() : Problem.AuthorName;
            string source = _isNewProblem ? SourceTextBox.Text.Trim() : Problem.Source;

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

            if (_isNewProblem && string.IsNullOrWhiteSpace(authorName))
            {
                MessageBox.Show("문항 제작자를 입력하세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                AuthorNameTextBox.Focus();
                return;
            }

            if (_isNewProblem && string.IsNullOrWhiteSpace(source))
            {
                MessageBox.Show("문항 출처를 입력하세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                SourceTextBox.Focus();
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
                Version = ProblemAssetUtilities.CurrentProblemVersion,
                Id = id,
                Title = title,
                AuthorName = authorName,
                Source = source,
                TimeLimitMs = timeLimitMs,
                MemoryLimitMb = memoryLimitMb,
                StatementFormat = ProblemStatementFormats.MarkdownLatex,
                Description = DescriptionTextBox.Text,
                InputFormat = InputDescriptionTextBox.Text,
                OutputFormat = OutputDescriptionTextBox.Text,
                Assets = ProblemAssetUtilities.CloneAssets(_assets),
                Samples = samples,
                TestCases = CloneTestCases(_testCases)
            };

            if (_isNewProblem && !SaveNewProblemWithDialog())
            {
                return;
            }

            DialogResult = true;
        }

        private bool SaveNewProblemWithDialog()
        {
            var dialog = new SaveFileDialog
            {
                Title = "문제 JSON 저장",
                Filter = "JSON 문제 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
                FileName = CreateDefaultProblemFileName(Problem)
            };
            ApplyInitialDirectory(dialog, _defaultProblemSaveDirectory);

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return false;
            }

            try
            {
                string json = JsonSerializer.Serialize(Problem, _jsonOptions);
                File.WriteAllText(dialog.FileName, json);

                if (Directory.EnumerateFiles(_assetWorkspacePath).Any())
                {
                    string targetAssetFolderPath = ProblemAssetUtilities.GetAssetFolderPath(dialog.FileName);
                    ProblemAssetUtilities.CopyAssetFolder(_assetWorkspacePath, targetAssetFolderPath);
                }

                SavedFilePath = dialog.FileName;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"문제 JSON 저장 중 오류가 발생했습니다.\n\n{ex.Message}",
                    "저장 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        private static void ApplyInitialDirectory(SaveFileDialog dialog, string? directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(directoryPath);
                Directory.CreateDirectory(fullPath);
                dialog.InitialDirectory = fullPath;
            }
            catch
            {
                // Invalid user settings should not block saving a new problem.
            }
        }

        private static string CreateDefaultProblemFileName(ProblemDocument problem)
        {
            string baseName = string.IsNullOrWhiteSpace(problem.Id)
                ? problem.Title
                : $"{problem.Id}_{problem.Title}";

            baseName = Regex.Replace(baseName, @"[\\/:*?""<>|]+", "_").Trim();

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "problem";
            }

            return baseName + ".json";
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
                        $"예제 {i + 1}의 입력과 출력은 함께 입력하거나 함께 비워야 합니다.",
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
