using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Local_Judge
{
    public partial class MainWindow : Window
    {
        private string _latestEditorCode = "";
        private TaskCompletionSource<string>? _editorCodeRequest;
        private string? _editorCodeRequestId;

        private readonly PythonRunner _pythonRunner = new();
        private const int OutputLimitBytes = PythonExecutionLimits.DefaultOutputLimitBytes;

        private readonly Brush _readyBrush = new SolidColorBrush(Color.FromRgb(45, 164, 78));
        private readonly Brush _workingBrush = new SolidColorBrush(Color.FromRgb(251, 188, 5));
        private readonly Brush _errorBrush = new SolidColorBrush(Color.FromRgb(218, 54, 51));

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private ProblemDocument? _currentProblem;
        private string? _currentProblemFilePath;
        private bool _isProblemDirty;

        public MainWindow()
        {
            InitializeComponent();

            SetStatus("대기 중");
            ResetProblemView();
            AppendTerminal("[UI] 화면 구성이 완료되었습니다.");

            _ = InitializeCodeEditorAsync();
        }

        private async Task InitializeCodeEditorAsync()
        {
            try
            {
                SetStatus("코드 편집기 초기화 중", isWorking: true);

                await CodeEditorWebView.EnsureCoreWebView2Async();

                string editorFolderPath = Path.Combine(AppContext.BaseDirectory, "Editor");

                if (!Directory.Exists(editorFolderPath))
                {
                    SetStatus("코드 편집기 초기화 실패", isError: true);
                    AppendTerminal($"[Editor] Editor 폴더를 찾을 수 없습니다: {editorFolderPath}");
                    return;
                }

                CodeEditorWebView.CoreWebView2.WebMessageReceived += CodeEditorWebView_WebMessageReceived;

                CodeEditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "localjudge.editor",
                    editorFolderPath,
                    CoreWebView2HostResourceAccessKind.Allow);

                CodeEditorWebView.Source = new Uri("https://localjudge.editor/index.html");

                SetStatus("코드 편집기 준비 중", isWorking: true);
            }
            catch (Exception ex)
            {
                SetStatus("코드 편집기 초기화 실패", isError: true);
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
                        SetStatus("코드 편집기 준비 완료");
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

            if (TerminalTextBox.Text.Length > 0 && !TerminalTextBox.Text.EndsWith(Environment.NewLine))
            {
                TerminalTextBox.AppendText(Environment.NewLine);
            }

            TerminalTextBox.AppendText(message + Environment.NewLine);
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

            string normalizedText = text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", Environment.NewLine);

            TerminalTextBox.AppendText(normalizedText);
            TerminalTextBox.ScrollToEnd();
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
        }

        private void DisplayProblem(ProblemDocument problem)
        {
            string title = string.IsNullOrWhiteSpace(problem.Id)
                ? problem.Title
                : $"[{problem.Id}] {problem.Title}";

            ProblemTitleTextBlock.Text = string.IsNullOrWhiteSpace(title) ? "제목 없는 문제" : title;
            ProblemMetaTextBlock.Text = $"시간 제한: {problem.TimeLimitMs} ms / 메모리 제한: {problem.MemoryLimitMb} MB / 예제: {problem.Samples.Count}개 / 채점 테스트: {problem.TestCases.Count}개";
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

        private void NewProblemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("문제 만드는 중", isWorking: true);

            var editor = new ProblemEditorWindow
            {
                Owner = this
            };

            bool? result = editor.ShowDialog();
            if (result != true)
            {
                SetStatus("대기 중");
                AppendTerminal("[Problem] 새 문제 만들기를 취소했습니다.");
                return;
            }

            _currentProblem = editor.Problem;
            _currentProblemFilePath = null;
            _isProblemDirty = true;

            DisplayProblem(_currentProblem);
            SetStatus("새 문제 작성 완료");
            AppendTerminal($"[Problem] 새 문제를 만들었습니다: {_currentProblem.Title}");
            AppendTerminal("[Problem] 파일 > 문제 저장 또는 다른 이름으로 문제 저장을 눌러 JSON으로 저장하세요.");
        }

        private void EditProblemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProblem is null)
            {
                SetStatus("수정할 문제가 없습니다", isError: true);
                AppendTerminal("[Problem] 먼저 문제를 만들거나 불러오세요.");
                return;
            }

            SetStatus("문제 수정 중", isWorking: true);

            var editor = new ProblemEditorWindow(_currentProblem)
            {
                Owner = this
            };

            bool? result = editor.ShowDialog();
            if (result != true)
            {
                SetStatus("대기 중");
                AppendTerminal("[Problem] 문제 수정을 취소했습니다.");
                return;
            }

            _currentProblem = editor.Problem;
            _isProblemDirty = true;

            DisplayProblem(_currentProblem);
            SetStatus("문제 수정 완료");
            AppendTerminal($"[Problem] 문제를 수정했습니다: {_currentProblem.Title}");
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
                SetStatus("대기 중");
                return;
            }

            SetStatus("문제 불러오는 중", isWorking: true);
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
                problem.Version = problem.Version <= 0 ? 2 : problem.Version;
                problem.TimeLimitMs = problem.TimeLimitMs <= 0 ? 2000 : problem.TimeLimitMs;
                problem.MemoryLimitMb = problem.MemoryLimitMb <= 0 ? 128 : problem.MemoryLimitMb;

                _currentProblem = problem;
                _currentProblemFilePath = dialog.FileName;
                _isProblemDirty = false;

                DisplayProblem(problem);
                SetStatus("문제 불러오기 완료");
                AppendTerminal($"[Problem] 문제를 불러왔습니다: [{problem.Id}] {problem.Title}");
            }
            catch (JsonException ex)
            {
                SetStatus("문제 JSON 오류", isError: true);
                AppendTerminal("[Problem] JSON 형식이 올바르지 않습니다.");
                AppendTerminal(ex.Message);
            }
            catch (Exception ex)
            {
                SetStatus("문제 불러오기 실패", isError: true);
                AppendTerminal("[Problem] 문제 불러오기 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private void SaveProblemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProblem is null)
            {
                SetStatus("저장할 문제가 없습니다", isError: true);
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
                SetStatus("저장할 문제가 없습니다", isError: true);
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
                SetStatus("대기 중");
                return;
            }

            SaveCurrentProblemToFile(dialog.FileName);
        }

        private void SaveCurrentProblemToFile(string filePath)
        {
            if (_currentProblem is null)
            {
                return;
            }

            SetStatus("문제 저장 중", isWorking: true);
            AppendTerminal($"[Problem] 문제 파일을 저장합니다: {filePath}");

            try
            {
                string json = JsonSerializer.Serialize(_currentProblem, _jsonOptions);
                File.WriteAllText(filePath, json);

                _currentProblemFilePath = filePath;
                _isProblemDirty = false;
                UpdateCurrentProblemStatus();

                SetStatus("문제 저장 완료");
                AppendTerminal("[Problem] 문제 JSON 저장이 완료되었습니다.");
            }
            catch (Exception ex)
            {
                SetStatus("문제 저장 실패", isError: true);
                AppendTerminal("[Problem] 문제 저장 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
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

        private async void SaveCodeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string code = await GetEditorCodeAsync();

            SetStatus("코드 저장 중", isWorking: true);
            AppendTerminal($"[Code] 현재 코드 길이: {code.Length}자");
            SetStatus("대기 중");
        }

        private async void RunCodeMenuItem_Click(object sender, RoutedEventArgs e)
        {
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

                AppendTerminal($"[Sample {sampleNumber}] {(passed ? "PASS" : "FAIL")} | {result.Elapsed.TotalMilliseconds:0} ms");

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

            return new PythonExecutionLimits(
                TimeSpan.FromMilliseconds(timeLimitMs),
                memoryLimitMb * 1024L * 1024L,
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

        private void AppendExecutionResult(PythonExecutionResult result, bool accepted)
        {
            switch (result.Status)
            {
                case PythonExecutionStatus.Stopped:
                    AppendTerminal("Result: Stopped");
                    return;

                case PythonExecutionStatus.TimeLimitExceeded:
                    AppendTerminal("Result: Time Limit Exceeded");
                    return;

                case PythonExecutionStatus.MemoryLimitExceeded:
                    AppendTerminal("Result: Memory Limit Exceeded");
                    return;

                case PythonExecutionStatus.OutputLimitExceeded:
                    AppendTerminal("Result: Output Limit Exceeded");
                    return;
            }

            if (result.ExitCode != 0)
            {
                AppendTerminal($"Result: Runtime Error (ExitCode: {result.ExitCode})");
            }
            else if (!accepted)
            {
                AppendTerminal("Result: Wrong Answer");
            }
            else
            {
                AppendTerminal("Result: Accepted");
            }
        }

        private static bool ShouldStopBatch(PythonExecutionResult result)
        {
            return result.Status is PythonExecutionStatus.Stopped
                or PythonExecutionStatus.TimeLimitExceeded
                or PythonExecutionStatus.MemoryLimitExceeded
                or PythonExecutionStatus.OutputLimitExceeded;
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
            SetStatus("디버그 준비 중", isWorking: true);
            AppendTerminal("[Debug] 디버그 기능은 추후 debugpy와 연결합니다.");
            SetStatus("대기 중");
        }

        private async void SubmitMenuItem_Click(object sender, RoutedEventArgs e)
        {
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

            if (_currentProblem.TestCases.Count == 0)
            {
                SetStatus("채점 테스트가 없습니다", isError: true);
                AppendTerminal("[Submit] 현재 문제에 등록된 채점 테스트케이스가 없습니다.");
                return;
            }

            int passedCount = 0;
            int totalCount = _currentProblem.TestCases.Count;

            SetStatus("채점 중", isWorking: true);
            AppendTerminal("----------------------------------------");
            AppendTerminal($"[Submit] 채점 시작 - 총 {totalCount}개");

            for (int i = 0; i < totalCount; i++)
            {
                TestCaseDocument testCase = _currentProblem.TestCases[i];
                int testCaseNumber = i + 1;

                PythonExecutionResult? result = await RunPythonCodeAsync(
                    code,
                    runTitle: $"테스트 {testCaseNumber} 채점",
                    inputText: testCase.Input,
                    showStartBanner: false,
                    limits: CreateExecutionLimits(_currentProblem));

                if (result is null)
                {
                    break;
                }

                bool passed = result.Succeeded
                              && CompareOutput(result.StandardOutput, testCase.Output);

                if (passed)
                {
                    passedCount++;
                }

                AppendTerminal($"[Test {testCaseNumber}] {(passed ? "PASS" : "FAIL")} | {result.Elapsed.TotalMilliseconds:0} ms");

                AppendExecutionResult(result, passed);

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
                SetStatus("대기 중");
                return;
            }

            _pythonRunner.PythonExecutablePath = dialog.FileName;
            SetStatus("Python 경로 설정 완료");
            AppendTerminal($"[Settings] Python 경로를 설정했습니다: {_pythonRunner.PythonExecutablePath}");

            string versionText = await GetPythonVersionTextAsync();
            if (!string.IsNullOrWhiteSpace(versionText))
            {
                AppendTerminal($"[Settings] {versionText}");
            }
        }

        private async Task<string> GetPythonVersionTextAsync()
        {
            try
            {
                return await _pythonRunner.GetVersionTextAsync();
            }
            catch (Exception ex)
            {
                SetStatus("Python 확인 실패", isError: true);
                AppendTerminal("[Settings] Python 버전 확인 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                return string.Empty;
            }
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("환경 설정");
            AppendTerminal("[Settings] 환경 설정 UI는 추후 구현합니다.");
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
            TerminalTextBox.Clear();
            TerminalTextBox.Text = "[System] 터미널을 비웠습니다.";
            SetStatus("대기 중");
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
