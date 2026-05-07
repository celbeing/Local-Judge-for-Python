using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
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

                            _latestEditorCode = code;
                            _editorCodeRequest?.TrySetResult(code);
                            _editorCodeRequest = null;
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
            _editorCodeRequest = new TaskCompletionSource<string>();

            string script = JsonSerializer.Serialize(new
            {
                type = "getCode",
                requestId
            });

            CodeEditorWebView.CoreWebView2.PostWebMessageAsJson(script);

            Task completedTask = await Task.WhenAny(
                _editorCodeRequest.Task,
                Task.Delay(1500));

            if (completedTask == _editorCodeRequest.Task)
            {
                return await _editorCodeRequest.Task;
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
            TerminalTextBox.AppendText(Environment.NewLine + message);
            TerminalTextBox.ScrollToEnd();
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

            baseName = Regex.Replace(baseName, "[\\/:*?\"<>|]+", "_").Trim();

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

        private void RunCodeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("실행 준비 중", isWorking: true);
            AppendTerminal("[Run] 일반 실행 기능은 다음 단계에서 Python Runner와 연결합니다.");
            SetStatus("대기 중");
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

            SetStatus("예제 실행 중", isWorking: true);
            AppendTerminal("[Run] 예제 실행 기능은 다음 단계에서 Python Runner와 연결합니다.");
            AppendTerminal($"[Run] 현재 코드 길이: {code.Length}자");
            SetStatus("실행 대기");
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

        private void SubmitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("제출 처리 중", isWorking: true);
            AppendTerminal("[Submit] 제출 이력 저장 기능은 추후 구현합니다.");
            SetStatus("대기 중");
        }

        private void PythonPathMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Python 경로 설정");
            AppendTerminal("[Settings] Python 경로 설정 UI는 추후 구현합니다.");
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
            Close();
        }
    }
}
