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

namespace ClassCode.App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
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