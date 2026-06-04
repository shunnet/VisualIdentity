using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Snet.Core.handler;
using Snet.Model.data;
using Snet.Utility;
using Snet.Windows.Controls.handler;
using Snet.Windows.Core.mvvm;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using Snet.Yolo.Tool.Data;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Extensions;
using YoloDotNet.Models;

namespace Snet.Yolo.Tool.ViewModel;

public class YoloDetectViewModel : BindNotify
{
    private IdentityOperate _currentOperate;
    private OnnxType _currentOnnxType;
    private string _currentDeviceJson;

    public IdentityOperate YoloInit(OnnxType onnxType)
    {
        // Reuse cached instance if configuration hasn't changed
        if (_currentOperate != null && _currentOnnxType == onnxType && _currentDeviceJson == DeviceJson)
        {
            return _currentOperate;
        }
        // Dispose old instance before creating new one
        if (_currentOperate != null)
        {
            _currentOperate.Dispose();
            _currentOperate = null;
        }
        _currentOnnxType = onnxType;
        _currentDeviceJson = DeviceJson;
        _currentOperate = IdentityOperate.Instance(new Yolo.Server.models.data.IdentityData
        {
            // 默认使用 CPU 运算
            Hardware = new CpuExecutionProvider(OnnxModel),
            IdentifyType = onnxType,
            SN = $"{onnxType}{DeviceJson}"
        });
        return _currentOperate;
    }

    public bool _needReinit = true;
    public Visibility ImagesVisibility
    {
        get => _ImagesVisibility;
        set => SetProperty(ref _ImagesVisibility, value);
    }
    private Visibility _ImagesVisibility = Visibility.Collapsed;

    /// <summary>
    /// 结果image
    /// </summary>
    public ImageSource ResultImage
    {
        get => GetProperty(() => ResultImage);
        set => SetProperty(() => ResultImage, value);
    }

    /// <summary>
    /// 置信度阈值
    /// </summary>
    public float Confidence
    {
        get => _Confidence;
        set => SetProperty(ref _Confidence, value);
    }
    private float _Confidence = 0.2f;

    /// <summary>
    /// 交并比阈值
    /// </summary>
    public float Iou
    {
        get => _Iou;
        set => SetProperty(ref _Iou, value);
    }
    private float _Iou = 0.7f;

    /// <summary>
    /// 使用哪个硬件的JSON字符串
    /// </summary>
    public string DeviceJson
    {
        get => _DeviceJson;
        set => SetProperty(ref _DeviceJson, value);
    }
    private string _DeviceJson = "{\"CpuExecutionProvider\":{}}";

    /// <summary>
    /// 模型路径
    /// </summary>
    public string OnnxModel
    {
        get => GetProperty(() => OnnxModel);
        set
        {
            _needReinit = true;
            SetProperty(() => OnnxModel, value);
        }
    }

    /// <summary>
    /// 源路径
    /// </summary>
    public string SourcePath
    {
        get => GetProperty(() => SourcePath);
        set => SetProperty(() => SourcePath, value);
    }


    private readonly object _tokenLock = new();
    CancellationTokenSource tokenSource;

    /// <summary>
    /// 停止验证
    /// </summary>
    public IAsyncRelayCommand V_Image_Stop => p_V_Image_Stop ??= new AsyncRelayCommand(V_Image_StopAsync);
    IAsyncRelayCommand p_V_Image_Stop;
    private async Task V_Image_StopAsync()
    {
        CancellationTokenSource ts;
        lock (_tokenLock)
        {
            ts = tokenSource;
        }
        if (ts != null)
        {
            ts.Cancel();
            ts.Dispose();
            lock (_tokenLock)
            {
                tokenSource = null;
            }
            await msgShow(App.LanguageOperate.GetLanguageValue("验证已停止"));
        }
    }



