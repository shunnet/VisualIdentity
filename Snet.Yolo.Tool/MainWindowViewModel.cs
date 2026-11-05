using Snet.Windows.Core.mvvm;
using CommunityToolkit.Mvvm.Input;
using Snet.Core.handler;
using Snet.Yolo.Server.inference;
using System.Globalization;
using Snet.Yolo.Tool.Services;
using Snet.Windows.Controls.data;

namespace Snet.Yolo.Tool
{
    /// <summary>主窗口的全局 ONNX Runtime 设备选择和应用状态。</summary>
    public class MainWindowViewModel : BindNotify
    {
        private readonly ToolRuntimeService _runtime;
        private ComboBoxModel? _selectedHardware;
        private string _gpuId = "0";
        private bool _isApplying;
        private bool _canEditHardware = true;
        private bool _gpuControlsEnabled;
        private string _currentHardware = string.Empty;
        private string _hardwareMessage = string.Empty;

        /// <summary>从桌面程序独立保存的配置恢复设备，并创建异步应用命令。</summary>
        public MainWindowViewModel() : this(App.Runtime) { }

        /// <summary>绑定指定运行服务，避免界面状态与已经应用的配置混用。</summary>
        /// <param name="runtime">应用级桌面推理运行服务。</param>
        internal MainWindowViewModel(ToolRuntimeService runtime)
        {
            _runtime = runtime;
            var snapshot = _runtime.Current;
            _selectedHardware = HardwareOptions.First(item => (OnnxRuntimeHardware)item.Value == snapshot.Hardware);
            _gpuId = snapshot.GpuId.ToString(CultureInfo.InvariantCulture);
            _gpuControlsEnabled = snapshot.Hardware == OnnxRuntimeHardware.Cuda;
            _currentHardware = Describe(snapshot);
            _hardwareMessage = Text("HardwareScope");
            ApplyHardware = new AsyncRelayCommand(ApplyHardwareAsync, () => !IsApplying && SelectedHardware is not null);
        }

        /// <summary>使用 Snet 下拉框数据模型显示可选的 CPU 与 NVIDIA GPU。</summary>
        public IReadOnlyList<ComboBoxModel> HardwareOptions { get; } =
            [new("CPU", OnnxRuntimeHardware.Cpu), new("NVIDIA GPU", OnnxRuntimeHardware.Cuda)];

        /// <summary>待应用的执行硬件；编辑不改变正在使用的设备。</summary>
        public ComboBoxModel? SelectedHardware
        {
            get => _selectedHardware;
            set
            {
                SetProperty(ref _selectedHardware, value);
                GpuControlsEnabled = value?.Value is OnnxRuntimeHardware.Cuda && !IsApplying;
                HardwareMessage = Text("HardwareScope");
                ApplyHardware.NotifyCanExecuteChanged();
            }
        }

        /// <summary>待应用的 GPU 编号；提交时校验，避免无效输入沿用旧编号。</summary>
        public string GpuId { get => _gpuId; set => SetProperty(ref _gpuId, value); }
        /// <summary>设备准备或保存是否正在执行。</summary>
        public bool IsApplying { get => _isApplying; private set => SetProperty(ref _isApplying, value); }
        /// <summary>准备设备期间禁止重复修改待提交的选项。</summary>
        public bool CanEditHardware { get => _canEditHardware; private set => SetProperty(ref _canEditHardware, value); }
        /// <summary>仅选择 GPU 且未在应用配置时允许编辑编号。</summary>
        public bool GpuControlsEnabled { get => _gpuControlsEnabled; private set => SetProperty(ref _gpuControlsEnabled, value); }
        /// <summary>当前已经生效的设备，与待应用选项分别显示。</summary>
        public string CurrentHardware { get => _currentHardware; private set => SetProperty(ref _currentHardware, value); }
        /// <summary>设备范围、准备进度、成功或失败原因。</summary>
        public string HardwareMessage { get => _hardwareMessage; private set => SetProperty(ref _hardwareMessage, value); }
        /// <summary>在后台验证目标设备，成功后原子保存并切换后续识别。</summary>
        public IAsyncRelayCommand ApplyHardware { get; }

        /// <summary>捕获输入后后台探测；失败仅更新提示，不覆盖原配置及活动会话。</summary>
        private async Task ApplyHardwareAsync()
        {
            if (SelectedHardware?.Value is not OnnxRuntimeHardware hardware) { return; }
            var gpuId = _runtime.Current.GpuId;
            if (hardware == OnnxRuntimeHardware.Cuda &&
                (!int.TryParse(GpuId, NumberStyles.None, CultureInfo.InvariantCulture, out gpuId) || gpuId < 0))
            {
                HardwareMessage = Text("HardwareInvalidGpu");
                return;
            }
            var snapshot = new OnnxRuntimeSettings { Hardware = hardware, GpuId = gpuId };
            IsApplying = true;
            CanEditHardware = false;
            GpuControlsEnabled = false;
            ApplyHardware.NotifyCanExecuteChanged();
            HardwareMessage = Text("HardwareApplying");
            try
            {
                var saved = await Task.Run(() => _runtime.Configure(snapshot));
                CurrentHardware = Describe(saved);
                GpuId = saved.GpuId.ToString(CultureInfo.InvariantCulture);
                HardwareMessage = Text("HardwareApplied");
            }
            catch (Exception error) { HardwareMessage = Text("HardwareApplyFailed") + " " + error.GetBaseException().Message; }
            finally
            {
                IsApplying = false;
                CanEditHardware = true;
                GpuControlsEnabled = SelectedHardware?.Value is OnnxRuntimeHardware.Cuda;
                ApplyHardware.NotifyCanExecuteChanged();
            }
        }

        /// <summary>生成当前已保存设备的简短说明。</summary>
        /// <param name="settings">已保存的硬件快照。</param>
        /// <returns>本地化的 CPU 或指定 GPU 说明。</returns>
        private static string Describe(OnnxRuntimeSettings settings)
            => Text("HardwareCurrent") + " " + (settings.Hardware == OnnxRuntimeHardware.Cpu ? "CPU" : $"NVIDIA GPU {settings.GpuId}");

        /// <summary>沿用桌面程序的中英文资源获取方式。</summary>
        /// <param name="key">语言资源键。</param>
        /// <returns>当前语言对应的文字。</returns>
        private static string Text(string key) => App.LanguageOperate.GetLanguageValue(key) ?? key;
    }
}
