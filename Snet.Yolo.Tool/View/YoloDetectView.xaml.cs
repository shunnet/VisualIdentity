using System.Windows.Controls;

namespace Snet.Yolo.Tool.View
{
    /// <summary>
    /// YoloDetectView.xaml 的交互逻辑
    /// </summary>
    public partial class YoloDetectView : UserControl
    {
        public YoloDetectView()
        {
            InitializeComponent();
            Unloaded += (_, _) => (DataContext as IDisposable)?.Dispose();
        }
    }
}
