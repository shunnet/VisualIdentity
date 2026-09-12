using System.Windows.Controls;

namespace Snet.Yolo.Tool.View
{
    /// <summary>
    /// YoloOBBView.xaml 的交互逻辑
    /// </summary>
    public partial class YoloOBBView : UserControl
    {
        public YoloOBBView()
        {
            InitializeComponent();
            Unloaded += (_, _) => (DataContext as IDisposable)?.Dispose();
        }
    }
}
