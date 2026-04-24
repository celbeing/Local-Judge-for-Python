using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Collections.Generic;

using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace ClassCode.App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        CompletionWindow completionWindow; // 자동 완성 창을 담을 변수
        public MainWindow()
        {
            InitializeComponent();

            // Python Code Editor 파이썬 구문 강조 적용
            codeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Python");
            // [핵심] 글자 입력 시 발생하는 이벤트 연결
            codeEditor.TextArea.TextEntering += CodeEditor_TextEntering;
            codeEditor.TextArea.TextEntered += CodeEditor_TextEntered;

        }

        // 글자가 입력되기 직전
        private void CodeEditor_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0 && completionWindow != null)
            {
                // 사용자가 글자를 치는 동안 목록에 없는 글자라면 창을 닫음
                if (!char.IsLetterOrDigit(e.Text[0]))
                {
                    completionWindow.CompletionList.RequestInsertion(e);
                }
            }
        }

        // 글자가 입력된 직후
        private void CodeEditor_TextEntered(object sender, TextCompositionEventArgs e)
        {
            // 알파벳이나 숫자가 입력되었을 때만 자동 완성 창을 띄움
            if (completionWindow == null && char.IsLetterOrDigit(e.Text[0]))
            {
                completionWindow = new CompletionWindow(codeEditor.TextArea);
                IList<ICompletionData> data = completionWindow.CompletionList.CompletionData;

                string[] keywords = { "print", "if", "else", "for", "while", "def", "import", "return", "True", "False" };
                foreach (var key in keywords)
                {
                    data.Add(new PythonCompletionData(key));
                }

                completionWindow.Show();
                completionWindow.Closed += delegate { completionWindow = null; };

                // [핵심 추가] 현재 입력된 글자를 기반으로 즉시 리스트를 필터링하도록 명령
                // 이 코드가 없으면 에디터는 'p'가 입력된 것을 모르는 상태에서 'print'를 통째로 넣으려 할 수 있습니다.
                completionWindow.CompletionList.SelectItem(e.Text);
            }
        }


        // 메뉴 클릭 이벤트 예시
        private void OpenProblem_Click(object sender, RoutedEventArgs e)
        {
            // 나중에 여기에 파일 탐색기를 띄우는 로직을 넣을 거예요!
            MessageBox.Show("문항 열기 기능을 준비 중입니다.");
        }
        private void OpenWorkbook_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("문제집 열기 기능을 준비 중입니다.");
        }

        private void OpenContest_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("대회 만들기 기능을 준비 중입니다.");
        }

        private void CreateProblem_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("문항 만들기 기능을 준비 중입니다.");
        }

        private void CreateWorkbook_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("문제집 만들기 기능을 준비 중입니다.");
        }

        private void CreateContest_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("대회 만들기 기능을 준비 중입니다.");
        }
    }
}