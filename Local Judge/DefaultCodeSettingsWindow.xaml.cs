using System.Windows;

namespace Local_Judge
{
    public partial class DefaultCodeSettingsWindow : Window
    {
        public DefaultCodeSettingsWindow(string defaultCode)
        {
            InitializeComponent();
            DefaultCodeTextBox.Text = NormalizeLineEndings(defaultCode ?? LocalJudgeUserSettings.DefaultPythonEditorCode);
            DefaultCodeTextBox.Focus();
        }

        public string DefaultCode { get; private set; } = LocalJudgeUserSettings.DefaultPythonEditorCode;

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            DefaultCodeTextBox.Text = LocalJudgeUserSettings.DefaultPythonEditorCode;
            DefaultCodeTextBox.Focus();
            DefaultCodeTextBox.CaretIndex = DefaultCodeTextBox.Text.Length;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DefaultCode = NormalizeLineEndings(DefaultCodeTextBox.Text);
            DialogResult = true;
        }

        private static string NormalizeLineEndings(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
        }
    }
}
