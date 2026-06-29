using System.Windows;

namespace Local_Judge
{
    public enum InitialCodeLoadChoice
    {
        ExistingCode,
        ProblemInitialCode
    }

    public partial class InitialCodeChoiceWindow : Window
    {
        public InitialCodeChoiceWindow()
        {
            InitializeComponent();
        }

        public InitialCodeLoadChoice Choice { get; private set; } = InitialCodeLoadChoice.ExistingCode;

        private void ExistingCodeButton_Click(object sender, RoutedEventArgs e)
        {
            Choice = InitialCodeLoadChoice.ExistingCode;
            DialogResult = true;
        }

        private void ProblemInitialCodeButton_Click(object sender, RoutedEventArgs e)
        {
            Choice = InitialCodeLoadChoice.ProblemInitialCode;
            DialogResult = true;
        }
    }
}