    /// <summary>
    /// 模型路径选择命令
    /// </summary>
    public IAsyncRelayCommand OnnxModelSelect => p_OnnxModelSelect ??= new AsyncRelayCommand(OnnxModelSelectAsync);
    IAsyncRelayCommand p_OnnxModelSelect;
    private Task OnnxModelSelectAsync()
    {
        string path = Win32Handler.Select(App.LanguageOperate.GetLanguageValue("请选择模型路径"), false, new Dictionary<string, string> { ["onnx"] = "*.onnx" });
        if (!path.IsNullOrWhiteSpace())
        {
            OnnxModel = path;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 源路径选择命令
    /// </summary>
    public IAsyncRelayCommand SourcePathSelect => p_SourcePathSelect ??= new AsyncRelayCommand(SourcePathSelectAsync);
    IAsyncRelayCommand p_SourcePathSelect;
    private async Task SourcePathSelectAsync()
    {
        string path = Win32Handler.Select(App.LanguageOperate.GetLanguageValue("请选择需要验证图片的文件夹"), true);
        if (!path.IsNullOrWhiteSpace())
        {
            await Task.Run(async () =>
            {
                SourcePath = path;
                if (Application.Current == null)
                    return;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ItemsControlSource.Clear();
                });
                //检索图片
                string[] extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp" };
                var imageFiles = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(file => extensions.Contains(Path.GetExtension(file).ToLower())).ToList();
                foreach (var file in imageFiles)
                {
                    try
                    {
                        FileInfo info = new FileInfo(file);
                        long sizeInBytes = info.Length;

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad; // 避免文件占用锁
                        bitmap.UriSource = new Uri(file, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze(); // 多线程场景推荐冻结

                        int width = bitmap.PixelWidth;
                        int height = bitmap.PixelHeight;
                        string size = sizeInBytes.GetFileSize();

                        if (Application.Current == null)
                            return;
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            //添加到集合
                            ItemsControlSource.Add(new ItemsControlBody()
                            {
                                Name = Path.GetFileNameWithoutExtension(file),
                                Description = $"{width} x {height} ({size})",
                                Path = file,
                                IsSelected = false
                            });
                        });
                        ImagesVisibility = Visibility.Visible;
                    }
                    catch (Exception ex)
                    {
                        await msgShow($"{App.LanguageOperate.GetLanguageValue("读取失败")}: {path}，{ex.Message}");
                    }
                }
                if (ItemsControlSource.Count > 0)
                {
                    await msgShow($"{App.LanguageOperate.GetLanguageValue("已加载")}{ItemsControlSource.Count}{App.LanguageOperate.GetLanguageValue("张原图")}");
                }
                else
                {
                    await msgShow(App.LanguageOperate.GetLanguageValue("未检索到图片"));
                }

            });
        }
    }
    /// <summary>
    /// 消息
    /// </summary>
    public string Message
    {
        get => GetProperty(() => Message);
        set => SetProperty(() => Message, value);
    }
    public IAsyncRelayCommand MessageClear => p_MessageClear ??= new AsyncRelayCommand<string>(MessageClearAsync);
    IAsyncRelayCommand p_MessageClear;
    private Task MessageClearAsync(string? e)
    {
        Message = string.Empty;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 信息框事件
    /// </summary>
    public IAsyncRelayCommand MessageTextChanged => p_MessageTextChanged ??= new AsyncRelayCommand<TextChangedEventArgs>(MessageTextChangedAsync);
    IAsyncRelayCommand p_MessageTextChanged;
    /// <summary>
    /// 信息框事件
    /// 让滚动条一直处在最下方
    /// </summary>
    public Task MessageTextChangedAsync(TextChangedEventArgs? e)
    {
        TextBox textBox = e.Source.GetSource<TextBox>();
        textBox.SelectionStart = textBox.Text.Length;
        textBox.SelectionLength = 0;
        textBox.ScrollToEnd();
        return Task.CompletedTask;
    }
    /// <summary>
    /// 消息显示
    /// </summary>
    /// <param name="msg">消息</param>
    /// <returns></returns>
    public async Task msgShow(string msg, bool isDateTime = true)
    {
        if (msg.IsNullOrWhiteSpace())
            return;
        if (Application.Current == null)
            return;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (Message?.Length > 10000)
            {
                Message = string.Empty;
            }
            if (isDateTime)
            {
                Message += $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff")} : {msg}\r\n";
            }
            else
            {
                Message += $"{msg}\r\n";
            }
        });
    }

    /// <summary>
    /// 项控件源数据
    /// </summary>
    public ObservableCollection<ItemsControlBody> ItemsControlSource
    {
        get => _ItemsControlSource;
        set => SetProperty(ref _ItemsControlSource, value);
    }
    private ObservableCollection<ItemsControlBody> _ItemsControlSource = new ObservableCollection<ItemsControlBody>();


    /// <summary>
    /// 验证所有图片
    /// </summary>
    public IAsyncRelayCommand VA_Image => p_VA_Image ??= new AsyncRelayCommand(VA_ImageAsync);
    IAsyncRelayCommand p_VA_Image;
    public async Task VA_ImageAsync()
    {
        CancellationTokenSource ts;
        lock (_tokenLock)
        {
            if (tokenSource == null)
            {
                tokenSource = new CancellationTokenSource();
            }
            ts = tokenSource;
        }
        foreach (var item in ItemsControlSource) { item.IsSelected = false; }
        foreach (var item in ItemsControlSource)
        {
            if (ts != null && !ts.IsCancellationRequested)
            {
                int index = ItemsControlSource.IndexOf(item);
                if (index > 0)
                {
                    ItemsControlSource[index - 1].IsSelected = false;
                }
                item.IsSelected = true;
                await V_ImageAsync(item, ts.Token);
            }
        }
    }
    /// <summary>
    /// 验证选中图片
    /// </summary>
    public IAsyncRelayCommand VS_Image => p_VS_Image ??= new AsyncRelayCommand(VS_ImageAsync);
    IAsyncRelayCommand p_VS_Image;
    public async Task VS_ImageAsync()
    {
        CancellationTokenSource ts;
        lock (_tokenLock)
        {
            if (tokenSource == null)
            {
                tokenSource = new CancellationTokenSource();
            }
            ts = tokenSource;
        }
        foreach (var item in ItemsControlSource)
        {
            if (ts != null && !ts.IsCancellationRequested)
            {
                if (item.IsSelected)
                {
                    await V_ImageAsync(item, ts.Token);
                }
            }
        }
    }

    /// <summary>
    /// 向下验证
    /// </summary>
    public IAsyncRelayCommand XV_Image => p_XV_Image ??= new AsyncRelayCommand(XV_ImageAsync);
    IAsyncRelayCommand p_XV_Image;
    public async Task XV_ImageAsync()
    {
        CancellationTokenSource ts;
        lock (_tokenLock)
        {
            if (tokenSource == null)
            {
                tokenSource = new CancellationTokenSource();
            }
            ts = tokenSource;
        }
        ItemsControlBody? item = ItemsControlSource.Where(c => c.IsSelected).FirstOrDefault();
        int index = 0;
        if (item != null)
        {
            index = ItemsControlSource.IndexOf(item);
            index += 1;
            foreach (var itens in ItemsControlSource) { itens.IsSelected = false; }
        }
        if (index >= ItemsControlSource.Count)
        {
            index = 0;
        }
        ItemsControlSource[index].IsSelected = true;
        await V_ImageAsync(ItemsControlSource[index], ts.Token);
    }

    /// <summary>
    /// 向上验证
    /// </summary>
    public IAsyncRelayCommand SV_Image => p_SV_Image ??= new AsyncRelayCommand(SV_ImageAsync);
    IAsyncRelayCommand p_SV_Image;
    public async Task SV_ImageAsync()
    {
        CancellationTokenSource ts;
        lock (_tokenLock)
        {
            if (tokenSource == null)
            {
                tokenSource = new CancellationTokenSource();
            }
            ts = tokenSource;
        }
        ItemsControlBody? item = ItemsControlSource.Where(c => c.IsSelected).FirstOrDefault();
        int index = 0;
        if (item != null)
        {
            index = ItemsControlSource.IndexOf(item);
            index -= 1;
            foreach (var itens in ItemsControlSource) { itens.IsSelected = false; }
        }
        if (index < 0)
        {
            index = ItemsControlSource.Count - 1;
        }
        ItemsControlSource[index].IsSelected = true;
        await V_ImageAsync(ItemsControlSource[index], ts.Token);
    }




    public TimeHandler time = TimeHandler.Instance("TestTime");
    /// <summary>
    /// 验证图片
    /// </summary>
    public virtual async Task V_ImageAsync(ItemsControlBody item, CancellationToken token = default)
    {
        await Task.Run(async () =>
        {
            try
            {
                using SKImage image = SKImage.FromEncodedData(item.Path);
                time.StartRecord();
                OperateResult operateResult = await YoloInit(OnnxType.ObjectDetection).RunAsync(new ObjectDetectionData
                {
                    Confidence = Confidence,
                    Iou = Iou,
                    File = image.Encode().ToArray()
                });
                List<ObjectDetection> results = operateResult.GetObjectDetectionResult().ToObjectDetection();
                string msg = $"\r\n{App.LanguageOperate.GetLanguageValue("验证")} : {Path.GetFileName(item.Path)}\r\n{App.LanguageOperate.GetLanguageValue("大小")} : {item.Description}\r\n{App.LanguageOperate.GetLanguageValue("用时")} : {time.StopRecord().milliseconds} ms";
                msg += $"\r\n{App.LanguageOperate.GetLanguageValue("目标")} : <{results.Count}> {App.LanguageOperate.GetLanguageValue("个")}";
                if (results.Count > 0)
                {
                    for (int i = 0; i < results.Count; i++)
                    {
                        msg += $"\r\n<{i + 1}> {App.LanguageOperate.GetLanguageValue("标签")} : {results[i].Label.Name}";
                        msg += $"\r\n<{i + 1}> {App.LanguageOperate.GetLanguageValue("准度")} : {results[i].Confidence}";
                        msg += $"\r\n<{i + 1}> {App.LanguageOperate.GetLanguageValue("坐标")} : {results[i].BoundingBox.ToString()}";
                        if (Statistics)
                        {
                            Confidences.Add(results[i].Confidence);
                        }
                    }
                }
                msg += $"\r\n-------------------------------------------------------------------";
                using SKBitmap resultImage = image.Draw(results);
                ResultImage = await ConvertSKImageToImageSourceAsync(resultImage);
                await msgShow(msg);
            }
            catch (Exception ex)
            {
                await msgShow($"{App.LanguageOperate.GetLanguageValue("验证图片异常")}:{ex.Message}");
            }
        }, token);
    }

    /// <summary>
    /// 转换图片
    /// </summary>
    public async Task<ImageSource> ConvertSKImageToImageSourceAsync(SKBitmap skImage, SKEncodedImageFormat sK = SKEncodedImageFormat.Png)
    {
        return await Task.Run(() =>
        {
            using (var data = skImage.Encode(sK, 100))
            using (var ms = new MemoryStream(data.ToArray()))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // 避免文件锁
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze(); // 可用于跨线程场景
                return bitmap;
            }
        });
    }


    /// <summary>
    /// 统计状态
    /// </summary>
    public bool Statistics
    {
        get => statistics;
        set => SetProperty(ref statistics, value);
    }
    private bool statistics = false;

    public List<double> Confidences
    {
        get => confidences;
        set => SetProperty(ref confidences, value);
    }
    private List<double> confidences = new List<double>();

    public IAsyncRelayCommand Start => p_Start ??= new AsyncRelayCommand(StartAsync);
    IAsyncRelayCommand p_Start;
    private async Task StartAsync()
    {
        if (Statistics)
        {
            await msgShow("统计已启动!!!");
            return;
        }
        await msgShow("开始统计");
        Confidences.Clear();
        Statistics = true;
    }


    public IAsyncRelayCommand Stop => p_Stop ??= new AsyncRelayCommand(StopAsync);
    IAsyncRelayCommand p_Stop;
    private async Task StopAsync()
    {
        if (!Statistics)
        {
            await msgShow("统计未启动!!!");
            return;
        }
        if (Confidences.Count > 0)
        {
            double average = Confidences.Average();
            string msg = $"共检测到{Confidences.Count}个目标，平均置信度为：{average}";
            await msgShow("\r\n" + msg);
            await Snet.Windows.Controls.message.MessageBox.Show(msg, "提示");
        }
        else
        {
            await msgShow("平均值计算失败");
        }
        Statistics = false;
    }




    /// <summary>
    /// 右键复制
    /// </summary>
    public IAsyncRelayCommand MenuItemCopyClick => p_MenuItemCopyClick ??= new AsyncRelayCommand<object>(OnMenuItemCopyClickAsync);
    IAsyncRelayCommand p_MenuItemCopyClick;
    /// <summary>
    /// 右键复制 被点击
    /// </summary>
    public async Task OnMenuItemCopyClickAsync(object e)
    {
        if (!ResultImage.GetType().Equals(typeof(string)))
        {
            CopyImageToClipboard(ResultImage);
            await Windows.Controls.message.MessageBox.Show("结果图已复制到粘贴板", "提示", Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
        }
    }


    public static void CopyImageToClipboard(ImageSource imageSource)
    {
        if (imageSource is BitmapSource bitmapSource)
        {
            Clipboard.SetImage(bitmapSource);
        }
        else
        {
            throw new InvalidOperationException("ImageSource 不是 BitmapSource，无法复制。");
        }
    }

    /// <summary>
    /// Release cached IdentityOperate and its native resources.
    /// </summary>
    public void DisposeResources()
    {
        _currentOperate?.Dispose();
        _currentOperate = null;
        lock (_tokenLock)
        {
            tokenSource?.Cancel();
            tokenSource?.Dispose();
            tokenSource = null;
        }
    }
}
