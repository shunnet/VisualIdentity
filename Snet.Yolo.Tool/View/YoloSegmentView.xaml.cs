using System.Windows.Controls;

namespace Snet.Yolo.Tool.View
{
    /// <summary>
    /// YoloSegmentView.xaml 的交互逻辑
    /// </summary>
    public partial class YoloSegmentView : UserControl
    {
        public YoloSegmentView()
        {
            InitializeComponent();
            Unloaded += (_, _) => (DataContext as IDisposable)?.Dispose();
        }
    }
}
