using System.Windows.Controls;

namespace Snet.Yolo.Tool.View
{
    /// <summary>
    /// YoloClassifyView.xaml 的交互逻辑
    /// </summary>
    public partial class YoloClassifyView : UserControl
    {
        public YoloClassifyView()
        {
            InitializeComponent();
            DisposableViewLifetime.Attach(this);
        }
    }
}
