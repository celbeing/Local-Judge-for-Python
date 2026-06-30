using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Local_Judge
{
    public partial class DefaultCodeSettingsWindow : Window
    {
        private const string DefaultCodeEditorHostName = "localjudge.default-code-editor";

        private bool _isEditorReady;
        private string _editorCode = LocalJudgeUserSettings.DefaultPythonEditorCode;
        private readonly string _editorTheme;
        private TaskCompletionSource<string>? _editorCodeRequest;
        private string? _editorCodeRequestId;

        public DefaultCodeSettingsWindow(string defaultCode, string editorTheme)
        {
            InitializeComponent();
            _editorCode = NormalizeLineEndings(defaultCode ?? LocalJudgeUserSettings.DefaultPythonEditorCode);
            _editorTheme = LocalJudgeUserSettings.NormalizeEditorTheme(editorTheme);
            Loaded += async (_, _) => await InitializeEditorAsync();
        }

        public string DefaultCode { get; private set; } = LocalJudgeUserSettings.DefaultPythonEditorCode;

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            SetEditorCode(LocalJudgeUserSettings.DefaultPythonEditorCode);
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DefaultCode = NormalizeLineEndings(await GetEditorCodeAsync());
            DialogResult = true;
        }

        private async Task InitializeEditorAsync()
        {
            try
            {
                await DefaultCodeEditorWebView.EnsureCoreWebView2Async();

                string editorFolderPath = Path.Combine(AppContext.BaseDirectory, "Editor");
                if (!Directory.Exists(editorFolderPath))
                {
                    MessageBox.Show(
                        $"기본 코드 편집기 리소스를 찾을 수 없습니다.\n\n{editorFolderPath}",
                        "기본 코드 편집기 초기화 실패",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                DefaultCodeEditorWebView.CoreWebView2.WebMessageReceived += DefaultCodeEditorWebView_WebMessageReceived;
                DefaultCodeEditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    DefaultCodeEditorHostName,
                    editorFolderPath,
                    CoreWebView2HostResourceAccessKind.Allow);

                DefaultCodeEditorWebView.Source = new Uri($"https://{DefaultCodeEditorHostName}/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"기본 코드 편집기 초기화 중 오류가 발생했습니다.\n\n{ex.Message}",
                    "기본 코드 편집기 초기화 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DefaultCodeEditorWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
                JsonElement root = document.RootElement;

                if (!root.TryGetProperty("type", out JsonElement typeElement))
                {
                    return;
                }

                switch (typeElement.GetString())
                {
                    case "editorReady":
                        _isEditorReady = true;
                        SetEditorTheme(_editorTheme);
                        SetEditorCode(_editorCode);
                        FocusEditor();
                        break;

                    case "codeChanged":
                        if (root.TryGetProperty("code", out JsonElement codeElement))
                        {
                            _editorCode = NormalizeLineEndings(codeElement.GetString() ?? string.Empty);
                        }
                        break;

                    case "currentCode":
                        if (root.TryGetProperty("code", out JsonElement currentCodeElement))
                        {
                            string code = NormalizeLineEndings(currentCodeElement.GetString() ?? string.Empty);
                            string? responseRequestId = root.TryGetProperty("requestId", out JsonElement requestIdElement)
                                ? requestIdElement.GetString()
                                : null;

                            _editorCode = code;
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
            catch
            {
                // Monaco 메시지는 저장 시 캐시된 코드로 보완합니다.
            }
        }

        private async Task<string> GetEditorCodeAsync()
        {
            if (DefaultCodeEditorWebView.CoreWebView2 is null)
            {
                return _editorCode;
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

            DefaultCodeEditorWebView.CoreWebView2.PostWebMessageAsJson(script);

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

            return _editorCode;
        }

        private void SetEditorCode(string code)
        {
            _editorCode = NormalizeLineEndings(code);

            if (!_isEditorReady || DefaultCodeEditorWebView.CoreWebView2 is null)
            {
                return;
            }

            string script = JsonSerializer.Serialize(new
            {
                type = "setCode",
                code = _editorCode
            });

            DefaultCodeEditorWebView.CoreWebView2.PostWebMessageAsJson(script);
        }

        private void SetEditorTheme(string theme)
        {
            if (!_isEditorReady || DefaultCodeEditorWebView.CoreWebView2 is null)
            {
                return;
            }

            string script = JsonSerializer.Serialize(new
            {
                type = "setTheme",
                theme = LocalJudgeUserSettings.NormalizeEditorTheme(theme)
            });

            DefaultCodeEditorWebView.CoreWebView2.PostWebMessageAsJson(script);
        }

        private void FocusEditor()
        {
            if (!_isEditorReady || DefaultCodeEditorWebView.CoreWebView2 is null)
            {
                return;
            }

            string script = JsonSerializer.Serialize(new
            {
                type = "focus"
            });

            DefaultCodeEditorWebView.CoreWebView2.PostWebMessageAsJson(script);
        }

        private static string NormalizeLineEndings(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
        }
    }
}
