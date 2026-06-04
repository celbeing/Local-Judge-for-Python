using System.Windows;

namespace Local_Judge
{
    public partial class AboutWindow : Window
    {
        public AboutWindow(
            string version,
            string author,
            string indischoolId,
            string tistoryUrl)
        {
            InitializeComponent();

            VersionTextBlock.Text = version;
            AuthorTextBlock.Text = author;
            IndischoolTextBlock.Text = indischoolId;
            TistoryTextBlock.Text = tistoryUrl;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
