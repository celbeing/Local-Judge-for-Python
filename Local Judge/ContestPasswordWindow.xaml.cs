using System.Windows;

namespace Local_Judge
{
    public partial class ContestPasswordWindow : Window
    {
        public ContestPasswordWindow()
        {
            InitializeComponent();
        }

        public string Password { get; private set; } = string.Empty;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ContestPasswordBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Password = ContestPasswordBox.Password.Trim();
            DialogResult = true;
        }
    }
}
