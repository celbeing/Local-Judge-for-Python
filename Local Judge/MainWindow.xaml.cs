using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Local_Judge
{
    public partial class MainWindow : Window
    {
        private const string DefaultPythonCode = "import sys\n\n\ndef main():\n    pass\n\n\nif __name__ == \"__main__\":\n    main()\n";
        private string _latestEditorCode = DefaultPythonCode;
        private TaskCompletionSource<string>? _editorCodeRequest;
        private string? _editorCodeRequestId;

        private readonly PythonRunner _pythonRunner = new();
        private readonly JudgeEnvironmentBenchmark _environmentBenchmark;
        private readonly SubmissionHistoryStore _submissionHistoryStore;
        private readonly SubmissionHistoryExporter _submissionHistoryExporter;
        private readonly SubmissionHistoryImportReader _submissionHistoryImportReader;
        private readonly LessonResultInspectionReader _lessonResultInspectionReader;
        private readonly LessonPackageReader _lessonPackageReader;
        private readonly ContestPackageReader _contestPackageReader;
        private readonly ContestProblemNavigator _contestProblemNavigator;
        private readonly ContestResultExporter _contestResultExporter;
        private readonly LocalJudgeSettingsStore _settingsStore;
        private const string ApplicationVersion = "v1.0";
        private const string ApplicationAuthor = "김명서";
        private const string ApplicationIndischoolId = "전라남도교육지원청";
        private const string ApplicationTistoryUrl = "https://celbeing.tistory.com/";
        private const int OutputLimitBytes = PythonExecutionLimits.DefaultOutputLimitBytes;
        private const string ProblemViewerHostName = "localjudge.problem-viewer";
        private const string ProblemAssetsHostName = "localjudge.problem-assets";
        private const string DraftFileExtension = ".py";

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
        private string? _pendingProblemAssetWorkspacePath;
        private LessonContext? _currentLesson;
        private LessonProblemItem? _currentLessonProblem;
        private readonly ContestSessionService _contestSession = new();
        private LocalJudgeUserSettings _userSettings = new();
        private bool _isPythonConnected;
        private bool _isRefreshingLessonExplorer;
        private bool _isRefreshingContestExplorer;
        private bool _isProblemDirty;
        private JudgeEnvironmentBenchmarkResult? _benchmarkResult;
        private bool _isBenchmarkRunning;
        private bool _isSubmitting;
        private ContestContext? _currentContest => _contestSession.CurrentContest;
        private ContestProblemItem? _currentContestProblem => _contestSession.CurrentProblem;
        private bool _contestAutoExportCompleted => _contestSession.AutoExportCompleted;
        private bool _contestAutoExportFailed => _contestSession.AutoExportFailed;
        private bool _isContestAutoExporting => _contestSession.IsAutoExporting;
        private bool _isContestInfoLockedUntilStart => _contestSession.IsInfoLockedUntilStart;
        private string? _activeEditorCodeScopeKey;
        private readonly Dictionary<string, string> _scopedEditorCodeBuffers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EditorDraftTarget> _editorCodeDraftTargets = new(StringComparer.OrdinalIgnoreCase);
        private int _suppressedEditorCodeChangedCount;
        private bool _activeEditorDraftDirty;
        private string? _lastDraftSaveErrorScopeKey;
        private bool _terminalEndsWithLineBreak = true;
        private bool _isProblemViewerReady;
        private readonly DispatcherTimer _contestTimer;
        private readonly DispatcherTimer _draftAutoSaveTimer;
        private readonly string _emptyProblemAssetFolderPath = Path.Combine(Path.GetTempPath(), "LocalJudge", "EmptyProblemAssets");

        public MainWindow()
        {
            _environmentBenchmark = new JudgeEnvironmentBenchmark(_pythonRunner);
            _submissionHistoryStore = new SubmissionHistoryStore(_jsonOptions);
            _submissionHistoryExporter = new SubmissionHistoryExporter(_submissionHistoryStore, _jsonOptions);
            _submissionHistoryImportReader = new SubmissionHistoryImportReader(_jsonOptions);
            _lessonResultInspectionReader = new LessonResultInspectionReader(_jsonOptions);
            _lessonPackageReader = new LessonPackageReader(_jsonOptions);
            _contestPackageReader = new ContestPackageReader(_jsonOptions);
            _contestProblemNavigator = new ContestProblemNavigator(_jsonOptions);
            _contestResultExporter = new ContestResultExporter(_submissionHistoryExporter, _contestProblemNavigator);
            _settingsStore = new LocalJudgeSettingsStore(_jsonOptions);

            InitializeComponent();

            _contestTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _contestTimer.Tick += ContestTimer_Tick;

            _draftAutoSaveTimer = new DispatcherTimer();
            _draftAutoSaveTimer.Tick += DraftAutoSaveTimer_Tick;

            VersionStatusTextBlock.Text = ApplicationVersion;
            LoadUserSettings();
            SetStatus("채점 대기");
            ResetProblemView();
            UpdateContestStatus();
            AppendTerminal("[UI] 화면 구성이 완료되었습니다.");

            _ = InitializeProblemViewerAsync();
            _ = InitializeCodeEditorAsync();
            _ = StartEnvironmentBenchmarkAsync(isManual: false);
        }

        private void LoadUserSettings()
        {
            _userSettings = _settingsStore.Load();
            ApplyDraftAutoSaveSettings(logSetting: false);

            if (string.IsNullOrWhiteSpace(_userSettings.PythonExecutablePath))
            {
                AppendTerminal("[Settings] 저장된 Python 경로가 없습니다. 기본 python 명령으로 연결을 확인합니다.");
                return;
            }

            _pythonRunner.PythonExecutablePath = _userSettings.PythonExecutablePath;
            AppendTerminal($"[Settings] 저장된 Python 경로를 불러왔습니다: {_pythonRunner.PythonExecutablePath}");
        }

        private void SaveUserSettings()
        {
            try
            {
                _settingsStore.Save(_userSettings);
                AppendTerminal($"[Settings] 설정을 저장했습니다: {LocalJudgeSettingsStore.GetSettingsFilePath()}");
            }
            catch (Exception ex)
            {
                AppendTerminal("[Settings] 설정 저장 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private void ApplyInitialDirectory(FileDialog dialog, string? directoryPath)
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
            catch (Exception ex)
            {
                AppendTerminal($"[Settings] 저장 경로 설정을 적용할 수 없습니다: {directoryPath}");
                AppendTerminal(ex.Message);
            }
        }

        private static string FormatConfiguredDirectory(string? directoryPath)
        {
            return string.IsNullOrWhiteSpace(directoryPath)
                ? "미지정"
                : directoryPath;
        }

        private async Task InitializeProblemViewerAsync()
        {
            try
            {
                await ProblemViewerWebView.EnsureCoreWebView2Async();

                string viewerFolderPath = Path.Combine(AppContext.BaseDirectory, "ProblemViewer");

                if (!Directory.Exists(viewerFolderPath))
                {
                    AppendTerminal($"[Problem] ProblemViewer 폴더를 찾을 수 없습니다: {viewerFolderPath}");
                    return;
                }

                Directory.CreateDirectory(_emptyProblemAssetFolderPath);

                ProblemViewerWebView.CoreWebView2.WebMessageReceived += ProblemViewerWebView_WebMessageReceived;
                ProblemViewerWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    ProblemViewerHostName,
                    viewerFolderPath,
                    CoreWebView2HostResourceAccessKind.Allow);

                MapProblemAssetFolder(GetCurrentProblemAssetFolderPath());

                ProblemViewerWebView.Source = new Uri($"https://{ProblemViewerHostName}/index.html");
            }
            catch (Exception ex)
            {
                AppendTerminal("[Problem] WebView2 문제 뷰어 초기화 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private void ProblemViewerWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
                JsonElement root = document.RootElement;

                if (!root.TryGetProperty("type", out JsonElement typeElement))
                {
                    return;
                }

                if (string.Equals(typeElement.GetString(), "viewerReady", StringComparison.Ordinal))
                {
                    _isProblemViewerReady = true;
                    RenderProblemView();
                }
            }
            catch (Exception ex)
            {
                AppendTerminal("[Problem] 문제 뷰어 메시지 처리 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
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
                        SetEditorCode(_latestEditorCode);
                        break;

                    case "codeChanged":
                        if (root.TryGetProperty("code", out JsonElement codeElement))
                        {
                            _latestEditorCode = codeElement.GetString() ?? "";
                            StoreLatestEditorCodeForActiveScope();
                            if (_suppressedEditorCodeChangedCount > 0)
                            {
                                _suppressedEditorCodeChangedCount--;
                            }
                            else
                            {
                                MarkActiveEditorDraftDirty();
                            }
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
                            StoreLatestEditorCodeForActiveScope();
                            MarkActiveEditorDraftDirty();

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
            StoreLatestEditorCodeForActiveScope();

            if (CodeEditorWebView.CoreWebView2 == null)
            {
                return;
            }

            string script = JsonSerializer.Serialize(new
            {
                type = "setCode",
                code
            });

            _suppressedEditorCodeChangedCount++;
            CodeEditorWebView.CoreWebView2.PostWebMessageAsJson(script);
        }

        private void StoreLatestEditorCodeForActiveScope()
        {
            if (string.IsNullOrWhiteSpace(_activeEditorCodeScopeKey))
            {
                return;
            }

            _scopedEditorCodeBuffers[_activeEditorCodeScopeKey] = _latestEditorCode;
        }

        private void MarkActiveEditorDraftDirty()
        {
            if (!string.IsNullOrWhiteSpace(_activeEditorCodeScopeKey)
                && _editorCodeDraftTargets.ContainsKey(_activeEditorCodeScopeKey))
            {
                _activeEditorDraftDirty = true;
            }
        }

        private void ActivateEditorCodeScope(string scopeKey, EditorDraftTarget draftTarget)
        {
            SaveDraftForActiveScope(force: false, logOnSuccess: false);
            StoreLatestEditorCodeForActiveScope();
            _activeEditorCodeScopeKey = scopeKey;
            _editorCodeDraftTargets[scopeKey] = draftTarget;
            _activeEditorDraftDirty = false;

            string code;
            if (_scopedEditorCodeBuffers.TryGetValue(scopeKey, out string? bufferedCode))
            {
                code = bufferedCode;
            }
            else if (TryLoadEditorDraft(draftTarget, out string draftCode, out string loadedDraftPath))
            {
                code = draftCode;
                _scopedEditorCodeBuffers[scopeKey] = code;
                AppendTerminal($"[Draft] 임시 저장 코드를 불러왔습니다: {loadedDraftPath}");
            }
            else
            {
                code = DefaultPythonCode;
            }

            SetEditorCode(code);
        }

        private void DeactivateEditorCodeScope()
        {
            SaveDraftForActiveScope(force: false, logOnSuccess: false);
            StoreLatestEditorCodeForActiveScope();
            _activeEditorCodeScopeKey = null;
            _activeEditorDraftDirty = false;
        }

        private static string CreateLessonEditorCodeScopeKey(LessonContext lesson, LessonProblemItem problem)
        {
            return $"lesson:{lesson.LessonId}:{problem.RelativePath}";
        }

        private static string CreateContestEditorCodeScopeKey(ContestContext contest, ContestProblemItem problem)
        {
            return $"contest:{contest.ContestId}:{problem.RelativePath}";
        }

        private EditorDraftTarget CreateLessonEditorDraftTarget(LessonContext lesson, LessonProblemItem problem)
        {
            return new EditorDraftTarget(
                CreateSessionDraftFilePath(lesson.RootPath, problem.SubmissionKey),
                CreateStableDraftFilePath("Lessons", CreateLessonDraftContextKey(lesson), problem.SubmissionKey),
                FormatLessonProblemName(problem));
        }

        private EditorDraftTarget CreateContestEditorDraftTarget(ContestContext contest, ContestProblemItem problem)
        {
            return new EditorDraftTarget(
                CreateSessionDraftFilePath(contest.RootPath, problem.SubmissionKey),
                CreateStableDraftFilePath("Contests", CreateContestDraftContextKey(contest), problem.SubmissionKey),
                ContestProblemNavigator.FormatProblemName(problem));
        }

        private void SaveDraftForActiveScope(bool force, bool logOnSuccess)
        {
            StoreLatestEditorCodeForActiveScope();

            if (!_userSettings.AutoSaveDraftsEnabled
                || string.IsNullOrWhiteSpace(_activeEditorCodeScopeKey)
                || !_editorCodeDraftTargets.TryGetValue(_activeEditorCodeScopeKey, out EditorDraftTarget? target)
                || (!force && !_activeEditorDraftDirty))
            {
                return;
            }

            bool savedAny = false;
            var failures = new List<string>();
            foreach (string filePath in target.FilePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    WriteDraftFileAtomically(filePath, _latestEditorCode);
                    savedAny = true;
                }
                catch (Exception ex)
                {
                    failures.Add($"{filePath}: {ex.Message}");
                }
            }

            if (savedAny)
            {
                if (failures.Count == 0)
                {
                    _activeEditorDraftDirty = false;
                    _lastDraftSaveErrorScopeKey = null;
                }
                else
                {
                    _activeEditorDraftDirty = true;
                }

                if (logOnSuccess)
                {
                    AppendTerminal($"[Draft] 임시 저장을 완료했습니다: {target.PrimaryFilePath}");
                }
            }

            if (failures.Count > 0
                && !string.Equals(_lastDraftSaveErrorScopeKey, _activeEditorCodeScopeKey, StringComparison.OrdinalIgnoreCase))
            {
                _lastDraftSaveErrorScopeKey = _activeEditorCodeScopeKey;
                AppendTerminal($"[Draft] 임시 저장 중 일부 파일을 저장하지 못했습니다: {target.DisplayName}");
                foreach (string failure in failures)
                {
                    AppendTerminal(failure);
                }
            }
        }

        private bool TryLoadEditorDraft(EditorDraftTarget target, out string code, out string loadedFilePath)
        {
            foreach (string filePath in target.FilePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        continue;
                    }

                    code = File.ReadAllText(filePath, Encoding.UTF8);
                    loadedFilePath = filePath;
                    return true;
                }
                catch (Exception ex)
                {
                    AppendTerminal($"[Draft] 임시 저장 파일을 읽지 못했습니다: {filePath}");
                    AppendTerminal(ex.Message);
                }
            }

            code = string.Empty;
            loadedFilePath = string.Empty;
            return false;
        }

        private void DraftAutoSaveTimer_Tick(object? sender, EventArgs e)
        {
            SaveDraftForActiveScope(force: false, logOnSuccess: false);
        }

        private void ApplyDraftAutoSaveSettings(bool logSetting)
        {
            _userSettings.Normalize();
            _draftAutoSaveTimer.Interval = TimeSpan.FromSeconds(_userSettings.AutoSaveDraftIntervalSeconds);

            if (_userSettings.AutoSaveDraftsEnabled)
            {
                _draftAutoSaveTimer.Start();
            }
            else
            {
                _draftAutoSaveTimer.Stop();
            }

            if (logSetting)
            {
                AppendTerminal(_userSettings.AutoSaveDraftsEnabled
                    ? $"[Draft] 자동 저장을 켰습니다. 주기: {_userSettings.AutoSaveDraftIntervalSeconds}초"
                    : "[Draft] 자동 저장을 껐습니다.");
            }
        }

        private static string CreateSessionDraftFilePath(string rootPath, string problemKey)
        {
            return Path.Combine(rootPath, ".localjudge", "drafts", CreateDraftFileName(problemKey));
        }

        private static string CreateStableDraftFilePath(string kind, string contextKey, string problemKey)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Local Judge",
                "Drafts",
                kind,
                contextKey,
                CreateDraftFileName(problemKey));
        }

        private static string CreateLessonDraftContextKey(LessonContext lesson)
        {
            string context = CreateSourceFingerprint(lesson.SourceZipPath);
            if (string.IsNullOrWhiteSpace(context))
            {
                context = SafeFullPath(lesson.RootPath);
            }

            return CreateShortHash($"lesson\n{context}\n{lesson.Title}");
        }

        private static string CreateContestDraftContextKey(ContestContext contest)
        {
            string context = CreateSourceFingerprint(contest.SourceZipPath);
            if (string.IsNullOrWhiteSpace(context))
            {
                context = SafeFullPath(contest.RootPath);
            }

            return CreateShortHash($"contest\n{context}\n{contest.Title}\n{contest.StartsAt:O}\n{contest.EndsAt:O}");
        }

        private static string CreateSourceFingerprint(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return string.Empty;
            }

            try
            {
                var fileInfo = new FileInfo(sourcePath);
                if (!fileInfo.Exists)
                {
                    return SafeFullPath(sourcePath);
                }

                return string.Join(
                    "\n",
                    fileInfo.FullName,
                    fileInfo.Length.ToString(),
                    fileInfo.LastWriteTimeUtc.Ticks.ToString());
            }
            catch
            {
                return sourcePath;
            }
        }

        private static string SafeFullPath(string path)
        {
            try
            {
                return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
            }
            catch
            {
                return path ?? string.Empty;
            }
        }

        private static string CreateDraftFileName(string problemKey)
        {
            string fileName = Regex.Replace(problemKey ?? string.Empty, @"[\\/:*?""<>|]+", "_");
            fileName = Regex.Replace(fileName, @"\s+", " ").Trim().TrimEnd('.', ' ');

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "problem";
            }

            if (fileName.Length > 96)
            {
                fileName = fileName[..96].TrimEnd('_', '.', ' ');
            }

            return fileName + DraftFileExtension;
        }

        private static string CreateShortHash(string text)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)))
                .ToLowerInvariant()[..16];
        }

        private static void WriteDraftFileAtomically(string filePath, string code)
        {
            string? directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string tempFilePath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempFilePath, code ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(tempFilePath, filePath, overwrite: true);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch
                {
                    // 임시 파일 정리 실패는 원래 저장 오류를 가리지 않도록 무시합니다.
                }

                throw;
            }
        }

        private sealed class EditorDraftTarget
        {
            public EditorDraftTarget(string primaryFilePath, string recoveryFilePath, string displayName)
            {
                PrimaryFilePath = primaryFilePath;
                RecoveryFilePath = recoveryFilePath;
                DisplayName = displayName;
            }

            public string PrimaryFilePath { get; }

            public string RecoveryFilePath { get; }

            public string DisplayName { get; }

            public IEnumerable<string> FilePaths
            {
                get
                {
                    yield return PrimaryFilePath;
                    yield return RecoveryFilePath;
                }
            }
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
            ProgramInputTextBox.Text = string.Empty;
            CurrentProblemStatusTextBlock.Text = "문제 미선택";
            RenderProblemView();
            UpdateProblemCommandState();
        }

        private void DisplayProblem(ProblemDocument problem)
        {
            SampleCaseDocument? firstSample = problem.Samples.Count > 0 ? problem.Samples[0] : null;
            ProgramInputTextBox.Text = firstSample?.Input ?? string.Empty;

            RenderProblemView();
            UpdateCurrentProblemStatus();
            UpdateProblemCommandState();
        }

        private bool EnsureContestSessionAllowsOpening(string actionName)
        {
            if (_currentContest is null)
            {
                return true;
            }

            string message = $"대회가 열려 있는 동안에는 '{actionName}' 작업을 할 수 없습니다. [대회] > [대회 닫기] 후 다시 시도하세요.";
            SetStatus("대회 진행 중", isError: true);
            AppendTerminal($"[Contest] {message}");
            MessageBox.Show(
                message,
                "대회 진행 중",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        private async void OpenLessonMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureContestSessionAllowsOpening("수업 열기"))
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "수업 ZIP 열기",
                Filter = "ZIP 수업 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            try
            {
                OpenLessonMenuItem.IsEnabled = false;
                SetStatus("수업 여는 중", isWorking: true);
                AppendTerminal($"[Lesson] 수업 ZIP을 여는 중입니다: {dialog.FileName}");

                LessonContext lesson = await Task.Run(() => _lessonPackageReader.OpenZip(dialog.FileName));
                SetCurrentLesson(lesson);
                AppendTerminal($"[Lesson] 수업을 열었습니다: {lesson.Title}");
                AppendTerminal($"[Lesson] 작업 폴더: {lesson.RootPath}");

                LessonProblemItem? firstProblem = lesson.Problems.FirstOrDefault();
                if (firstProblem is not null)
                {
                    OpenLessonProblem(firstProblem);
                }
            }
            catch (Exception ex)
            {
                AppendTerminal("[Lesson] 수업을 여는 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                MessageBox.Show(
                    "수업 ZIP 파일을 열 수 없습니다.\n\n" + ex.Message,
                    "수업 열기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                OpenLessonMenuItem.IsEnabled = true;
                if (!_isBenchmarkRunning && !_pythonRunner.IsRunning)
                {
                    SetStatus("채점 대기");
                }
                UpdateProblemCommandState();
            }
        }

        private void CloseLessonMenuItem_Click(object sender, RoutedEventArgs e)
        {
            CloseCurrentLesson();
        }

        private void SetCurrentLesson(LessonContext lesson)
        {
            if (_currentContest is not null)
            {
                AppendTerminal("[Contest] 대회가 열려 있는 동안에는 수업을 열 수 없습니다.");
                return;
            }

            _currentLesson = lesson;
            _currentLessonProblem = null;

            LessonExplorerRow.Height = new GridLength(190);
            LessonExplorerSplitterRow.Height = new GridLength(6);
            LessonExplorerGroupBox.Visibility = Visibility.Visible;
            LessonExplorerGroupBox.Header = "수업";
            LessonExplorerGridSplitter.Visibility = Visibility.Visible;
            CloseLessonMenuItem.IsEnabled = true;
            ExportLessonResultMenuItem.IsEnabled = true;

            LessonTitleTextBlock.Text = $"{lesson.Title} | 문항 {lesson.Problems.Count()}개";
            RefreshLessonExplorer();
            UpdateProblemCommandState();
        }

        private void CloseCurrentLesson()
        {
            DeactivateEditorCodeScope();
            _currentLesson = null;
            _currentLessonProblem = null;
            LessonTreeView.Items.Clear();
            LessonTitleTextBlock.Text = string.Empty;
            LessonExplorerGroupBox.Visibility = Visibility.Collapsed;
            LessonExplorerGridSplitter.Visibility = Visibility.Collapsed;
            LessonExplorerRow.Height = new GridLength(0);
            LessonExplorerSplitterRow.Height = new GridLength(0);
            CloseLessonMenuItem.IsEnabled = false;
            ExportLessonResultMenuItem.IsEnabled = false;

            _currentProblem = null;
            _currentProblemFilePath = null;
            _pendingProblemAssetWorkspacePath = null;
            _isProblemDirty = false;
            ResetProblemView();
            AppendTerminal("[Lesson] 수업을 닫았습니다.");
        }

        private void CreateContestMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureContestSessionAllowsOpening("대회 만들기"))
            {
                return;
            }

            var editor = new ContestCreatorWindow(_jsonOptions, _userSettings.ProblemSaveDirectory)
            {
                Owner = this
            };

            bool? result = editor.ShowDialog();
            if (result != true)
            {
                AppendTerminal("[Contest] 대회 만들기를 취소했습니다.");
                return;
            }

            AppendTerminal("[Contest] 대회 ZIP을 저장했습니다.");
            AppendTerminal($"[Contest] 저장 위치: {editor.SavedFilePath ?? "경로 알 수 없음"}");
        }

        private async void OpenContestMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureContestSessionAllowsOpening("대회 열기"))
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "대회 ZIP 열기",
                Filter = "ZIP 대회 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            var passwordWindow = new ContestPasswordWindow
            {
                Owner = this
            };
            bool? passwordResult = passwordWindow.ShowDialog();
            if (passwordResult != true)
            {
                AppendTerminal("[Contest] 대회 열기를 취소했습니다.");
                return;
            }

            string testCasePassword = passwordWindow.Password;
            bool shouldRestoreIdleStatus = true;
            try
            {
                OpenContestMenuItem.IsEnabled = false;
                SetStatus("대회 여는 중", isWorking: true);
                AppendTerminal($"[Contest] 대회 ZIP을 여는 중입니다: {dialog.FileName}");

                ContestContext contest = await Task.Run(() => _contestPackageReader.OpenZip(dialog.FileName, testCasePassword));
                bool openedAfterEnd = DateTimeOffset.Now > contest.EndsAt;
                SetCurrentContest(contest, suppressAutoExport: openedAfterEnd);
                AppendTerminal($"[Contest] 대회를 열었습니다: {contest.Title}");
                int decryptionFailureCount = contest.Problems.Count(problem => problem.TestCasesDecryptionFailed);
                if (decryptionFailureCount > 0)
                {
                    AppendTerminal($"[Contest] 채점 테스트케이스 복호화 실패: {decryptionFailureCount}개 문항");
                    AppendTerminal("[Contest] 대회 암호가 맞지 않으면 문제 확인은 가능하지만 제출 채점은 진행할 수 없습니다.");
                }

                if (openedAfterEnd)
                {
                    shouldRestoreIdleStatus = false;
                    SetStatus("대회 종료");
                    AppendTerminal("[Contest] 종료된 대회를 열었습니다. 종료 이후에는 제출할 수 없습니다.");
                    AppendTerminal("[Contest] 필요한 경우 [저지] > [대회 결과 내보내기]에서 결과를 내보내세요.");
                }

                if (IsContestProblemOpenAllowed())
                {
                    ContestProblemItem? firstProblem = contest.Problems.FirstOrDefault();
                    if (firstProblem is not null)
                    {
                        OpenContestProblem(firstProblem);
                    }
                }
                else
                {
                    ResetProblemView();
                    CurrentProblemStatusTextBlock.Text = $"현재 대회: {contest.Title} | 시작 전";
                    ShowContestInfo(lockUntilStart: true);
                }
            }
            catch (Exception ex)
            {
                shouldRestoreIdleStatus = false;
                SetStatus("대회 열기 실패", isError: true);
                AppendTerminal("[Contest] 대회를 여는 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                MessageBox.Show(
                    "대회 ZIP 파일을 열 수 없습니다.\n\n" + ex.Message,
                    "대회 열기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                OpenContestMenuItem.IsEnabled = true;
                if (shouldRestoreIdleStatus && !_isBenchmarkRunning && !_pythonRunner.IsRunning)
                {
                    SetStatus("채점 대기");
                }
                UpdateProblemCommandState();
                UpdateContestStatus();
            }
        }

        private void CloseContestMenuItem_Click(object sender, RoutedEventArgs e)
        {
            CloseCurrentContest();
        }

        private void ShowContestInfoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowContestInfo(lockUntilStart: false);
        }

        private void MainWindowRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateContestInfoPopupLayout();
        }

        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            UpdateContestInfoPopupLayout();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(UpdateContestInfoPopupLayout, DispatcherPriority.Loaded);
        }

        private void ShowContestInfo(bool lockUntilStart)
        {
            if (!_contestSession.IsOpen)
            {
                return;
            }

            _contestSession.SetInfoLockUntilStart(lockUntilStart, DateTimeOffset.Now);
            UpdateContestInfoPopupLayout();
            UpdateContestInfoPopup();
            ContestInfoPopup.IsOpen = true;
            Dispatcher.BeginInvoke(() =>
            {
                UpdateContestInfoPopupLayout();
                ContestInfoPopupSizingGrid.Focus();
            }, DispatcherPriority.Loaded);
        }

        private void HideContestInfo()
        {
            _contestSession.ClearInfoLock();
            ContestInfoPopup.IsOpen = false;
        }

        private void ContestInfoCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ContestInfoCloseButton.IsEnabled)
            {
                return;
            }

            HideContestInfo();
        }

        private void UpdateContestInfoPopup()
        {
            if (_currentContest is null)
            {
                return;
            }

            ContestContext contest = _currentContest!;
            DateTimeOffset now = DateTimeOffset.Now;
            string statusText;
            string noticeText;
            bool canClose;
            ContestSessionPhase phase = _contestSession.GetPhase(now);
            if (phase == ContestSessionPhase.BeforeStart)
            {
                statusText = $"대회 시작 전 | 시작까지 {FormatDuration(contest.StartsAt - now)}";
                noticeText = "대회 시작 전에는 이 화면을 닫거나 문항을 열 수 없습니다. 시작 시간이 되면 닫기 버튼이 활성화됩니다.";
                canClose = false;
            }
            else if (phase == ContestSessionPhase.Active)
            {
                statusText = $"대회 진행 중 | 남은 시간 {FormatDuration(contest.EndsAt - now)}";
                noticeText = "대회 정보는 진행 중 언제든 다시 확인할 수 있습니다.";
                canClose = true;
            }
            else
            {
                statusText = "대회 종료";
                noticeText = "대회가 종료되었습니다.";
                canClose = true;
            }

            List<ContestInfoDisplayItem> infoItems = contest.AdditionalInfo
                .Select(item => new ContestInfoDisplayItem(
                    string.IsNullOrWhiteSpace(item.Label) ? "정보" : item.Label.Trim(),
                    string.IsNullOrWhiteSpace(item.Text) ? "-" : item.Text.Trim()))
                .ToList();
            if (infoItems.Count == 0)
            {
                infoItems.Add(new ContestInfoDisplayItem("추가 정보", "-"));
            }

            bool closeEnabled = canClose && !_isContestInfoLockedUntilStart;
            ContestInfoTitleTextBlock.Text = contest.Title;
            ContestInfoStatusTextBlock.Text = statusText;
            ContestInfoTimeTextBlock.Text =
                $"시작: {contest.StartsAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} / 종료: {contest.EndsAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}";
            ContestInfoItemsControl.ItemsSource = infoItems;
            ContestInfoNoticeTextBlock.Text = noticeText;
            ContestInfoCloseButton.IsEnabled = closeEnabled;
            ContestInfoCloseStateTextBlock.Text = closeEnabled
                ? "대회 정보를 닫고 문항을 확인할 수 있습니다."
                : "대회 시작 전에는 닫을 수 없습니다.";
        }

        private void UpdateContestInfoPopupLayout()
        {
            if (MainWindowRoot.ActualWidth <= 0 || MainWindowRoot.ActualHeight <= 0)
            {
                return;
            }

            double width = MainWindowRoot.ActualWidth;
            double height = MainWindowRoot.ActualHeight;
            SetFixedElementSize(ContestInfoPopupSizingGrid, width, height);
            ContestInfoPopup.Width = width;
            ContestInfoPopup.Height = height;
            ContestInfoBox.Width = Math.Min(820, Math.Max(560, width - 140));
            ContestInfoBox.MaxHeight = Math.Max(360, height - 120);
            ContestInfoScrollViewer.MaxHeight = Math.Max(220, height - 230);
            ContestInfoPopupSizingGrid.UpdateLayout();
        }

        private static void SetFixedElementSize(FrameworkElement element, double width, double height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            element.Width = width;
            element.MinWidth = width;
            element.MaxWidth = width;
            element.Height = height;
            element.MinHeight = height;
            element.MaxHeight = height;
        }

        private sealed class ContestInfoDisplayItem
        {
            public ContestInfoDisplayItem(string label, string text)
            {
                Label = label;
                Text = text;
            }

            public string Label { get; }

            public string Text { get; }
        }

        private void SetCurrentContest(ContestContext contest, bool suppressAutoExport = false)
        {
            if (_currentContest is not null)
            {
                CloseCurrentContest(showLog: false);
            }

            if (_currentLesson is not null)
            {
                CloseCurrentLesson();
            }

            _contestSession.Open(contest, suppressAutoExport);

            LessonExplorerRow.Height = new GridLength(190);
            LessonExplorerSplitterRow.Height = new GridLength(6);
            LessonExplorerGroupBox.Visibility = Visibility.Visible;
            LessonExplorerGroupBox.Header = "대회";
            LessonExplorerGridSplitter.Visibility = Visibility.Visible;
            CloseContestMenuItem.IsEnabled = true;
            ShowContestInfoMenuItem.IsEnabled = true;
            ExportContestResultMenuItem.IsEnabled = true;

            LessonTitleTextBlock.Text = $"{contest.Title} | 문항 {contest.Problems.Count}개";
            RefreshContestExplorer();
            _contestTimer.Start();
            UpdateContestStatus();
            UpdateProblemCommandState();
        }

        private void CloseCurrentContest(bool showLog = true)
        {
            DeactivateEditorCodeScope();
            _contestTimer.Stop();
            _contestSession.Close();

            LessonTreeView.Items.Clear();
            LessonTitleTextBlock.Text = string.Empty;
            LessonExplorerGroupBox.Visibility = Visibility.Collapsed;
            LessonExplorerGridSplitter.Visibility = Visibility.Collapsed;
            LessonExplorerRow.Height = new GridLength(0);
            LessonExplorerSplitterRow.Height = new GridLength(0);
            CloseContestMenuItem.IsEnabled = false;
            ShowContestInfoMenuItem.IsEnabled = false;
            ExportContestResultMenuItem.IsEnabled = false;

            _currentProblem = null;
            _currentProblemFilePath = null;
            _pendingProblemAssetWorkspacePath = null;
            _isProblemDirty = false;
            HideContestInfo();
            ResetProblemView();
            UpdateContestStatus();

            if (showLog)
            {
                AppendTerminal("[Contest] 대회를 닫았습니다.");
            }
        }

        private void RefreshContestExplorer(string? selectedProblemRelativePath = null)
        {
            if (_currentContest is null)
            {
                return;
            }

            selectedProblemRelativePath ??= _currentContestProblem?.RelativePath;
            _isRefreshingContestExplorer = true;
            try
            {
                LessonTreeView.Items.Clear();

                foreach (TreeViewItem problemItem in _contestProblemNavigator.CreateProblemTreeItems(
                             _currentContest,
                             selectedProblemRelativePath))
                {
                    LessonTreeView.Items.Add(problemItem);
                }
            }
            finally
            {
                _isRefreshingContestExplorer = false;
            }
        }

        private void RefreshLessonExplorer(string? selectedProblemRelativePath = null)
        {
            if (_currentLesson is null)
            {
                return;
            }

            selectedProblemRelativePath ??= _currentLessonProblem?.RelativePath;
            _isRefreshingLessonExplorer = true;
            try
            {
                LessonTreeView.Items.Clear();

                foreach (LessonSection section in _currentLesson.Sections)
                {
                    var sectionItem = new TreeViewItem
                    {
                        Header = section.Title,
                        IsExpanded = true
                    };

                    foreach (LessonProblemItem problem in section.Problems)
                    {
                        UpdateLessonProblemStatus(problem);
                        var problemItem = new TreeViewItem
                        {
                            Header = new TextBlock
                            {
                                Text = FormatLessonProblemTreeText(problem),
                                Foreground = GetLessonProblemBrush(problem)
                            },
                            Tag = problem,
                            IsSelected = string.Equals(problem.RelativePath, selectedProblemRelativePath, StringComparison.OrdinalIgnoreCase)
                        };

                        sectionItem.Items.Add(problemItem);
                    }

                    LessonTreeView.Items.Add(sectionItem);
                }
            }
            finally
            {
                _isRefreshingLessonExplorer = false;
            }
        }

        private void LessonTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isRefreshingLessonExplorer || _isRefreshingContestExplorer)
            {
                return;
            }

            if (e.NewValue is TreeViewItem { Tag: LessonProblemItem problem })
            {
                if (_currentLessonProblem is not null
                    && string.Equals(_currentLessonProblem.RelativePath, problem.RelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                OpenLessonProblem(problem);
            }
            else if (e.NewValue is TreeViewItem { Tag: ContestProblemItem contestProblem })
            {
                if (_currentContestProblem is not null
                    && string.Equals(_currentContestProblem.RelativePath, contestProblem.RelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                OpenContestProblem(contestProblem);
            }
        }

        private void OpenLessonProblem(LessonProblemItem lessonProblem)
        {
            if (_currentLesson is not null)
            {
                ActivateEditorCodeScope(
                    CreateLessonEditorCodeScopeKey(_currentLesson, lessonProblem),
                    CreateLessonEditorDraftTarget(_currentLesson, lessonProblem));
            }

            _currentLessonProblem = lessonProblem;
            _currentProblem = lessonProblem.Problem;
            _currentProblemFilePath = lessonProblem.FilePath;
            _pendingProblemAssetWorkspacePath = null;
            _isProblemDirty = false;

            DisplayProblem(lessonProblem.Problem);
            RefreshLessonExplorer(lessonProblem.RelativePath);
            AppendTerminal($"[Lesson] 문항을 열었습니다: {FormatLessonProblemName(lessonProblem)}");
        }

        private void OpenContestProblem(ContestProblemItem contestProblem)
        {
            if (!IsContestProblemOpenAllowed())
            {
                SetStatus("대회 시작 전", isError: true);
                ShowContestInfo(lockUntilStart: true);
                RefreshContestExplorer(_currentContestProblem?.RelativePath);
                return;
            }

            if (_currentContest is not null)
            {
                ActivateEditorCodeScope(
                    CreateContestEditorCodeScopeKey(_currentContest, contestProblem),
                    CreateContestEditorDraftTarget(_currentContest, contestProblem));
            }

            _contestSession.SelectProblem(contestProblem);
            _currentProblem = contestProblem.Problem;
            _currentProblemFilePath = contestProblem.FilePath;
            _pendingProblemAssetWorkspacePath = null;
            _isProblemDirty = false;

            DisplayProblem(contestProblem.Problem);
            HideContestInfo();
            RefreshContestExplorer(contestProblem.RelativePath);
            AppendTerminal($"[Contest] 문항을 열었습니다: {ContestProblemNavigator.FormatProblemName(contestProblem)}");
        }

        private void UpdateLessonProblemStatus(LessonProblemItem problem)
        {
            IReadOnlyList<SubmissionAttemptHistoryItem> attempts = LoadLessonAttempts(problem);
            problem.AttemptCount = attempts.Count;
            problem.HasAccepted = attempts.Any(item => string.Equals(item.Attempt.Verdict, "AC", StringComparison.OrdinalIgnoreCase));
            problem.LastVerdict = attempts
                .OrderBy(item => item.Attempt.SubmittedAt)
                .LastOrDefault()
                ?.Attempt
                .Verdict ?? string.Empty;
        }

        private IReadOnlyList<SubmissionAttemptHistoryItem> LoadLessonAttempts(LessonProblemItem problem)
        {
            if (_currentLesson is null)
            {
                return Array.Empty<SubmissionAttemptHistoryItem>();
            }

            string problemDirectory = Path.Combine(_currentLesson.SubmissionsRoot, problem.SubmissionKey);
            if (!Directory.Exists(problemDirectory))
            {
                return Array.Empty<SubmissionAttemptHistoryItem>();
            }

            var attempts = new List<SubmissionAttemptHistoryItem>();
            foreach (string filePath in Directory.EnumerateFiles(problemDirectory, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    SubmissionAttemptDocument? attempt = JsonSerializer.Deserialize<SubmissionAttemptDocument>(json, _jsonOptions);
                    if (attempt is null)
                    {
                        continue;
                    }

                    attempts.Add(new SubmissionAttemptHistoryItem(filePath, attempt));
                }
                catch
                {
                    // Ignore malformed lesson submission files.
                }
            }

            return attempts
                .OrderByDescending(item => item.Attempt.SubmittedAt)
                .ToList();
        }

        private IReadOnlyList<SubmissionAttemptHistoryItem> LoadContestAttempts(ContestProblemItem problem)
        {
            if (_currentContest is null)
            {
                return Array.Empty<SubmissionAttemptHistoryItem>();
            }

            return _contestProblemNavigator.LoadAttempts(_currentContest, problem);
        }

        private static string FormatLessonProblemTreeText(LessonProblemItem problem)
        {
            string name = FormatLessonProblemName(problem);
            if (problem.AttemptCount == 0 || problem.HasAccepted || string.IsNullOrWhiteSpace(problem.LastVerdict))
            {
                return name;
            }

            return $"{name} ({problem.LastVerdict})";
        }

        private static string FormatLessonProblemName(LessonProblemItem problem)
        {
            return string.IsNullOrWhiteSpace(problem.Problem.Id)
                ? problem.Problem.Title
                : $"[{problem.Problem.Id}] {problem.Problem.Title}";
        }

        private static Brush GetLessonProblemBrush(LessonProblemItem problem)
        {
            if (problem.HasAccepted)
            {
                return Brushes.ForestGreen;
            }

            return problem.AttemptCount > 0
                ? Brushes.Firebrick
                : Brushes.Black;
        }

        private void RenderProblemView()
        {
            if (!_isProblemViewerReady || ProblemViewerWebView.CoreWebView2 is null)
            {
                return;
            }

            MapProblemAssetFolder(GetCurrentProblemAssetFolderPath());

            object problemPayload = _currentProblem is null
                ? new
                {
                    emptyState = true,
                    message = "문제를 불러와주세요."
                }
                : new
                {
                    emptyState = false,
                    id = _currentProblem.Id,
                    title = _currentProblem.Title,
                    authorName = _currentProblem.AuthorName,
                    source = _currentProblem.Source,
                    timeLimitMs = _currentProblem.TimeLimitMs,
                    memoryLimitMb = _currentProblem.MemoryLimitMb,
                    statementFormat = _currentProblem.StatementFormat,
                    description = _currentProblem.Description,
                    inputFormat = _currentProblem.InputFormat,
                    outputFormat = _currentProblem.OutputFormat,
                    samples = _currentProblem.Samples,
                    testCaseCount = _currentProblem.TestCases.Count
                };

            string json = JsonSerializer.Serialize(new
            {
                type = "renderProblem",
                assetBaseUrl = $"https://{ProblemAssetsHostName}/",
                problem = problemPayload
            }, _jsonOptions);

            ProblemViewerWebView.CoreWebView2.PostWebMessageAsJson(json);
        }

        private string? GetCurrentProblemAssetFolderPath()
        {
            return string.IsNullOrWhiteSpace(_currentProblemFilePath)
                ? null
                : ProblemAssetUtilities.GetAssetFolderPath(_currentProblemFilePath);
        }

        private void MapProblemAssetFolder(string? folderPath)
        {
            if (ProblemViewerWebView.CoreWebView2 is null)
            {
                return;
            }

            string assetFolderPath = !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath)
                ? folderPath
                : _emptyProblemAssetFolderPath;

            Directory.CreateDirectory(assetFolderPath);
            ProblemViewerWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                ProblemAssetsHostName,
                assetFolderPath,
                CoreWebView2HostResourceAccessKind.Allow);
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
                if (_currentContest is not null)
                {
                    CurrentProblemStatusTextBlock.Text = $"현재 대회: {_currentContest.Title} | 문제 미선택";
                }
                else
                {
                    CurrentProblemStatusTextBlock.Text = _currentLesson is null
                        ? "문제 미선택"
                        : $"현재 수업: {_currentLesson.Title} | 문제 미선택";
                }
                return;
            }

            string title = string.IsNullOrWhiteSpace(_currentProblem.Id)
                ? _currentProblem.Title
                : $"[{_currentProblem.Id}] {_currentProblem.Title}";
            string saveState = _isProblemDirty ? "저장 안 됨" : "저장됨";
            string pathState = string.IsNullOrWhiteSpace(_currentProblemFilePath)
                ? "새 문제"
                : Path.GetFileName(_currentProblemFilePath);

            if (_currentLesson is not null && _currentLessonProblem is not null)
            {
                CurrentProblemStatusTextBlock.Text = $"현재 수업: {_currentLesson.Title} | {_currentLessonProblem.SectionTitle} | {title}";
                return;
            }

            if (_currentContest is not null && _currentContestProblem is not null)
            {
                CurrentProblemStatusTextBlock.Text = $"현재 대회: {_currentContest.Title} | {_currentContestProblem.Label} | {title}";
                return;
            }

            CurrentProblemStatusTextBlock.Text = $"현재 문제: {title} | {saveState} | {pathState}";
        }

        private void UpdateProblemCommandState()
        {
            bool hasProblem = _currentProblem is not null;
            bool isLessonProblem = ResolveCurrentLessonProblem() is not null;
            bool isContestProblem = ResolveCurrentContestProblem() is not null;
            bool isContestOpen = _currentContest is not null;
            bool isContestProblemOpen = !isContestProblem || IsContestProblemOpenAllowed();
            bool isContestProblemSubmittable = !isContestProblem || IsContestActive();
            bool isJudgeRuntimeReady = _isPythonConnected
                                       && _benchmarkResult?.Succeeded == true
                                       && !_isBenchmarkRunning
                                       && !_pythonRunner.IsRunning;

            NewProblemMenuItem.IsEnabled = !isContestOpen;
            NewProblemButton.IsEnabled = !isContestOpen;
            LoadProblemMenuItem.IsEnabled = !isContestOpen;
            LoadProblemButton.IsEnabled = !isContestOpen;
            OpenLessonMenuItem.IsEnabled = !isContestOpen;
            CreateContestMenuItem.IsEnabled = !isContestOpen;
            OpenContestMenuItem.IsEnabled = !isContestOpen;
            RunCodeMenuItem.IsEnabled = isJudgeRuntimeReady;
            RunCodeButton.IsEnabled = isJudgeRuntimeReady;
            EditProblemMenuItem.IsEnabled = hasProblem && !isLessonProblem && !isContestProblem;
            EditProblemButton.IsEnabled = hasProblem && !isLessonProblem && !isContestProblem;
            RunSampleMenuItem.IsEnabled = hasProblem && isJudgeRuntimeReady && isContestProblemOpen;
            RunSampleButton.IsEnabled = hasProblem && isJudgeRuntimeReady && isContestProblemOpen;
            SubmitMenuItem.IsEnabled = hasProblem && isJudgeRuntimeReady && isContestProblemSubmittable;
            SubmitButton.IsEnabled = hasProblem && isJudgeRuntimeReady && isContestProblemSubmittable;
            SubmissionHistoryMenuItem.IsEnabled = hasProblem;
            ExportSubmissionHistoryMenuItem.IsEnabled = hasProblem && !isLessonProblem && !isContestProblem;
            BenchmarkMenuItem.IsEnabled = !_isBenchmarkRunning && !_pythonRunner.IsRunning;
        }

        private void ContestTimer_Tick(object? sender, EventArgs e)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            UpdateContestStatus();

            _contestSession.ReleaseInfoLockIfProblemOpenAllowed(now);

            if (ContestInfoPopup.IsOpen)
            {
                UpdateContestInfoPopupLayout();
                UpdateContestInfoPopup();
            }

            RefreshContestExplorer(_currentContestProblem?.RelativePath);
            UpdateProblemCommandState();

            if (_contestSession.ShouldAutoExport(now, _isSubmitting, _pythonRunner.IsRunning))
            {
                TryAutoExportContestResult();
            }
        }

        private void UpdateContestStatus()
        {
            ContestContext? contest = _currentContest;
            if (contest is null)
            {
                ContestStatusTextBlock.Text = "대회: 없음";
                return;
            }

            DateTimeOffset now = DateTimeOffset.Now;
            ContestSessionPhase phase = _contestSession.GetPhase(now);
            if (phase == ContestSessionPhase.BeforeStart)
            {
                ContestStatusTextBlock.Text = $"대회: 시작 전 | 시작까지 {FormatDuration(contest.StartsAt - now)}";
                return;
            }

            if (phase == ContestSessionPhase.Active)
            {
                ContestStatusTextBlock.Text = $"대회: 진행 중 | 남은 시간 {FormatDuration(contest.EndsAt - now)}";
                return;
            }

            ContestStatusTextBlock.Text = _contestAutoExportCompleted
                ? "대회: 종료 | 결과 내보냄"
                : _contestAutoExportFailed
                ? "대회: 종료 | 결과 내보내기 실패"
                : "대회: 종료";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                duration = TimeSpan.Zero;
            }

            int totalHours = (int)Math.Floor(duration.TotalHours);
            return $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        private bool IsContestProblemOpenAllowed()
        {
            return _contestSession.CanOpenProblem(DateTimeOffset.Now);
        }

        private bool IsContestActive()
        {
            return _contestSession.IsActive(DateTimeOffset.Now);
        }

        private bool IsContestEnded()
        {
            return _contestSession.IsEnded(DateTimeOffset.Now);
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

            _isBenchmarkRunning = true;
            _benchmarkResult = null;
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
                if (!await EnsurePythonConnectionAsync())
                {
                    return;
                }

                JudgeEnvironmentBenchmarkResult result = await _environmentBenchmark.RunAsync();

                if (result.Succeeded)
                {
                    _benchmarkResult = result;
                    SetStatus("채점 환경 준비 완료");
                    AppendBenchmarkSummary(result);
                }
                else
                {
                    _benchmarkResult = null;
                    SetStatus("채점 환경 점검 실패", isError: true);
                    AppendTerminal($"[Benchmark] 실패: {result.ErrorMessage}");
                    AppendTerminal("[Benchmark] 벤치마크가 성공하기 전까지 실행과 제출을 사용할 수 없습니다.");
                }
            }
            catch (Exception ex)
            {
                _benchmarkResult = null;
                SetStatus("채점 환경 점검 실패", isError: true);
                AppendTerminal("[Benchmark] 벤치마크 실행 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                AppendTerminal("[Benchmark] 벤치마크가 성공하기 전까지 실행과 제출을 사용할 수 없습니다.");
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

        private async Task<bool> EnsurePythonConnectionAsync()
        {
            try
            {
                string versionText = await _pythonRunner.GetVersionTextAsync();
                if (string.IsNullOrWhiteSpace(versionText))
                {
                    throw new InvalidOperationException("Python 버전 정보를 읽을 수 없습니다.");
                }

                _isPythonConnected = true;
                AppendTerminal($"[Settings] Python 연결 확인: {versionText}");
                return true;
            }
            catch (Exception ex)
            {
                _isPythonConnected = false;
                _benchmarkResult = null;

                SetStatus("Python 연결 필요", isError: true);
                AppendTerminal("[Settings] Python 실행 파일을 확인할 수 없습니다.");
                AppendTerminal($"[Settings] 현재 Python 실행 파일 설정: {_pythonRunner.PythonExecutablePath}");
                AppendTerminal("[Settings] [도구] > [Python 경로 설정]에서 python.exe를 선택하세요.");
                AppendTerminal(ex.Message);
                return false;
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
            if (!_isPythonConnected)
            {
                SetStatus("Python 연결 필요", isError: true);
                AppendTerminal("[Run] Python 연결과 채점 환경 벤치마크가 완료된 뒤 실행할 수 있습니다.");
                AppendTerminal("[Run] [도구] > [Python 경로 설정]에서 python.exe를 선택하세요.");
                return false;
            }

            if (_isBenchmarkRunning)
            {
                SetStatus("채점 환경 점검 중", isError: true);
                AppendTerminal("[Benchmark] 채점 환경 벤치마크가 끝난 뒤 실행할 수 있습니다.");
                return false;
            }

            if (_benchmarkResult?.Succeeded != true)
            {
                SetStatus("채점 환경 미준비", isError: true);
                AppendTerminal("[Benchmark] 채점 환경 벤치마크가 성공적으로 완료된 뒤 실행할 수 있습니다.");
                return false;
            }

            return true;
        }

        private void NewProblemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureContestSessionAllowsOpening("새 문제 만들기"))
            {
                return;
            }

            var editor = new ProblemEditorWindow(defaultProblemSaveDirectory: _userSettings.ProblemSaveDirectory)
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

            var editor = new ProblemEditorWindow(_currentProblem, _currentProblemFilePath)
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
            _pendingProblemAssetWorkspacePath = editor.AssetWorkspacePath;
            _isProblemDirty = true;

            DisplayProblem(_currentProblem);
            AppendTerminal($"[Problem] 문제를 수정했습니다: {_currentProblem.Title}");

            SaveCurrentProblemWithDialogIfNeeded();
        }

        private void LoadProblemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureContestSessionAllowsOpening("문제 불러오기"))
            {
                return;
            }

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

                NormalizeProblemDocument(problem, defaultToMarkdownLatex: false);

                DeactivateEditorCodeScope();
                _currentProblem = problem;
                _currentProblemFilePath = dialog.FileName;
                _pendingProblemAssetWorkspacePath = null;
                _currentLessonProblem = null;
                _isProblemDirty = false;
                RefreshLessonExplorer();

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
            ApplyInitialDirectory(dialog, _userSettings.ProblemSaveDirectory);

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
            ApplyInitialDirectory(dialog, _userSettings.ProblemSaveDirectory);

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
                string? previousProblemFilePath = _currentProblemFilePath;
                string json = JsonSerializer.Serialize(_currentProblem, _jsonOptions);
                File.WriteAllText(filePath, json);

                string? sourceAssetFolderPath = _pendingProblemAssetWorkspacePath;
                if (string.IsNullOrWhiteSpace(sourceAssetFolderPath)
                    && !string.IsNullOrWhiteSpace(previousProblemFilePath)
                    && !Path.GetFullPath(previousProblemFilePath).Equals(Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase))
                {
                    sourceAssetFolderPath = ProblemAssetUtilities.GetAssetFolderPath(previousProblemFilePath);
                }

                if (!string.IsNullOrWhiteSpace(sourceAssetFolderPath) && Directory.Exists(sourceAssetFolderPath))
                {
                    string targetAssetFolderPath = ProblemAssetUtilities.GetAssetFolderPath(filePath);
                    ProblemAssetUtilities.CopyAssetFolder(sourceAssetFolderPath, targetAssetFolderPath);
                }

                _currentProblemFilePath = filePath;
                _pendingProblemAssetWorkspacePath = null;
                _isProblemDirty = false;
                UpdateCurrentProblemStatus();
                RenderProblemView();

                AppendTerminal("[Problem] 문제 JSON 저장이 완료되었습니다.");
                AppendTerminal($"[Problem] 저장 위치: {filePath}");
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

        private static string CreateDefaultLessonResultExportFileName(LessonContext lesson)
        {
            string baseName = Regex.Replace(lesson.Title, @"[\\/:*?""<>|]+", "_").Trim();

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "lesson";
            }

            return baseName + "_result.zip";
        }

        private static void NormalizeProblemDocument(ProblemDocument problem, bool defaultToMarkdownLatex)
        {
            bool useMarkdownLatexByDefault = defaultToMarkdownLatex
                                             || problem.Version >= ProblemAssetUtilities.CurrentProblemVersion;
            problem.Samples ??= new();
            problem.TestCases ??= new();
            problem.Assets ??= new();
            problem.AuthorName ??= string.Empty;
            problem.Source ??= string.Empty;
            problem.Description ??= string.Empty;
            problem.InputFormat ??= string.Empty;
            problem.OutputFormat ??= string.Empty;
            problem.StatementFormat = ProblemAssetUtilities.NormalizeStatementFormat(
                problem.StatementFormat,
                useMarkdownLatexByDefault);
            problem.Version = problem.Version <= 0 ? 3 : Math.Max(problem.Version, 3);
            problem.TimeLimitMs = problem.TimeLimitMs <= 0 ? 2000 : problem.TimeLimitMs;
            problem.MemoryLimitMb = problem.MemoryLimitMb <= 0 ? 128 : problem.MemoryLimitMb;
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

            if (_benchmarkResult?.Succeeded != true)
            {
                throw new InvalidOperationException("채점 환경 벤치마크가 완료되지 않았습니다.");
            }

            JudgeEnvironmentBenchmarkResult calibration = _benchmarkResult;

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
            DateTimeOffset submittedAt,
            List<SubmissionTestResultDocument> testResults)
        {
            JudgeEnvironmentBenchmarkResult benchmark = _benchmarkResult
                ?? throw new InvalidOperationException("채점 환경 벤치마크가 완료되지 않았습니다.");

            return new SubmissionAttemptDocument
            {
                AttemptId = SubmissionHistoryStore.CreateAttemptId(submittedAt),
                SubmittedAt = submittedAt,
                Problem = CreateSubmissionProblemDocument(problem),
                ProblemFilePath = problemFilePath ?? string.Empty,
                Verdict = verdict,
                PassedCount = passedCount,
                TotalCount = totalCount,
                Language = "Python",
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
                string filePath;
                ContestProblemItem? contestProblem = ResolveCurrentContestProblem();
                LessonProblemItem? lessonProblem = ResolveCurrentLessonProblem();
                if (_currentContest is not null && contestProblem is not null)
                {
                    filePath = SaveContestSubmissionAttempt(attempt, _currentContest, contestProblem);
                    RefreshContestExplorer(contestProblem.RelativePath);
                    AppendTerminal($"[Submit] 대회 제출 이력을 저장했습니다: {filePath}");
                    AppendTerminal($"[Submit] 대회 작업 폴더: {_currentContest.RootPath}");
                }
                else if (_currentLesson is not null && lessonProblem is not null)
                {
                    filePath = SaveLessonSubmissionAttempt(attempt, _currentLesson, lessonProblem);
                    RefreshLessonExplorer(lessonProblem.RelativePath);
                    AppendTerminal($"[Submit] 수업 제출 이력을 저장했습니다: {filePath}");
                    AppendTerminal($"[Submit] 수업 작업 폴더: {_currentLesson.RootPath}");
                }
                else
                {
                    filePath = _submissionHistoryStore.SaveAttempt(attempt);
                    AppendTerminal($"[Submit] 제출 이력을 저장했습니다: {filePath}");
                }
            }
            catch (Exception ex)
            {
                AppendTerminal("[Submit] 제출 이력 저장 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
            }
        }

        private LessonProblemItem? ResolveCurrentLessonProblem()
        {
            if (_currentLesson is null || _currentProblem is null)
            {
                return null;
            }

            if (_currentLessonProblem is not null
                && IsSamePath(_currentLessonProblem.FilePath, _currentProblemFilePath))
            {
                return _currentLessonProblem;
            }

            if (!string.IsNullOrWhiteSpace(_currentProblemFilePath))
            {
                LessonProblemItem? pathMatch = _currentLesson.Problems.FirstOrDefault(problem =>
                    IsSamePath(problem.FilePath, _currentProblemFilePath));
                if (pathMatch is not null)
                {
                    _currentLessonProblem = pathMatch;
                    return pathMatch;
                }
            }

            List<LessonProblemItem> documentMatches = _currentLesson.Problems
                .Where(problem => ReferenceEquals(problem.Problem, _currentProblem))
                .ToList();
            if (documentMatches.Count == 1)
            {
                _currentLessonProblem = documentMatches[0];
                return documentMatches[0];
            }

            return null;
        }

        private ContestProblemItem? ResolveCurrentContestProblem()
        {
            if (_currentContest is null || _currentProblem is null)
            {
                return null;
            }

            ContestProblemItem? resolvedProblem = _contestProblemNavigator.ResolveCurrentProblem(
                _currentContest,
                _currentContestProblem,
                _currentProblem,
                _currentProblemFilePath);
            if (resolvedProblem is not null)
            {
                _contestSession.SelectProblem(resolvedProblem);
            }

            return resolvedProblem;
        }

        private static bool IsSamePath(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPathInsideDirectory(string filePath, string directoryPath)
        {
            string fullFilePath = Path.GetFullPath(filePath);
            string fullDirectoryPath = Path.GetFullPath(directoryPath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            return fullFilePath.StartsWith(fullDirectoryPath, StringComparison.OrdinalIgnoreCase);
        }

        private string SaveLessonSubmissionAttempt(
            SubmissionAttemptDocument attempt,
            LessonContext lesson,
            LessonProblemItem lessonProblem)
        {
            attempt.LessonId = lesson.LessonId;
            attempt.LessonTitle = lesson.Title;
            attempt.SectionTitle = lessonProblem.SectionTitle;
            attempt.ProblemRelativePath = lessonProblem.RelativePath;

            string problemDirectory = Path.Combine(lesson.SubmissionsRoot, lessonProblem.SubmissionKey);
            Directory.CreateDirectory(problemDirectory);

            string attemptId = string.IsNullOrWhiteSpace(attempt.AttemptId)
                ? SubmissionHistoryStore.CreateAttemptId(attempt.SubmittedAt)
                : Regex.Replace(attempt.AttemptId, @"[\\/:*?""<>|]+", "_");
            string filePath = Path.Combine(problemDirectory, attemptId + ".json");
            string json = JsonSerializer.Serialize(attempt, _jsonOptions);
            File.WriteAllText(filePath, json);
            return filePath;
        }

        private string SaveContestSubmissionAttempt(
            SubmissionAttemptDocument attempt,
            ContestContext contest,
            ContestProblemItem contestProblem)
        {
            ContestSubmissionSaveResult saveResult = _contestProblemNavigator.SaveSubmissionAttempt(
                attempt,
                contest,
                contestProblem);
            foreach (string failure in saveResult.Failures)
            {
                AppendTerminal($"[Submit] 대회 제출 이력 복사본 저장 실패: {failure}");
            }

            return saveResult.FilePath;
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
            ContestProblemItem? activeContestProblem = ResolveCurrentContestProblem();
            if (activeContestProblem is not null && !IsContestActive())
            {
                SetStatus(IsContestEnded() ? "대회 종료" : "대회 시작 전", isError: true);
                AppendTerminal(IsContestEnded()
                    ? "[Contest] 대회가 종료되어 새 제출을 할 수 없습니다."
                    : "[Contest] 대회 시작 전에는 제출할 수 없습니다.");
                UpdateProblemCommandState();
                return;
            }

            DateTimeOffset submittedAt = DateTimeOffset.Now;

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

            if (activeContestProblem?.TestCasesDecryptionFailed == true)
            {
                SetStatus("채점 테스트 복호화 실패", isError: true);
                AppendTerminal("[Contest] 채점 테스트케이스를 복호화할 수 없습니다.");
                AppendTerminal("[Contest] 대회 암호를 확인한 뒤 대회를 다시 열어 주세요.");
                MessageBox.Show(
                    "채점 테스트케이스를 복호화할 수 없습니다.\n대회 암호를 확인한 뒤 대회를 다시 열어 주세요.",
                    "대회 암호",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (problem.TestCases.Count == 0)
            {
                SetStatus("채점 테스트가 없습니다", isError: true);
                AppendTerminal("[Submit] 현재 문제에 등록된 채점 테스트케이스가 없습니다.");
                return;
            }

            _isSubmitting = true;
            try
            {
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
                    submittedAt,
                    testResults));
            }
            finally
            {
                _isSubmitting = false;
                UpdateProblemCommandState();

                if (_contestSession.ShouldAutoExport(DateTimeOffset.Now, _isSubmitting, _pythonRunner.IsRunning))
                {
                    TryAutoExportContestResult();
                }
            }
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
                ContestProblemItem? contestProblem = ResolveCurrentContestProblem();
                LessonProblemItem? lessonProblem = ResolveCurrentLessonProblem();
                IReadOnlyList<SubmissionAttemptHistoryItem> historyItems =
                    _currentContest is not null && contestProblem is not null
                        ? LoadContestAttempts(contestProblem)
                        : _currentLesson is not null && lessonProblem is not null
                        ? LoadLessonAttempts(lessonProblem)
                        : _submissionHistoryStore.LoadAttemptsForProblem(problemDocument);

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
                ApplyInitialDirectory(dialog, _userSettings.SubmissionHistoryExportDirectory);

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

        private void InspectSubmissionHistoryFileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "제출 이력 파일 확인",
                Filter = "ZIP 제출 이력 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            try
            {
                SubmissionHistoryInspectionDocument document = _submissionHistoryImportReader.ReadZip(dialog.FileName);
                var window = new SubmissionHistoryFileWindow(document)
                {
                    Owner = this
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                AppendTerminal("[Submit] 제출 이력 파일을 확인하는 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                MessageBox.Show(
                    "제출 이력 파일을 읽을 수 없습니다.\n\n" + ex.Message,
                    "제출 이력 파일 확인",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExportLessonResultMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLesson is null)
            {
                AppendTerminal("[Lesson] 수업 결과를 내보내려면 먼저 수업을 여세요.");
                MessageBox.Show(
                    "먼저 수업을 여세요.",
                    "수업 결과 내보내기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                if (!HasLessonSubmissionFiles(_currentLesson))
                {
                    AppendTerminal("[Lesson] 내보낼 제출 기록이 없습니다.");
                    MessageBox.Show(
                        "제출 기록이 없습니다.",
                        "수업 결과 내보내기",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = "수업 결과 내보내기",
                    Filter = "ZIP 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*",
                    DefaultExt = ".zip",
                    FileName = CreateDefaultLessonResultExportFileName(_currentLesson)
                };

                bool? result = dialog.ShowDialog(this);
                if (result != true)
                {
                    return;
                }

                if (IsPathInsideDirectory(dialog.FileName, _currentLesson.RootPath))
                {
                    AppendTerminal("[Lesson] 수업 결과 ZIP은 현재 수업 작업 폴더 바깥에 저장해야 합니다.");
                    MessageBox.Show(
                        "수업 결과 파일은 현재 수업 작업 폴더 바깥에 저장해 주세요.",
                        "수업 결과 내보내기",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                int fileCount = ExportLessonResultZip(_currentLesson, dialog.FileName);
                AppendTerminal($"[Lesson] 수업 결과를 내보냈습니다: {dialog.FileName}");
                AppendTerminal($"[Lesson] 내보낸 파일: {fileCount}개");
                MessageBox.Show(
                    "수업 결과를 내보냈습니다.",
                    "수업 결과 내보내기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendTerminal("[Lesson] 수업 결과 내보내기 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                MessageBox.Show(
                    "수업 결과를 내보낼 수 없습니다.\n\n" + ex.Message,
                    "수업 결과 내보내기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static bool HasLessonSubmissionFiles(LessonContext lesson)
        {
            return Directory.Exists(lesson.SubmissionsRoot)
                   && Directory.EnumerateFiles(lesson.SubmissionsRoot, "*.json", SearchOption.AllDirectories).Any();
        }

        private static int ExportLessonResultZip(LessonContext lesson, string destinationFilePath)
        {
            string? destinationDirectory = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (File.Exists(destinationFilePath))
            {
                File.Delete(destinationFilePath);
            }

            int fileCount = 0;
            using var outputStream = File.Create(destinationFilePath);
            using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create);

            foreach (string filePath in Directory.EnumerateFiles(lesson.RootPath, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(lesson.RootPath, filePath);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                string entryName = relativePath.Replace(Path.DirectorySeparatorChar, '/');
                ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using Stream entryStream = entry.Open();
                using FileStream inputStream = File.OpenRead(filePath);
                inputStream.CopyTo(entryStream);
                fileCount++;
            }

            return fileCount;
        }

        private void ExportContestResultMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_currentContest is null)
            {
                AppendTerminal("[Contest] 대회 결과를 내보내려면 먼저 대회를 여세요.");
                MessageBox.Show(
                    "먼저 대회를 여세요.",
                    "대회 결과 내보내기",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "대회 결과 내보내기",
                Filter = "ZIP 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*",
                DefaultExt = ".zip",
                FileName = ContestResultExporter.CreateDefaultExportFileName(_currentContest)
            };
            ApplyInitialDirectory(dialog, _userSettings.SubmissionHistoryExportDirectory);

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            ExportContestResult(_currentContest, dialog.FileName, isAutomatic: false);
        }

        private void TryAutoExportContestResult()
        {
            if (!_contestSession.ShouldAutoExport(DateTimeOffset.Now, _isSubmitting, _pythonRunner.IsRunning))
            {
                return;
            }

            ContestContext contest = _currentContest!;
            string destinationFilePath;
            try
            {
                string exportDirectory = _contestResultExporter.GetAutoExportDirectory(
                    contest,
                    _userSettings.SubmissionHistoryExportDirectory);
                destinationFilePath = Path.Combine(
                    exportDirectory,
                    ContestResultExporter.CreateDefaultExportFileName(contest));
            }
            catch (Exception ex)
            {
                _contestSession.FailExport(markAutoExportFailed: true);
                UpdateContestStatus();
                AppendTerminal("[Contest] 대회 종료 결과 자동 내보내기 경로를 준비하지 못했습니다.");
                AppendTerminal(ex.Message);
                return;
            }

            ExportContestResult(contest, destinationFilePath, isAutomatic: true);
        }

        private void ExportContestResult(ContestContext contest, string destinationFilePath, bool isAutomatic)
        {
            if (!_contestSession.CanStartExport())
            {
                AppendTerminal("[Contest] 대회 결과 내보내기가 이미 진행 중입니다.");
                return;
            }

            try
            {
                _contestSession.BeginExport();
                SubmissionHistoryExportResult exportResult = _contestResultExporter.Export(contest, destinationFilePath);
                if (isAutomatic || DateTimeOffset.Now > contest.EndsAt)
                {
                    _contestSession.CompleteExport(markAutoExportCompleted: true);
                }
                else
                {
                    _contestSession.CompleteExport(markAutoExportCompleted: false);
                }

                UpdateContestStatus();

                AppendTerminal(isAutomatic
                    ? $"[Contest] 대회 종료 결과를 자동으로 내보냈습니다: {exportResult.FilePath}"
                    : $"[Contest] 대회 결과를 내보냈습니다: {exportResult.FilePath}");
                AppendTerminal($"[Contest] 내보낸 문항: {exportResult.ProblemCount}개 / 제출: {exportResult.AttemptCount}개");

                if (!isAutomatic)
                {
                    MessageBox.Show(
                        "대회 결과를 내보냈습니다.",
                        "대회 결과 내보내기",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AppendTerminal("[Contest] 대회 결과 내보내기 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                if (isAutomatic)
                {
                    _contestSession.FailExport(markAutoExportFailed: true);
                    UpdateContestStatus();
                }
                else
                {
                    _contestSession.FailExport(markAutoExportFailed: false);
                }

                if (!isAutomatic)
                {
                    MessageBox.Show(
                        "대회 결과를 내보낼 수 없습니다.\n\n" + ex.Message,
                        "대회 결과 내보내기",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                UpdateProblemCommandState();
            }
        }

        private void InspectLessonResultMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "수업 결과 확인",
                Filter = "ZIP 수업 결과 파일 (*.zip)|*.zip|모든 파일 (*.*)|*.*"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true)
            {
                return;
            }

            try
            {
                SubmissionHistoryInspectionDocument document = _lessonResultInspectionReader.ReadZip(dialog.FileName);
                var window = new SubmissionHistoryFileWindow(document)
                {
                    Owner = this
                };
                window.ShowDialog();
            }
            catch (InvalidLessonResultFileException)
            {
                AppendTerminal("[Lesson] 잘못된 수업 결과 파일입니다.");
                MessageBox.Show(
                    "잘못된 파일입니다.",
                    "수업 결과 확인",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (LessonResultNoSubmissionsException)
            {
                AppendTerminal("[Lesson] 수업 결과 파일에 제출 기록이 없습니다.");
                MessageBox.Show(
                    "제출 기록이 없습니다.",
                    "수업 결과 확인",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendTerminal("[Lesson] 수업 결과 파일을 확인하는 중 오류가 발생했습니다.");
                AppendTerminal(ex.Message);
                MessageBox.Show(
                    "잘못된 파일입니다.",
                    "수업 결과 확인",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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

            string selectedPythonPath = dialog.FileName;
            _pythonRunner.PythonExecutablePath = selectedPythonPath;
            _isPythonConnected = false;
            _benchmarkResult = null;
            UpdateProblemCommandState();

            AppendTerminal($"[Settings] Python 경로를 설정했습니다: {_pythonRunner.PythonExecutablePath}");
            AppendTerminal("[Benchmark] Python 경로가 변경되어 채점 환경 벤치마크를 다시 실행합니다.");
            await StartEnvironmentBenchmarkAsync(isManual: true);

            if (_isPythonConnected && _benchmarkResult?.Succeeded == true)
            {
                _userSettings.PythonExecutablePath = selectedPythonPath;
                SaveUserSettings();
            }
            else
            {
                AppendTerminal("[Settings] Python 경로는 연결 확인과 벤치마크가 성공한 뒤 저장됩니다.");
            }
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
            var window = new SettingsWindow(_userSettings)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();
            if (result != true)
            {
                return;
            }

            _userSettings.ProblemSaveDirectory = window.ProblemSaveDirectory;
            _userSettings.SubmissionHistoryExportDirectory = window.SubmissionHistoryExportDirectory;
            _userSettings.AutoSaveDraftsEnabled = window.AutoSaveDraftsEnabled;
            _userSettings.AutoSaveDraftIntervalSeconds = window.AutoSaveDraftIntervalSeconds;
            SaveUserSettings();
            ApplyDraftAutoSaveSettings(logSetting: true);

            AppendTerminal("[Settings] 환경 설정을 적용했습니다.");
            AppendTerminal($"[Settings] 문항 저장 경로: {FormatConfiguredDirectory(_userSettings.ProblemSaveDirectory)}");
            AppendTerminal($"[Settings] 제출 이력 내보내기 경로: {FormatConfiguredDirectory(_userSettings.SubmissionHistoryExportDirectory)}");
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var window = new AboutWindow(
                ApplicationVersion,
                ApplicationAuthor,
                ApplicationIndischoolId,
                ApplicationTistoryUrl)
            {
                Owner = this
            };

            window.ShowDialog();
        }

        private void ClearTerminalMenuItem_Click(object sender, RoutedEventArgs e)
        {
            TerminalTextBox.Document.Blocks.Clear();
            _terminalEndsWithLineBreak = true;
            AppendTerminal("[System] 터미널을 비웠습니다.");
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SaveDraftForActiveScope(force: false, logOnSuccess: false);
            _draftAutoSaveTimer.Stop();
            base.OnClosing(e);
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
