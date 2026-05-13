using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Local_Judge
{
    public partial class MainWindow : Window
    {
        private string _latestEditorCode = "";
        private TaskCompletionSource<string>? _editorCodeRequest;
        private string? _editorCodeRequestId;

        private const string DefaultPythonCodeFileName = "main.py";
        private string _pythonExecutablePath = "python";
        private Process? _runningProcess;
        private StreamWriter? _runningProcessInput;
        private string? _runningTempDirectory;
        private bool _isRunStopRequested;

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
                            bool isCurrentRequest = pendingRequest is not null
                                                    && (string.IsNullOrEmpty(_editorCodeRequestId)
                                                        || string.Equals(responseRequestId, _editorCodeRequestId, StringComparison.Ordinal));

                            if (isCurrentRequest)
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

        private void SetRunInputEnabled(bool isEnabled)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetRunInputEnabled(isEnabled));
                return;
            }

            TerminalInputTextBox.IsEnabled = isEnabled;
            SendEofButton.IsEnabled = isEnabled;
            StopRunButton.IsEnabled = _runningProcess is not null && !_runningProcess.HasExited;

            if (isEnabled)
            {
                TerminalInputTextBox.Focus();
            }
            else
            {
                TerminalInputTextBox.Clear();
            }
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
            CurrentProblemStatusTextBlock.Text = "문제 미선택";
        }

        private void DisplayProblem(ProblemDocument problem)
        {
            string title = string.IsNullOrWhiteSpace(problem.Id)
                ? problem.Title
                : $"[{problem.Id}] {problem.Title}";

            ProblemTitleTextBlock.Text = string.IsNullOrWhiteSpace(title) ? "제목 없는 문제" : title;
            ProblemMetaTextBlock.Text = $"시간 제한: {problem.TimeLimitMs} ms / 메모리 제한: {problem.MemoryLimitMb} MB";
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
                problem.Version = problem.Version <= 0 ? 1 : problem.Version;
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
                initialInput: null,
                closeInputAfterInitialInput: false);
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
                    initialInput: sample.Input,
                    closeInputAfterInitialInput: true,
                    showStartBanner: false);

                if (result is null)
                {
                    break;
                }

                bool passed = !result.Stopped
                              && result.ExitCode == 0
                              && CompareOutput(result.StandardOutput, sample.Output);

                if (passed)
                {
                    passedCount++;
                }

                AppendTerminal($"[Sample {sampleNumber}] {(passed ? "PASS" : "FAIL")} | {result.Elapsed.TotalMilliseconds:0} ms");

                if (result.Stopped)
                {
                    AppendTerminal("Result: Stopped");
                }
                else if (result.ExitCode != 0)
                {
                    AppendTerminal($"Result: Runtime Error (ExitCode: {result.ExitCode})");
                }
                else if (!passed)
                {
                    AppendTerminal("Result: Wrong Answer");
                }
                else
                {
                    AppendTerminal("Result: Accepted");
                }

                AppendTerminal("Expected:");
                AppendTerminal(IndentMultiline(sample.Output));
                AppendTerminal("Actual:");
                AppendTerminal(IndentMultiline(result.StandardOutput));
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
            string? initialInput,
            bool closeInputAfterInitialInput,
            bool showStartBanner = true)
        {
            if (_runningProcess is not null && !_runningProcess.HasExited)
            {
                SetStatus("이미 실행 중", isError: true);
                AppendTerminal("[Run] 이미 실행 중인 Python 프로세스가 있습니다. 중지 후 다시 실행하세요.");
                return null;
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "LocalJudge", Guid.NewGuid().ToString("N"));
            string scriptPath = Path.Combine(tempDirectory, DefaultPythonCodeFileName);
            Directory.CreateDirectory(tempDirectory);

            await File.WriteAllTextAsync(scriptPath, code, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();
            var stopwatch = Stopwatch.StartNew();

            var startInfo = new ProcessStartInfo
            {
                FileName = _pythonExecutablePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = tempDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.ArgumentList.Add("-B");
            startInfo.ArgumentList.Add("-u");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["PYTHONUNBUFFERED"] = "1";

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            try
            {
                SetStatus($"{runTitle} 중", isWorking: true);

                if (showStartBanner)
                {
                    AppendTerminal("----------------------------------------");
                    AppendTerminal($"[Run] {runTitle} 시작");
                    AppendTerminal($"[Run] Python: {_pythonExecutablePath}");
                }

                process.Start();

                _runningProcess = process;
                _runningProcessInput = process.StandardInput;
                _runningTempDirectory = tempDirectory;
                _isRunStopRequested = false;

                bool interactiveInputEnabled = !closeInputAfterInitialInput;
                SetRunInputEnabled(interactiveInputEnabled);

                Task stdoutTask = ReadProcessStreamAsync(process.StandardOutput, stdoutBuilder);
                Task stderrTask = ReadProcessStreamAsync(process.StandardError, stderrBuilder);

                if (!string.IsNullOrEmpty(initialInput))
                {
                    await process.StandardInput.WriteAsync(initialInput);
                    await process.StandardInput.FlushAsync();
                }

                if (closeInputAfterInitialInput)
                {
                    process.StandardInput.Close();
                    _runningProcessInput = null;
                    SetRunInputEnabled(false);
                }

                await process.WaitForExitAsync();
                await Task.WhenAll(stdoutTask, stderrTask);

                stopwatch.Stop();

                int exitCode = process.ExitCode;
                bool stopped = _isRunStopRequested;

                if (showStartBanner)
                {
                    AppendTerminal(string.Empty);
                    AppendTerminal("[Run] 프로세스 종료");
                    AppendTerminal($"[Run] ExitCode: {exitCode}");
                    AppendTerminal($"[Run] 실행 시간: {stopwatch.Elapsed.TotalMilliseconds:0} ms");

                    if (stopped)
                    {
                        SetStatus("실행 중지됨", isError: true);
                        AppendTerminal("[Run] 사용자가 실행을 중지했습니다.");
                    }
                    else if (exitCode != 0)
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

                return new PythonExecutionResult(
                    exitCode,
                    stopped,
                    stopwatch.Elapsed,
                    stdoutBuilder.ToString(),
                    stderrBuilder.ToString());
            }
            catch (Win32Exception)
            {
                stopwatch.Stop();
                SetStatus("Python 실행 실패", isError: true);
                AppendTerminal("[Run] Python을 실행하지 못했습니다.");
                AppendTerminal("[Run] Python이 설치되어 있고 PATH에 등록되어 있는지 확인하세요.");
                AppendTerminal($"[Run] 현재 Python 실행 파일 설정: {_pythonExecutablePath}");
                return null;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                SetStatus("실행 실패", isError: true);
                AppendTerminal("[Run] 실행 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                return null;
            }
            finally
            {
                SetRunInputEnabled(false);
                StopRunButton.IsEnabled = false;

                _runningProcessInput = null;

                if (_runningProcess == process)
                {
                    _runningProcess = null;
                }

                process.Dispose();

                try
                {
                    if (Directory.Exists(tempDirectory))
                    {
                        Directory.Delete(tempDirectory, recursive: true);
                    }
                }
                catch
                {
                    // 임시 폴더 삭제 실패는 실행 실패로 처리하지 않음
                }

                _runningTempDirectory = null;
            }
        }

        private async Task ReadProcessStreamAsync(StreamReader reader, StringBuilder captureBuilder)
        {
            char[] buffer = new char[1024];

            while (true)
            {
                int readCount = await reader.ReadAsync(buffer, 0, buffer.Length);

                if (readCount <= 0)
                {
                    break;
                }

                string text = new string(buffer, 0, readCount);
                captureBuilder.Append(text);
                AppendTerminalRaw(text);
            }
        }

        private async void TerminalInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;

            string inputLine = TerminalInputTextBox.Text;
            TerminalInputTextBox.Clear();

            if (_runningProcess is null || _runningProcess.HasExited || _runningProcessInput is null)
            {
                return;
            }

            AppendTerminalRaw(inputLine + Environment.NewLine);

            try
            {
                await _runningProcessInput.WriteLineAsync(inputLine);
                await _runningProcessInput.FlushAsync();
            }
            catch (Exception ex)
            {
                SetStatus("입력 전송 실패", isError: true);
                AppendTerminal("[Run] 표준 입력 전송 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private void StopRunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_runningProcess is null || _runningProcess.HasExited)
            {
                AppendTerminal("[Run] 실행 중인 Python 프로세스가 없습니다.");
                return;
            }

            try
            {
                _isRunStopRequested = true;
                AppendTerminal("[Run] 실행 중지 요청");
                _runningProcess.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                SetStatus("실행 중지 실패", isError: true);
                AppendTerminal("[Run] Python 프로세스를 중지하지 못했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private void SendEofButton_Click(object sender, RoutedEventArgs e)
        {
            if (_runningProcessInput is null)
            {
                AppendTerminal("[Run] 닫을 표준 입력이 없습니다.");
                return;
            }

            try
            {
                _runningProcessInput.Close();
                _runningProcessInput = null;
                SetRunInputEnabled(false);
                AppendTerminal("[Run] 표준 입력을 닫았습니다. (EOF)");
            }
            catch (Exception ex)
            {
                SetStatus("EOF 전송 실패", isError: true);
                AppendTerminal("[Run] 표준 입력을 닫는 중 오류가 발생했습니다.");
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

        private sealed record PythonExecutionResult(
            int ExitCode,
            bool Stopped,
            TimeSpan Elapsed,
            string StandardOutput,
            string StandardError);

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

        private void SubmitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("제출 처리 중", isWorking: true);
            AppendTerminal("[Submit] 제출 이력 저장 기능은 추후 구현합니다.");
            SetStatus("대기 중");
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

            _pythonExecutablePath = dialog.FileName;
            SetStatus("Python 경로 설정 완료");
            AppendTerminal($"[Settings] Python 경로를 설정했습니다: {_pythonExecutablePath}");

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
                var startInfo = new ProcessStartInfo
                {
                    FileName = _pythonExecutablePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                startInfo.ArgumentList.Add("--version");

                using Process? process = Process.Start(startInfo);
                if (process is null)
                {
                    return string.Empty;
                }

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                string versionText = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
                return versionText.Trim();
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
            TerminalInputTextBox.Clear();
            SetStatus("대기 중");
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_runningProcess is not null && !_runningProcess.HasExited)
                {
                    _isRunStopRequested = true;
                    _runningProcess.Kill(entireProcessTree: true);
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
