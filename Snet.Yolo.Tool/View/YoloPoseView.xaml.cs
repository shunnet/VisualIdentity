using System.Windows.Controls;

namespace Snet.Yolo.Tool.View
{
    /// <summary>
    /// YoloPoseView.xaml 的交互逻辑
    /// </summary>
    public partial class YoloPoseView : UserControl
    {
        public YoloPoseView()
        {
            InitializeComponent();
            Unloaded += (_, _) => (DataContext as IDisposable)?.Dispose();
        }
    }
}
