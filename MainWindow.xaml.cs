using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using PokemonHelper.Services;

namespace PokemonHelper
{
    public partial class MainWindow : Window
    {
        private readonly ScreenCaptureService _ocrService;

        public MainWindow(ScreenCaptureService ocrService)
        {
            InitializeComponent();
            _ocrService = ocrService;
        }

        private void RefreshProcessButton_Click(object sender, RoutedEventArgs e)
        {
            var processes = Process.GetProcesses()
                .Where(p => p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                .OrderBy(p => p.MainWindowTitle)
                .ToList();

            ProcessComboBox.ItemsSource = processes;
            if (processes.Count > 0)
                ProcessComboBox.SelectedIndex = 0;
        }

        private void SetRegionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessComboBox.SelectedValue == null)
            {
                System.Windows.MessageBox.Show("먼저 타겟 윈도우를 선택해주세요.");
                return;
            }

            IntPtr targetHwnd = (IntPtr)ProcessComboBox.SelectedValue;
            
            // 타겟 핸들을 글로벌 상태로 저장
            ScreenCaptureService.TargetHwnd = targetHwnd;

            var overlay = new CaptureOverlayWindow(targetHwnd);
            overlay.ShowDialog(); // 설정이 끝날 때까지 대기
        }

        private void StartOcrButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScreenCaptureService.TargetHwnd == IntPtr.Zero)
            {
                System.Windows.MessageBox.Show("먼저 타겟 윈도우와 캡처 영역을 설정해주세요.");
                return;
            }

            StartOcrButton.IsEnabled = false;
            StopOcrButton.IsEnabled = true;
            _ocrService.Start();
            ResultTextBlock.Text += "\n\n화면 인식(OCR) 엔진이 가동되었습니다.\n선택한 프로그램 창을 추적하여 포켓몬 이름을 읽어옵니다.";
        }

        private void StopOcrButton_Click(object sender, RoutedEventArgs e)
        {
            StartOcrButton.IsEnabled = true;
            StopOcrButton.IsEnabled = false;
            _ocrService.Stop();
            ResultTextBlock.Text += "\n\n화면 인식(OCR) 엔진이 정지되었습니다.";
        }
    }
}