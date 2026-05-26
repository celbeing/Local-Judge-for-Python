using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Local_Judge
{
    public partial class MainWindow : Window
    {
        private string _latestEditorCode = "";
        private TaskCompletionSource<string>? _editorCodeRequest;
        private string? _editorCodeRequestId;

        private readonly PythonRunner _pythonRunner = new();
        private readonly JudgeEnvironmentBenchmark _environmentBenchmark;
        private readonly SubmissionHistoryStore _submissionHistoryStore;
        private readonly SubmissionHistoryExporter _submissionHistoryExporter;
        private const int OutputLimitBytes = PythonExecutionLimits.DefaultOutputLimitBytes;

        private readonly Brush _readyBrush = new SolidColorBrush(Color.FromRgb(45, 164, 78));
        private readonly Brush _workingBrush = new SolidColorBrush(Color.FromRgb(251, 188, 5));
        private readonly Brush _errorBrush = new SolidColorBrush(Color.FromRgb(218, 54, 51));
        private readonly Brush _terminalSuccessBrush = new SolidColorBrush(Color.FromRgb(63, 185, 80));
        private readonly Brush _terminalFailBrush = new SolidColorBrush(Color.FromRgb(248, 81, 73));
        private readonly Brush _terminalWrongAnswerBrush = new SolidColorBrush(Color.FromRgb(210, 153, 34));
        private readonly Brush _terminalTimeLimitBrush = new SolidColorBrush(Color.FromRgb(163, 113, 247));
        private readonly Brush _terminalMemoryLimitBrush = new SolidColorBrush(Color.FromRgb(88, 166, 255));
        private readonly Brush _terminalRuntimeErrorBrush = new SolidColorBrush(Color.FromRgb(255, 123, 114));
        private readonly Brush _terminalOutputLimitBrush = new SolidColorBrush(Color.FromRgb(240, 136, 62));
        private static readonly Regex TerminalResultTokenRegex = new(@"\b(PASS|FAIL|AC|WA|TLE|MLE|RE|OLE)\b", RegexOptions.Compiled);

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private ProblemDocument? _currentProblem;
        private string? _currentProblemFilePath;
        private bool _isProblemDirty;
        private JudgeEnvironmentBenchmarkResult? _benchmarkResult;
        private bool _isBenchmarkRunning;
        private bool _terminalEndsWithLineBreak = true;

        public MainWindow()
        {
            _environmentBenchmark = new JudgeEnvironmentBenchmark(_pythonRunner);
            _submissionHistoryStore = new SubmissionHistoryStore(_jsonOptions);
            _submissionHistoryExporter = new SubmissionHistoryExporter(_submissionHistoryStore, _jsonOptions);

            InitializeComponent();

            SetStatus("채점 대기");
            ResetProblemView();
            AppendTerminal("[UI] 화면 구성이 완료되었습니다.");

            _ = InitializeCodeEditorAsync();
            _ = StartEnvironmentBenchmarkAsync(isManual: false);
        }

        private async Task InitializeCodeEditorAsync()
        {
            try
            {
                await CodeEditorWebView.EnsureCoreWebView2Async();

                string editorFolderPath = Path.Combine(AppContext.BaseDirectory, "Editor");

                if (!Directory.Exists(editorFolderPath))
                {
                    AppendTerminal($"[Editor] Editor 폴더를 찾을 수 없습니다: {editorFolderPath}");
                    return;
                }

                CodeEditorWebView.CoreWebView2.WebMessageReceived += CodeEditorWebView_WebMessageReceived;

                CodeEditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "localjudge.editor",
                    editorFolderPath,
                    CoreWebView2HostResourceAccessKind.Allow);

                CodeEditorWebView.Source = new Uri("https://localjudge.editor/index.html");
            }
            catch (Exception ex)
            {
                AppendTerminal("[Editor] WebView2 코드 편집기 초기화 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private void CodeEditorWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.WebMessageAsJson;

                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;

                if (!root.TryGetProperty("type", out JsonElement typeElement))
                {
                    return;
                }

                string? type = typeElement.GetString();

                switch (type)
                {
                    case "editorReady":
                        AppendTerminal("[Editor] Monaco Editor가 준비되었습니다.");
                        break;

                    case "codeChanged":
                        if (root.TryGetProperty("code", out JsonElement codeElement))
                        {
                            _latestEditorCode = codeElement.GetString() ?? "";
                        }
                        break;

                    case "currentCode":
                        if (root.TryGetProperty("code", out JsonElement currentCodeElement))
                        {
                            string code = currentCodeElement.GetString() ?? "";
                            string? responseRequestId = root.TryGetProperty("requestId", out JsonElement requestIdElement)
                                ? requestIdElement.GetString()
                                : null;

                            _latestEditorCode = code;

                            TaskCompletionSource<string>? pendingRequest = _editorCodeRequest;
                            if (pendingRequest is not null
                                && (string.IsNullOrEmpty(_editorCodeRequestId)
                                    || string.Equals(responseRequestId, _editorCodeRequestId, StringComparison.Ordinal)))
                            {
                                _editorCodeRequest = null;
                                _editorCodeRequestId = null;
                                pendingRequest.TrySetResult(code);
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                AppendTerminal("[Editor] WebView2 메시지 처리 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private async Task<string> GetEditorCodeAsync()
        {
            if (CodeEditorWebView.CoreWebView2 == null)
            {
                return _latestEditorCode;
            }

            string requestId = Guid.NewGuid().ToString("N");
            var pendingRequest = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            _editorCodeRequest = pendingRequest;
            _editorCodeRequestId = requestId;

            string script = JsonSerializer.Serialize(new
            {
                type = "getCode",
                requestId
            });

            CodeEditorWebView.CoreWebView2.PostWebMessageAsJson(script);

            Task completedTask = await Task.WhenAny(
                pendingRequest.Task,
                Task.Delay(1500));

            if (completedTask == pendingRequest.Task)
            {
                return await pendingRequest.Task;
            }

            if (ReferenceEquals(_editorCodeRequest, pendingRequest))
            {
                _editorCodeRequest = null;
                _editorCodeRequestId = null;
            }

            return _latestEditorCode;
        }

        private void SetEditorCode(string code)
        {
            _latestEditorCode = code;

            if (CodeEditorWebView.CoreWebView2 == null)
            {
                return;
            }

            string script = JsonSerializer.Serialize(new
            {
                type = "setCode",
                code
            });

            CodeEditorWebView.CoreWebView2.PostWebMessageAsJson(script);
        }

        private void SetStatus(string message, bool isWorking = false, bool isError = false)
        {
            StatusTextBlock.Text = message;

            if (isError)
            {
                StatusIndicator.Fill = _errorBrush;
            }
            else if (isWorking)
            {
                StatusIndicator.Fill = _workingBrush;
            }
            else
            {
                StatusIndicator.Fill = _readyBrush;
            }
        }

        private void AppendTerminal(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendTerminal(message));
                return;
            }

            if (!_terminalEndsWithLineBreak)
            {
                AppendTerminalText("\n", colorizeResultTokens: false);
            }

            AppendTerminalText((message ?? string.Empty) + "\n", colorizeResultTokens: true);
            TerminalTextBox.ScrollToEnd();
        }

        private void AppendTerminalRaw(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendTerminalRaw(text));
                return;
            }

            AppendTerminalText(text, colorizeResultTokens: false);
            TerminalTextBox.ScrollToEnd();
        }

        private void AppendTerminalText(string text, bool colorizeResultTokens)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Paragraph paragraph = EnsureTerminalParagraph();
            string normalizedText = text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");
            _terminalEndsWithLineBreak = normalizedText.EndsWith('\n');

            int startIndex = 0;
            while (startIndex < normalizedText.Length)
            {
                int lineBreakIndex = normalizedText.IndexOf('\n', startIndex);
                int endIndex = lineBreakIndex >= 0 ? lineBreakIndex : normalizedText.Length;
                string segment = normalizedText[startIndex..endIndex];

                AppendTerminalRuns(paragraph, segment, colorizeResultTokens);

                if (lineBreakIndex < 0)
                {
                    break;
                }

                paragraph.Inlines.Add(new LineBreak());
                startIndex = lineBreakIndex + 1;
            }
        }

        private Paragraph EnsureTerminalParagraph()
        {
            if (TerminalTextBox.Document.Blocks.LastBlock is Paragraph paragraph)
            {
                paragraph.Margin = new Thickness(0);
                return paragraph;
            }

            paragraph = new Paragraph
            {
                Margin = new Thickness(0)
            };
            TerminalTextBox.Document.Blocks.Add(paragraph);
            return paragraph;
        }

        private void AppendTerminalRuns(Paragraph paragraph, string text, bool colorizeResultTokens)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (!colorizeResultTokens)
            {
                paragraph.Inlines.Add(new Run(text));
                return;
            }

            int currentIndex = 0;
            foreach (Match match in TerminalResultTokenRegex.Matches(text))
            {
                if (match.Index > currentIndex)
                {
                    paragraph.Inlines.Add(new Run(text[currentIndex..match.Index]));
                }

                Brush? tokenBrush = GetTerminalResultBrush(match.Value);
                var tokenRun = new Run(match.Value);
                if (tokenBrush is not null)
                {
                    tokenRun.Foreground = tokenBrush;
                    tokenRun.FontWeight = FontWeights.Bold;
                }

                paragraph.Inlines.Add(tokenRun);
                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < text.Length)
            {
                paragraph.Inlines.Add(new Run(text[currentIndex..]));
            }
        }

        private Brush? GetTerminalResultBrush(string token)
        {
            return token switch
            {
                "PASS" or "AC" => _terminalSuccessBrush,
                "FAIL" => _terminalFailBrush,
                "WA" => _terminalWrongAnswerBrush,
                "TLE" => _terminalTimeLimitBrush,
                "MLE" => _terminalMemoryLimitBrush,
                "RE" => _terminalRuntimeErrorBrush,
                "OLE" => _terminalOutputLimitBrush,
                _ => null
            };
        }

        private void SetRunControlsEnabled(bool isRunning)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetRunControlsEnabled(isRunning));
                return;
            }

            StopRunButton.IsEnabled = isRunning
                                      && _pythonRunner.IsRunning;
            UpdateProblemCommandState();
        }

        private void SelectTerminalTab()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(SelectTerminalTab);
                return;
            }

            RunTabControl.SelectedItem = TerminalTabItem;
        }

        private void ResetProblemView()
        {
            ProblemTitleTextBlock.Text = "문제를 불러와주세요";
            ProblemMetaTextBlock.Text = "시간 제한: - / 메모리 제한: -";
            ProblemDescriptionTextBox.Text = "문제 설명이 이곳에 표시됩니다.";
            InputDescriptionTextBox.Text = "입력 형식이 이곳에 표시됩니다.";
            OutputDescriptionTextBox.Text = "출력 형식이 이곳에 표시됩니다.";
            SampleInputTextBox.Text = "예시 입력이 이곳에 표시됩니다.";
            SampleOutputTextBox.Text = "예시 출력이 이곳에 표시됩니다.";
            ProgramInputTextBox.Text = string.Empty;
            CurrentProblemStatusTextBlock.Text = "문제 미선택";
            UpdateProblemCommandState();
        }

        private void DisplayProblem(ProblemDocument problem)
        {
            string title = string.IsNullOrWhiteSpace(problem.Id)
                ? problem.Title
                : $"[{problem.Id}] {problem.Title}";

            ProblemTitleTextBlock.Text = string.IsNullOrWhiteSpace(title) ? "제목 없는 문제" : title;
            ProblemMetaTextBlock.Text = FormatProblemMetaText(problem);
            ProblemDescriptionTextBox.Text = string.IsNullOrWhiteSpace(problem.Description)
                ? "문제 설명이 비어 있습니다."
                : problem.Description;
            InputDescriptionTextBox.Text = string.IsNullOrWhiteSpace(problem.InputFormat)
                ? "입력 설명이 비어 있습니다."
                : problem.InputFormat;
            OutputDescriptionTextBox.Text = string.IsNullOrWhiteSpace(problem.OutputFormat)
                ? "출력 설명이 비어 있습니다."
                : problem.OutputFormat;

            SampleCaseDocument? firstSample = problem.Samples.Count > 0 ? problem.Samples[0] : null;
            SampleInputTextBox.Text = firstSample?.Input ?? "예시 입력이 없습니다.";
            SampleOutputTextBox.Text = firstSample?.Output ?? "예시 출력이 없습니다.";
            ProgramInputTextBox.Text = firstSample?.Input ?? string.Empty;

            UpdateCurrentProblemStatus();
            UpdateProblemCommandState();
        }

        private string FormatProblemMetaText(ProblemDocument problem)
        {
            string authorName = string.IsNullOrWhiteSpace(problem.AuthorName) ? "-" : problem.AuthorName;
            string source = string.IsNullOrWhiteSpace(problem.Source) ? "-" : problem.Source;

            string limitText;
            if (_benchmarkResult is null)
            {
                limitText = $"시간 제한: {problem.TimeLimitMs} ms / 메모리 제한: {problem.MemoryLimitMb} MB / 로컬 적용 제한: 벤치마크 전";
            }
            else
            {
                int adjustedTimeLimitMs = _benchmarkResult.ApplyTimeLimitMs(problem.TimeLimitMs);
                int adjustedMemoryLimitMb = _benchmarkResult.ApplyMemoryLimitMb(problem.MemoryLimitMb);
                limitText = $"시간 제한: {problem.TimeLimitMs} ms -> 로컬 적용 {adjustedTimeLimitMs} ms / 메모리 제한: {problem.MemoryLimitMb} MB -> 로컬 적용 {adjustedMemoryLimitMb} MB";
            }

            return $"{limitText} / 예제: {problem.Samples.Count}개 / 채점 테스트: {problem.TestCases.Count}개 / 제작자: {authorName} / 출처: {source}";
        }

        private void UpdateCurrentProblemStatus()
        {
            if (_currentProblem is null)
            {
                CurrentProblemStatusTextBlock.Text = "문제 미선택";
                return;
            }

            string title = string.IsNullOrWhiteSpace(_currentProblem.Id)
                ? _currentProblem.Title
                : $"[{_currentProblem.Id}] {_currentProblem.Title}";
            string saveState = _isProblemDirty ? "저장 안 됨" : "저장됨";
            string pathState = string.IsNullOrWhiteSpace(_currentProblemFilePath)
                ? "새 문제"
                : Path.GetFileName(_currentProblemFilePath);

            CurrentProblemStatusTextBlock.Text = $"현재 문제: {title} | {saveState} | {pathState}";
        }

        private void UpdateProblemCommandState()
        {
            bool hasProblem = _currentProblem is not null;
            bool canRunWithLimits = _benchmarkResult is not null
                                    && !_isBenchmarkRunning
                                    && !_pythonRunner.IsRunning;

            RunCodeMenuItem.IsEnabled = canRunWithLimits;
            RunCodeButton.IsEnabled = canRunWithLimits;
            EditProblemMenuItem.IsEnabled = hasProblem;
            EditProblemButton.IsEnabled = hasProblem;
            RunSampleMenuItem.IsEnabled = hasProblem && canRunWithLimits;
            RunSampleButton.IsEnabled = hasProblem && canRunWithLimits;
            SubmitMenuItem.IsEnabled = hasProblem && canRunWithLimits;
            SubmitButton.IsEnabled = hasProblem && canRunWithLimits;
            SubmissionHistoryMenuItem.IsEnabled = hasProblem;
            ExportSubmissionHistoryMenuItem.IsEnabled = hasProblem;
            BenchmarkMenuItem.IsEnabled = !_isBenchmarkRunning && !_pythonRunner.IsRunning;
        }

        private async Task StartEnvironmentBenchmarkAsync(bool isManual)
        {
            if (_isBenchmarkRunning)
            {
                AppendTerminal("[Benchmark] 이미 채점 환경 벤치마크가 실행 중입니다.");
                return;
            }

            if (_pythonRunner.IsRunning)
            {
                SetStatus("채점 환경 점검 불가", isError: true);
                AppendTerminal("[Benchmark] 실행 중인 Python 프로세스가 있어 벤치마크를 시작할 수 없습니다.");
                return;
            }

            JudgeEnvironmentBenchmarkResult? previousResult = _benchmarkResult;
            _isBenchmarkRunning = true;
            UpdateProblemCommandState();
            SelectTerminalTab();

            SetStatus("채점 환경 점검 중", isWorking: true);
            AppendTerminal("----------------------------------------");
            AppendTerminal(isManual
                ? "[Benchmark] 채점 환경 벤치마크를 다시 실행합니다."
                : "[Benchmark] 프로그램 시작 필수 채점 환경 벤치마크를 실행합니다.");
            AppendTerminal($"[Benchmark] Python: {_pythonRunner.PythonExecutablePath}");

            try
            {
                JudgeEnvironmentBenchmarkResult result = await _environmentBenchmark.RunAsync();
                bool keptPreviousResult = !result.Succeeded && previousResult is not null;

                if (!keptPreviousResult)
                {
                    _benchmarkResult = result;
                }

                if (result.Succeeded)
                {
                    SetStatus("채점 환경 준비 완료");
                    AppendBenchmarkSummary(result);
                }
                else
                {
                    SetStatus("채점 환경 점검 실패", isError: true);
                    AppendTerminal($"[Benchmark] 실패: {result.ErrorMessage}");

                    if (keptPreviousResult)
                    {
                        AppendTerminal("[Benchmark] 이전 보정값을 유지합니다.");
                        AppendBenchmarkSummary(previousResult!);
                    }
                    else
                    {
                        AppendTerminal("[Benchmark] 안전 기본 보정값을 적용합니다.");
                        AppendBenchmarkSummary(result);
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus("채점 환경 점검 실패", isError: true);
                AppendTerminal("[Benchmark] 벤치마크 실행 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);

                if (previousResult is not null)
                {
                    _benchmarkResult = previousResult;
                    AppendTerminal("[Benchmark] 이전 보정값을 유지합니다.");
                    AppendBenchmarkSummary(previousResult);
                }
                else
                {
                    _benchmarkResult = JudgeEnvironmentBenchmarkResult.CreateFallback(ex.Message);
                    AppendTerminal("[Benchmark] 안전 기본 보정값을 적용합니다.");
                    AppendBenchmarkSummary(_benchmarkResult);
                }
            }
            finally
            {
                _isBenchmarkRunning = false;

                if (_currentProblem is not null)
                {
                    DisplayProblem(_currentProblem);
                }
                else
                {
                    UpdateProblemCommandState();
                }
            }
        }

        private void AppendBenchmarkSummary(JudgeEnvironmentBenchmarkResult result)
        {
            if (result.EmptyPythonStartupMs > 0)
            {
                AppendTerminal($"[Benchmark] Python 시작 오버헤드: {result.EmptyPythonStartupMs:0} ms");
            }

            foreach (JudgeBenchmarkSampleResult sample in result.Samples)
            {
                if (sample.IsMemorySample)
                {
                    AppendTerminal($"[Benchmark] {sample.Complexity} {sample.Name}: {sample.ActualElapsedMs:0} ms / peak {FormatMemoryBytes(sample.PeakWorkingSetBytes)}");
                }
                else
                {
                    AppendTerminal($"[Benchmark] {sample.Complexity} {sample.Name}: 기준 {sample.ReferenceElapsedMs:0} ms / 실측 {sample.ActualElapsedMs:0} ms / 배율 {sample.Slowdown:0.00}x");
                }
            }

            AppendTerminal($"[Benchmark] 최종 시간 배율: {result.TimeMultiplier:0.00}x");
            AppendTerminal($"[Benchmark] 추가 시간: {result.ExtraTimeMs} ms");
            AppendTerminal($"[Benchmark] 추가 메모리: {result.ExtraMemoryMb} MB");
        }

        private bool EnsureBenchmarkReadyForRun()
        {
            if (_isBenchmarkRunning)
            {
                SetStatus("채점 환경 점검 중", isError: true);
                AppendTerminal("[Benchmark] 채점 환경 벤치마크가 끝난 뒤 실행할 수 있습니다.");
                return false;
            }

            if (_benchmarkResult is null)
            {
                SetStatus("채점 환경 미준비", isError: true);
                AppendTerminal("[Benchmark] 채점 환경 벤치마크가 아직 완료되지 않았습니다.");
                return false;
            }

            return true;
        }

        private void NewProblemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var editor = new ProblemEditorWindow
            {
                Owner = this
            };

            bool? result = editor.ShowDialog();
            if (result != true)
            {
                AppendTerminal("[Problem] 새 문제 만들기를 취소했습니다.");
                UpdateProblemCommandState();
                return;
            }

            AppendTerminal($"[Problem] 새 문제를 저장했습니다: {editor.Problem.Title}");
            AppendTerminal($"[Problem] 저장 위치: {editor.SavedFilePath ?? "경로 알 수 없음"}");
            UpdateProblemCommandState();
        }

        private void EditProblemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProblem is null)
            {
                AppendTerminal("[Problem] 먼저 문제를 만들거나 불러오세요.");
                return;
            }

            var editor = new ProblemEditorWindow(_currentProblem)
            {
                Owner = this
            };

            bool? result = editor.ShowDialog();
            if (result != true)
            {
                AppendTerminal("[Problem] 문제 수정을 취소했습니다.");
                return;
            }

            _currentProblem = editor.Problem;
            _isProblemDirty = true;

            DisplayProblem(_currentProblem);
            AppendTerminal($"[Problem] 문제를 수정했습니다: {_currentProblem.Title}");

            SaveCurrentProblemWithDialogIfNeeded();
        }

        private void LoadProblemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "문제 JSON 불러오기",
                Filter = "JSON 문제 파일 (*.json)|*.json|모든 파일 (*.*)|*.*"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            AppendTerminal($"[Problem] 문제 파일을 불러옵니다: {dialog.FileName}");

            try
            {
                string json = File.ReadAllText(dialog.FileName);
                ProblemDocument? problem = JsonSerializer.Deserialize<ProblemDocument>(json, _jsonOptions);

                if (problem is null)
                {
                    throw new InvalidOperationException("문제 JSON을 읽었지만 내용이 비어 있습니다.");
                }

                problem.Samples ??= new();
                problem.TestCases ??= new();
                problem.AuthorName ??= string.Empty;
                problem.Source ??= string.Empty;
                problem.Version = problem.Version <= 0 ? 3 : Math.Max(problem.Version, 3);
                problem.TimeLimitMs = problem.TimeLimitMs <= 0 ? 2000 : problem.TimeLimitMs;
                problem.MemoryLimitMb = problem.MemoryLimitMb <= 0 ? 128 : problem.MemoryLimitMb;

                _currentProblem = problem;
                _currentProblemFilePath = dialog.FileName;
                _isProblemDirty = false;

                DisplayProblem(problem);
                AppendTerminal($"[Problem] 문제를 불러왔습니다: [{problem.Id}] {problem.Title}");
            }
            catch (JsonException ex)
            {
                AppendTerminal("[Problem] JSON 형식이 올바르지 않습니다.");
                AppendTerminal(ex.Message);
            }
            catch (Exception ex)
            {
                AppendTerminal("[Problem] 문제 불러오기 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private void SaveProblemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProblem is null)
            {
                AppendTerminal("[Problem] 먼저 문제를 만들거나 불러오세요.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentProblemFilePath))
            {
                SaveProblemAsMenuItem_Click(sender, e);
                return;
            }

            SaveCurrentProblemToFile(_currentProblemFilePath);
        }

        private void SaveProblemAsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProblem is null)
            {
                AppendTerminal("[Problem] 먼저 문제를 만들거나 불러오세요.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "문제 JSON 저장",
                Filter = "JSON 문제 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
                FileName = CreateDefaultProblemFileName(_currentProblem)
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            SaveCurrentProblemToFile(dialog.FileName);
        }

        private bool SaveCurrentProblemWithDialogIfNeeded()
        {
            if (_currentProblem is null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_currentProblemFilePath))
            {
                return SaveCurrentProblemToFile(_currentProblemFilePath);
            }

            var dialog = new SaveFileDialog
            {
                Title = "문제 JSON 저장",
                Filter = "JSON 문제 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
                FileName = CreateDefaultProblemFileName(_currentProblem)
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                AppendTerminal("[Problem] 문제 JSON 저장을 취소했습니다.");
                return false;
            }

            return SaveCurrentProblemToFile(dialog.FileName);
        }

        private bool SaveCurrentProblemToFile(string filePath)
        {
            if (_currentProblem is null)
            {
                return false;
            }

            AppendTerminal($"[Problem] 문제 파일을 저장합니다: {filePath}");

            try
            {
                string json = JsonSerializer.Serialize(_currentProblem, _jsonOptions);
                File.WriteAllText(filePath, json);

                _currentProblemFilePath = filePath;
                _isProblemDirty = false;
                UpdateCurrentProblemStatus();

                AppendTerminal("[Problem] 문제 JSON 저장이 완료되었습니다.");
                return true;
            }
            catch (Exception ex)
            {
                AppendTerminal("[Problem] 문제 저장 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                return false;
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

        private static string CreateDefaultSubmissionExportFileName(ProblemDocument problem)
        {
            string baseName = string.IsNullOrWhiteSpace(problem.Id)
                ? problem.Title
                : $"{problem.Id}_{problem.Title}";

            baseName = Regex.Replace(baseName, @"[\\/:*?""<>|]+", "_").Trim();

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "problem";
            }

            return baseName + "_submissions.zip";
        }

        private async void SaveCodeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string code = await GetEditorCodeAsync();

            AppendTerminal($"[Code] 현재 코드 길이: {code.Length}자");
        }

        private async void RunCodeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureBenchmarkReadyForRun())
            {
                return;
            }

            string code = await GetEditorCodeAsync();

            if (string.IsNullOrWhiteSpace(code))
            {
                SetStatus("실행할 코드가 없습니다", isError: true);
                AppendTerminal("[Run] 실행할 Python 코드가 없습니다.");
                return;
            }

            await RunPythonCodeAsync(
                code,
                runTitle: "일반 실행",
                inputText: ProgramInputTextBox.Text ?? string.Empty,
                limits: CreateExecutionLimits());
        }

        private async void RunSampleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureBenchmarkReadyForRun())
            {
                return;
            }

            string code = await GetEditorCodeAsync();

            if (string.IsNullOrWhiteSpace(code))
            {
                SetStatus("실행할 코드가 없습니다", isError: true);
                AppendTerminal("[Run] 실행할 Python 코드가 없습니다.");
                return;
            }

            if (_currentProblem is null || _currentProblem.Samples.Count == 0)
            {
                SetStatus("예제 입력이 없습니다", isError: true);
                AppendTerminal("[Run] 현재 문제에 등록된 예제 입력이 없습니다.");
                return;
            }

            int passedCount = 0;
            int totalCount = _currentProblem.Samples.Count;

            SetStatus("예제 실행 중", isWorking: true);
            AppendTerminal($"[Run] 예제 실행 시작 - 총 {totalCount}개");

            for (int i = 0; i < totalCount; i++)
            {
                SampleCaseDocument sample = _currentProblem.Samples[i];
                int sampleNumber = i + 1;

                AppendTerminal("----------------------------------------");
                AppendTerminal($"[Sample {sampleNumber}] 입력:");
                AppendTerminal(IndentMultiline(sample.Input));

                PythonExecutionResult? result = await RunPythonCodeAsync(
                    code,
                    runTitle: $"예제 {sampleNumber} 실행",
                    inputText: sample.Input,
                    showStartBanner: false,
                    limits: CreateExecutionLimits(_currentProblem));

                if (result is null)
                {
                    break;
                }

                bool passed = result.Succeeded
                              && CompareOutput(result.StandardOutput, sample.Output);

                if (passed)
                {
                    passedCount++;
                }

                AppendTerminal($"[Sample {sampleNumber}] {(passed ? "PASS" : "FAIL")} | {result.Elapsed.TotalMilliseconds:0} ms | 제한: {FormatExecutionLimits(result.Limits)}");

                AppendExecutionResult(result, passed);

                AppendTerminal("Expected:");
                AppendTerminal(IndentMultiline(sample.Output));
                AppendTerminal("Actual:");
                AppendTerminal(IndentMultiline(result.StandardOutput));

                if (ShouldStopBatch(result))
                {
                    break;
                }
            }

            AppendTerminal("----------------------------------------");
            AppendTerminal($"[Run] 예제 실행 완료: {passedCount}/{totalCount} 통과");

            if (passedCount == totalCount)
            {
                SetStatus($"예제 실행 완료: {passedCount}/{totalCount} 통과");
            }
            else
            {
                SetStatus($"예제 실행 완료: {passedCount}/{totalCount} 통과", isError: true);
            }
        }

        private async Task<PythonExecutionResult?> RunPythonCodeAsync(
            string code,
            string runTitle,
            string inputText,
            bool showStartBanner = true,
            PythonExecutionLimits? limits = null)
        {
            if (_pythonRunner.IsRunning)
            {
                SetStatus("이미 실행 중", isError: true);
                AppendTerminal("[Run] 이미 실행 중인 Python 프로세스가 있습니다. 중지 후 다시 실행하세요.");
                return null;
            }

            try
            {
                SetStatus($"{runTitle} 중", isWorking: true);

                if (showStartBanner)
                {
                    AppendTerminal("----------------------------------------");
                    AppendTerminal($"[Run] {runTitle} 시작");
                    AppendTerminal($"[Run] Python: {_pythonRunner.PythonExecutablePath}");
                }

                if (showStartBanner)
                {
                    AppendTerminal("[Input]");
                    AppendTerminal(IndentMultiline(inputText));
                    AppendTerminal("[Output]");
                }

                PythonExecutionLimits executionLimits = limits ?? CreateExecutionLimits();

                PythonExecutionResult result = await _pythonRunner.RunAsync(
                    code,
                    inputText,
                    executionLimits,
                    AppendTerminalRaw,
                    () =>
                    {
                        SelectTerminalTab();
                        SetRunControlsEnabled(true);
                    });

                if (showStartBanner)
                {
                    AppendTerminal(string.Empty);
                    AppendTerminal("[Run] 프로세스 종료");
                    AppendTerminal($"[Run] ExitCode: {result.ExitCode}");
                    AppendTerminal($"[Run] 실행 시간: {result.Elapsed.TotalMilliseconds:0} ms");
                    AppendTerminal($"[Run] 최대 메모리: {FormatMemoryBytes(result.PeakWorkingSetBytes)}");
                    AppendTerminal($"[Run] 제한: {FormatExecutionLimits(result.Limits)}");

                    if (result.Status == PythonExecutionStatus.Stopped)
                    {
                        SetStatus("실행 중지됨", isError: true);
                        AppendTerminal("[Run] 사용자가 실행을 중지했습니다.");
                    }
                    else if (result.Status == PythonExecutionStatus.TimeLimitExceeded)
                    {
                        SetStatus("Time Limit Exceeded", isError: true);
                        AppendTerminal("[Run] 시간 제한을 초과했습니다.");
                    }
                    else if (result.Status == PythonExecutionStatus.MemoryLimitExceeded)
                    {
                        SetStatus("Memory Limit Exceeded", isError: true);
                        AppendTerminal("[Run] 메모리 제한을 초과했습니다.");
                    }
                    else if (result.Status == PythonExecutionStatus.OutputLimitExceeded)
                    {
                        SetStatus("Output Limit Exceeded", isError: true);
                        AppendTerminal("[Run] 출력 제한을 초과했습니다.");
                    }
                    else if (result.ExitCode != 0)
                    {
                        SetStatus("Runtime Error", isError: true);
                        AppendTerminal("[Run] Runtime Error가 발생했습니다.");
                    }
                    else
                    {
                        SetStatus("실행 완료");
                        AppendTerminal("[Run] 실행이 정상 종료되었습니다.");
                    }
                }

                return result;
            }
            catch (Win32Exception)
            {
                SetStatus("Python 실행 실패", isError: true);
                AppendTerminal("[Run] Python을 실행하지 못했습니다.");
                AppendTerminal("[Run] Python이 설치되어 있고 PATH에 등록되어 있는지 확인하세요.");
                AppendTerminal($"[Run] 현재 Python 실행 파일 설정: {_pythonRunner.PythonExecutablePath}");
                return null;
            }
            catch (Exception ex)
            {
                SetStatus("실행 실패", isError: true);
                AppendTerminal("[Run] 실행 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                return null;
            }
            finally
            {
                SetRunControlsEnabled(false);
                StopRunButton.IsEnabled = false;
            }
        }

        private PythonExecutionLimits CreateExecutionLimits(ProblemDocument? problem = null)
        {
            problem ??= _currentProblem;

            int timeLimitMs = problem?.TimeLimitMs > 0
                ? problem.TimeLimitMs
                : 2000;
            int memoryLimitMb = problem?.MemoryLimitMb > 0
                ? problem.MemoryLimitMb
                : 128;
            JudgeEnvironmentBenchmarkResult calibration = _benchmarkResult
                ?? JudgeEnvironmentBenchmarkResult.DefaultFallback;

            int adjustedTimeLimitMs = calibration.ApplyTimeLimitMs(timeLimitMs);
            int adjustedMemoryLimitMb = calibration.ApplyMemoryLimitMb(memoryLimitMb);

            return new PythonExecutionLimits(
                TimeSpan.FromMilliseconds(adjustedTimeLimitMs),
                adjustedMemoryLimitMb * 1024L * 1024L,
                OutputLimitBytes);
        }

        private static string FormatExecutionLimits(PythonExecutionLimits limits)
        {
            string timeLimit = limits.TimeLimit is null
                ? "-"
                : $"{limits.TimeLimit.Value.TotalMilliseconds:0} ms";
            string memoryLimit = limits.MemoryLimitBytes is null
                ? "-"
                : $"{limits.MemoryLimitBytes.Value / 1024 / 1024} MB";
            string outputLimit = limits.OutputLimitBytes is null
                ? "-"
                : $"{limits.OutputLimitBytes.Value / 1024} KB";

            return $"시간 {timeLimit} / 메모리 {memoryLimit} / 출력 {outputLimit}";
        }

        private static string FormatMemoryBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "-";
            }

            return $"{bytes / 1024d / 1024d:0.#} MB";
        }

        private void AppendExecutionResult(PythonExecutionResult result, bool accepted)
        {
            string verdict = GetSubmissionVerdict(result, accepted);

            switch (verdict)
            {
                case "Stopped":
                    AppendTerminal("Result: Stopped");
                    return;

                case "TLE":
                    AppendTerminal("Result: TLE (Time Limit Exceeded)");
                    return;

                case "MLE":
                    AppendTerminal("Result: MLE (Memory Limit Exceeded)");
                    return;

                case "OLE":
                    AppendTerminal("Result: OLE (Output Limit Exceeded)");
                    return;

                case "RE":
                    AppendTerminal($"Result: RE (Runtime Error, ExitCode: {result.ExitCode})");
                    return;

                case "WA":
                    AppendTerminal("Result: WA (Wrong Answer)");
                    return;

                default:
                    AppendTerminal("Result: AC (Accepted)");
                    return;
            }
        }

        private static string GetSubmissionVerdict(PythonExecutionResult result, bool accepted)
        {
            return result.Status switch
            {
                PythonExecutionStatus.Stopped => "Stopped",
                PythonExecutionStatus.TimeLimitExceeded => "TLE",
                PythonExecutionStatus.MemoryLimitExceeded => "MLE",
                PythonExecutionStatus.OutputLimitExceeded => "OLE",
                _ when result.ExitCode != 0 => "RE",
                _ when !accepted => "WA",
                _ => "AC"
            };
        }

        private static bool ShouldStopBatch(PythonExecutionResult result)
        {
            return result.Status is PythonExecutionStatus.Stopped
                or PythonExecutionStatus.TimeLimitExceeded
                or PythonExecutionStatus.MemoryLimitExceeded
                or PythonExecutionStatus.OutputLimitExceeded;
        }

        private static string DetermineSubmissionVerdict(
            IReadOnlyList<SubmissionTestResultDocument> testResults,
            bool judgingError,
            int passedCount,
            int totalCount)
        {
            if (judgingError)
            {
                return "JudgingError";
            }

            if (passedCount == totalCount && testResults.Count == totalCount)
            {
                return "AC";
            }

            SubmissionTestResultDocument? firstFailedResult = testResults
                .FirstOrDefault(result => !string.Equals(result.Verdict, "AC", StringComparison.Ordinal));

            return firstFailedResult?.Verdict ?? "JudgingError";
        }

        private static SubmissionTestResultDocument CreateSubmissionTestResult(
            int testNumber,
            PythonExecutionResult result,
            string verdict)
        {
            string standardOutput = SubmissionHistoryStore.TruncateCapturedOutput(
                result.StandardOutput,
                out bool standardOutputTruncated);
            string standardError = SubmissionHistoryStore.TruncateCapturedOutput(
                result.StandardError,
                out bool standardErrorTruncated);

            return new SubmissionTestResultDocument
            {
                TestNumber = testNumber,
                Verdict = verdict,
                ExitCode = result.ExitCode,
                ElapsedMs = result.Elapsed.TotalMilliseconds,
                PeakMemoryBytes = result.PeakWorkingSetBytes,
                StandardOutput = standardOutput,
                StandardOutputTruncated = standardOutputTruncated,
                StandardError = standardError,
                StandardErrorTruncated = standardErrorTruncated
            };
        }

        private SubmissionAttemptDocument CreateSubmissionAttempt(
            ProblemDocument problem,
            string? problemFilePath,
            string code,
            string verdict,
            int passedCount,
            int totalCount,
            PythonExecutionLimits limits,
            List<SubmissionTestResultDocument> testResults)
        {
            DateTimeOffset submittedAt = DateTimeOffset.Now;
            JudgeEnvironmentBenchmarkResult benchmark = _benchmarkResult
                ?? JudgeEnvironmentBenchmarkResult.DefaultFallback;

            return new SubmissionAttemptDocument
            {
                AttemptId = SubmissionHistoryStore.CreateAttemptId(submittedAt),
                SubmittedAt = submittedAt,
                Problem = CreateSubmissionProblemDocument(problem),
                ProblemFilePath = problemFilePath ?? string.Empty,
                Verdict = verdict,
                PassedCount = passedCount,
                TotalCount = totalCount,
                Code = code,
                Limits = new SubmissionLimitDocument
                {
                    IdealTimeLimitMs = problem.TimeLimitMs,
                    IdealMemoryLimitMb = problem.MemoryLimitMb,
                    AppliedTimeLimitMs = limits.TimeLimit is null
                        ? 0
                        : (int)Math.Ceiling(limits.TimeLimit.Value.TotalMilliseconds),
                    AppliedMemoryLimitMb = limits.MemoryLimitBytes is null
                        ? 0
                        : (int)(limits.MemoryLimitBytes.Value / 1024 / 1024),
                    OutputLimitBytes = limits.OutputLimitBytes ?? 0
                },
                Benchmark = new SubmissionBenchmarkDocument
                {
                    IsFallback = benchmark.IsFallback,
                    TimeMultiplier = benchmark.TimeMultiplier,
                    ExtraTimeMs = benchmark.ExtraTimeMs,
                    ExtraMemoryMb = benchmark.ExtraMemoryMb
                },
                TestResults = testResults
            };
        }

        private static SubmissionProblemDocument CreateSubmissionProblemDocument(ProblemDocument problem)
        {
            return new SubmissionProblemDocument
            {
                Id = problem.Id,
                Title = problem.Title,
                AuthorName = problem.AuthorName,
                Source = problem.Source
            };
        }

        private void SaveSubmissionAttempt(SubmissionAttemptDocument attempt)
        {
            try
            {
                string filePath = _submissionHistoryStore.SaveAttempt(attempt);
                AppendTerminal($"[Submit] 제출 이력을 저장했습니다: {filePath}");
            }
            catch (Exception ex)
            {
                AppendTerminal("[Submit] 제출 이력 저장 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private void StopRunButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_pythonRunner.IsRunning)
            {
                AppendTerminal("[Run] 실행 중인 Python 프로세스가 없습니다.");
                return;
            }

            try
            {
                AppendTerminal("[Run] 실행 중지 요청");
                _pythonRunner.Stop();
            }
            catch (Exception ex)
            {
                SetStatus("실행 중지 실패", isError: true);
                AppendTerminal("[Run] Python 프로세스를 중지하지 못했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private static bool CompareOutput(string actual, string expected)
        {
            return NormalizeOutput(actual) == NormalizeOutput(expected);
        }

        private static string NormalizeOutput(string text)
        {
            return string.Join(
                    "\n",
                    text.Replace("\r\n", "\n")
                        .Replace("\r", "\n")
                        .Split('\n')
                        .Select(line => line.TrimEnd()))
                .TrimEnd();
        }

        private static string IndentMultiline(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "  <empty>";
            }

            return "  " + text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine + "  ");
        }

        private void DebugMenuItem_Click(object sender, RoutedEventArgs e)
        {
            AppendTerminal("[Debug] 디버그 기능은 추후 debugpy와 연결합니다.");
        }

        private async void SubmitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureBenchmarkReadyForRun())
            {
                return;
            }

            string code = await GetEditorCodeAsync();

            if (string.IsNullOrWhiteSpace(code))
            {
                SetStatus("제출할 코드가 없습니다", isError: true);
                AppendTerminal("[Submit] 제출할 Python 코드가 없습니다.");
                return;
            }

            if (_currentProblem is null)
            {
                SetStatus("채점할 문제가 없습니다", isError: true);
                AppendTerminal("[Submit] 먼저 문제를 만들거나 불러오세요.");
                return;
            }

            ProblemDocument problem = _currentProblem;
            string? problemFilePath = _currentProblemFilePath;

            if (problem.TestCases.Count == 0)
            {
                SetStatus("채점 테스트가 없습니다", isError: true);
                AppendTerminal("[Submit] 현재 문제에 등록된 채점 테스트케이스가 없습니다.");
                return;
            }

            int passedCount = 0;
            int totalCount = problem.TestCases.Count;
            var testResults = new List<SubmissionTestResultDocument>();
            bool judgingError = false;
            PythonExecutionLimits submissionLimits = CreateExecutionLimits(problem);

            SetStatus("채점 중", isWorking: true);
            AppendTerminal("----------------------------------------");
            AppendTerminal($"[Submit] 채점 시작 - 총 {totalCount}개");

            for (int i = 0; i < totalCount; i++)
            {
                TestCaseDocument testCase = problem.TestCases[i];
                int testCaseNumber = i + 1;

                PythonExecutionResult? result = await RunPythonCodeAsync(
                    code,
                    runTitle: $"테스트 {testCaseNumber} 채점",
                    inputText: testCase.Input,
                    showStartBanner: false,
                    limits: submissionLimits);

                if (result is null)
                {
                    judgingError = true;
                    break;
                }

                bool passed = result.Succeeded
                              && CompareOutput(result.StandardOutput, testCase.Output);
                string testVerdict = GetSubmissionVerdict(result, passed);

                if (passed)
                {
                    passedCount++;
                }

                AppendTerminal($"[Test {testCaseNumber}] {(passed ? "PASS" : "FAIL")} | {result.Elapsed.TotalMilliseconds:0} ms | 제한: {FormatExecutionLimits(result.Limits)}");

                AppendExecutionResult(result, passed);
                testResults.Add(CreateSubmissionTestResult(testCaseNumber, result, testVerdict));

                if (ShouldStopBatch(result))
                {
                    break;
                }
            }

            AppendTerminal("----------------------------------------");
            AppendTerminal($"[Submit] 채점 완료: {passedCount}/{totalCount} 통과");

            if (passedCount == totalCount)
            {
                SetStatus($"채점 완료: {passedCount}/{totalCount} 통과");
            }
            else
            {
                SetStatus($"채점 완료: {passedCount}/{totalCount} 통과", isError: true);
            }

            string finalVerdict = DetermineSubmissionVerdict(testResults, judgingError, passedCount, totalCount);
            SaveSubmissionAttempt(CreateSubmissionAttempt(
                problem,
                problemFilePath,
                code,
                finalVerdict,
                passedCount,
                totalCount,
                submissionLimits,
                testResults));
        }

        private void SubmissionHistoryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProblem is null)
            {
                AppendTerminal("[Submit] 제출 이력을 보려면 먼저 문제를 불러오세요.");
                return;
            }

            try
            {
                SubmissionProblemDocument problemDocument = CreateSubmissionProblemDocument(_currentProblem);
                IReadOnlyList<SubmissionAttemptHistoryItem> historyItems = _submissionHistoryStore
                    .LoadAttemptsForProblem(problemDocument);

                var window = new SubmissionHistoryWindow(_currentProblem, historyItems)
                {
                    Owner = this
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                AppendTerminal("[Submit] 제출 이력을 불러오는 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private void ExportSubmissionHistoryMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProblem is null)
            {
                AppendTerminal("[Submit] 제출 이력을 내보내려면 먼저 문제를 불러오세요.");
                return;
            }

            try
            {
                SubmissionProblemDocument problemDocument = CreateSubmissionProblemDocument(_currentProblem);
                IReadOnlyList<SubmissionAttemptHistoryItem> historyItems = _submissionHistoryStore
                    .LoadAttemptsForProblem(problemDocument);

                if (historyItems.Count == 0)
                {
                    AppendTerminal("[Submit] 내보낼 제출 이력이 없습니다.");
                    MessageBox.Show(
                        "현재 문항에 저장된 제출 이력이 없습니다.",
                        "제출 이력 내보내기",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = "제출 이력 내보내기",
                    Filter = "ZIP 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*",
                    DefaultExt = ".zip",
                    FileName = CreateDefaultSubmissionExportFileName(_currentProblem)
                };

                bool? result = dialog.ShowDialog(this);
                if (result != true)
                {
                    return;
                }

                string displayName = string.IsNullOrWhiteSpace(_currentProblem.Id)
                    ? _currentProblem.Title
                    : $"[{_currentProblem.Id}] {_currentProblem.Title}";

                var request = new SubmissionHistoryExportRequest
                {
                    DestinationFilePath = dialog.FileName,
                    ExportKind = "Problem",
                    ExportName = displayName,
                    Problems =
                    {
                        new SubmissionHistoryExportProblem
                        {
                            DisplayName = displayName,
                            Problem = problemDocument,
                            ProblemFilePath = _currentProblemFilePath ?? string.Empty
                        }
                    }
                };

                SubmissionHistoryExportResult exportResult = _submissionHistoryExporter.Export(request);
                AppendTerminal($"[Submit] 제출 이력을 내보냈습니다: {exportResult.FilePath}");
                AppendTerminal($"[Submit] 내보낸 문항: {exportResult.ProblemCount}개 / 제출: {exportResult.AttemptCount}개");
            }
            catch (Exception ex)
            {
                AppendTerminal("[Submit] 제출 이력 내보내기 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private async void PythonPathMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Python 실행 파일 선택",
                Filter = "Python 실행 파일 (python.exe)|python.exe|실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            _pythonRunner.PythonExecutablePath = dialog.FileName;
            AppendTerminal($"[Settings] Python 경로를 설정했습니다: {_pythonRunner.PythonExecutablePath}");

            string versionText = await GetPythonVersionTextAsync();
            if (!string.IsNullOrWhiteSpace(versionText))
            {
                AppendTerminal($"[Settings] {versionText}");
            }

            AppendTerminal("[Benchmark] Python 경로가 변경되어 채점 환경 벤치마크를 다시 실행합니다.");
            await StartEnvironmentBenchmarkAsync(isManual: true);
        }

        private async void BenchmarkMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await StartEnvironmentBenchmarkAsync(isManual: true);
        }

        private async Task<string> GetPythonVersionTextAsync()
        {
            try
            {
                return await _pythonRunner.GetVersionTextAsync();
            }
            catch (Exception ex)
            {
                AppendTerminal("[Settings] Python 버전 확인 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                return string.Empty;
            }
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Local Judge\nC# WPF 기반 PS 로컬 저지 프로그램",
                "정보",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ClearTerminalMenuItem_Click(object sender, RoutedEventArgs e)
        {
            TerminalTextBox.Document.Blocks.Clear();
            _terminalEndsWithLineBreak = true;
            AppendTerminal("[System] 터미널을 비웠습니다.");
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_pythonRunner.IsRunning)
                {
                    _pythonRunner.Stop();
                }
            }
            catch
            {
                // 종료 중 프로세스 정리 실패는 무시
            }

            Close();
        }
    }
}
