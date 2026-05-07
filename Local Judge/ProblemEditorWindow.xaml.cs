using System.Linq;
using System.Windows;

namespace Local_Judge
{
    public partial class ProblemEditorWindow : Window
    {
        public ProblemDocument Problem { get; private set; }

        public ProblemEditorWindow(ProblemDocument? problem = null)
        {
            InitializeComponent();

            Problem = CloneProblem(problem ?? CreateDefaultProblem());
            LoadProblemToForm(Problem);
        }

        private static ProblemDocument CreateDefaultProblem()
        {
            return new ProblemDocument
            {
                Version = 1,
                TimeLimitMs = 2000,
                MemoryLimitMb = 128,
                Samples = new()
            };
        }

        private static ProblemDocument CloneProblem(ProblemDocument source)
        {
            return new ProblemDocument
            {
                Version = source.Version <= 0 ? 1 : source.Version,
                Id = source.Id,
                Title = source.Title,
                TimeLimitMs = source.TimeLimitMs <= 0 ? 2000 : source.TimeLimitMs,
                MemoryLimitMb = source.MemoryLimitMb <= 0 ? 128 : source.MemoryLimitMb,
                Description = source.Description,
                InputFormat = source.InputFormat,
                OutputFormat = source.OutputFormat,
                Samples = source.Samples
                    .Select(sample => new SampleCaseDocument
                    {
                        Input = sample.Input,
                        Output = sample.Output
                    })
                    .ToList()
            };
        }

        private void LoadProblemToForm(ProblemDocument problem)
        {
            ProblemIdTextBox.Text = problem.Id;
            TitleTextBox.Text = problem.Title;
            TimeLimitTextBox.Text = problem.TimeLimitMs.ToString();
            MemoryLimitTextBox.Text = problem.MemoryLimitMb.ToString();
            DescriptionTextBox.Text = problem.Description;
            InputDescriptionTextBox.Text = problem.InputFormat;
            OutputDescriptionTextBox.Text = problem.OutputFormat;

            SampleCaseDocument? firstSample = problem.Samples.FirstOrDefault();
            SampleInputTextBox.Text = firstSample?.Input ?? string.Empty;
            SampleOutputTextBox.Text = firstSample?.Output ?? string.Empty;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            string id = ProblemIdTextBox.Text.Trim();
            string title = TitleTextBox.Text.Trim();

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

            bool hasSampleInput = !string.IsNullOrWhiteSpace(SampleInputTextBox.Text);
            bool hasSampleOutput = !string.IsNullOrWhiteSpace(SampleOutputTextBox.Text);

            if (hasSampleInput != hasSampleOutput)
            {
                MessageBox.Show("예제 입력과 예제 출력은 함께 입력하거나 둘 다 비워두세요.", "입력 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Problem = new ProblemDocument
            {
                Version = 1,
                Id = id,
                Title = title,
                TimeLimitMs = timeLimitMs,
                MemoryLimitMb = memoryLimitMb,
                Description = DescriptionTextBox.Text,
                InputFormat = InputDescriptionTextBox.Text,
                OutputFormat = OutputDescriptionTextBox.Text,
                Samples = new()
            };

            if (hasSampleInput && hasSampleOutput)
            {
                Problem.Samples.Add(new SampleCaseDocument
                {
                    Input = SampleInputTextBox.Text,
                    Output = SampleOutputTextBox.Text
                });
            }

            DialogResult = true;
        }
    }
}
