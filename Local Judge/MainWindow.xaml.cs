using System;
using System.Windows;
using System.Windows.Media;

namespace Local_Judge
{
    public partial class MainWindow : Window
    {
        private readonly Brush _readyBrush = new SolidColorBrush(Color.FromRgb(45, 164, 78));
        private readonly Brush _workingBrush = new SolidColorBrush(Color.FromRgb(251, 188, 5));
        private readonly Brush _errorBrush = new SolidColorBrush(Color.FromRgb(218, 54, 51));

        public MainWindow()
        {
            InitializeComponent();

            SetStatus("대기 중");
            AppendTerminal("[UI] 화면 구성이 완료되었습니다.");
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

        private void LoadProblemMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("문제 불러오는 중", isWorking: true);
            AppendTerminal("[Problem] 문제 불러오기를 시작합니다.");

            // TODO: 이후 JSON 문제 로더와 연결
            ProblemTitleTextBlock.Text = "예제 문제: A+B";
            ProblemMetaTextBlock.Text = "시간 제한: 2초 / 메모리 제한: 128 MB";
            ProblemDescriptionTextBox.Text = "두 정수 A와 B를 입력받은 다음, A+B를 출력하는 프로그램을 작성하시오.";
            InputDescriptionTextBox.Text = "첫째 줄에 A와 B가 주어진다.";
            OutputDescriptionTextBox.Text = "첫째 줄에 A+B를 출력한다.";
            SampleInputTextBox.Text = "1 2";
            SampleOutputTextBox.Text = "3";
            CurrentProblemStatusTextBlock.Text = "현재 문제: 예제 문제 A+B";

            SetStatus("문제 불러오기 완료");
            AppendTerminal("[Problem] 문제 정보가 화면에 표시되었습니다.");
        }

        private void SaveCodeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("코드 저장 중", isWorking: true);
            AppendTerminal("[Code] 코드 저장 기능은 다음 단계에서 연결합니다.");
            SetStatus("대기 중");
        }

        private void RunSampleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("실행 중", isWorking: true);
            AppendTerminal("[Run] 예제 실행 기능은 다음 단계에서 Python Runner와 연결합니다.");
            SetStatus("실행 대기");
        }

        private void DebugMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("디버그 준비 중", isWorking: true);
            AppendTerminal("[Debug] 디버그 기능은 추후 debugpy와 연결합니다.");
            SetStatus("대기 중");
        }

        private void JudgeSampleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("예제 채점 중", isWorking: true);
            AppendTerminal("[Judge] 예제 채점 기능은 다음 단계에서 구현합니다.");
            SetStatus("채점 대기");
        }

        private void JudgeAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("채점 중", isWorking: true);
            AppendTerminal("[Judge] 전체 채점 기능은 다음 단계에서 구현합니다.");
            SetStatus("채점 대기");
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
