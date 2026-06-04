using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Snet.Core.handler;
using Snet.Utility;
using Snet.Windows.Controls.handler;
using Snet.Windows.Controls.message;
using Snet.Windows.Controls.property;
using Snet.Windows.Core.handler;
using Snet.Windows.Core.mvvm;
using Snet.Yolo.Tool.Data;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snet.Yolo.Tool.ViewModel;

public class YoloDataUnificationViewModel : BindNotify
{

    /// <summary>
    /// 缓存参数对话框
    /// </summary>
    public static readonly PropertyControl param = InjectionWpf.GetService<PropertyControl>();

    public ObservableCollection<NamesModel> DataGridItemsSource
    {
        get => _DataGridItemsSource;
        set => SetProperty(ref _DataGridItemsSource, value);
    }
    private ObservableCollection<NamesModel> _DataGridItemsSource = new ObservableCollection<NamesModel>();

    /// <summary>
    /// 插件选中
    /// </summary>
    public NamesModel DataGridSelectedItem
    {
        get => GetProperty(() => DataGridSelectedItem);
        set => SetProperty(() => DataGridSelectedItem, value);
    }


    /// <summary>
    /// 存储路径
    /// </summary>
    public string SavePath
    {
        get => GetProperty(() => SavePath);
        set => SetProperty(() => SavePath, value);
    }
    public IAsyncRelayCommand SelectSavePath => selectSavePath ??= new AsyncRelayCommand(SelectSavePathAsync);
    private IAsyncRelayCommand selectSavePath;
    private async Task SelectSavePathAsync()
    {
        SavePath = Win32Handler.Select("请选择文件夹".GetLanguageValue(), true);
    }


    /// <summary>
    /// 内容菜单打开触发
    /// </summary>
    public IAsyncRelayCommand DataGrid_ContextMenuOpening => dataGrid_ContextMenuOpening ??= new AsyncRelayCommand<ContextMenuEventArgs>(DataGrid_ContextMenuOpeningAsync);
    private IAsyncRelayCommand? dataGrid_ContextMenuOpening;
    private async Task DataGrid_ContextMenuOpeningAsync(ContextMenuEventArgs? e)
    {
        if (e?.Source is not DataGrid dataGrid)
            return;

        // 最终裁决：
        // 只要当前不是“行右键”，就禁止弹出
        //if (dataGrid.SelectedItem == null)
        //{
        //    e.Handled = true;
        //}
    }

    /// <summary>
    /// 鼠标右键点击触发
    /// </summary>
    public IAsyncRelayCommand DataGrid_PreviewMouseRightButtonDown => dataGrid_PreviewMouseRightButtonDown ??= new AsyncRelayCommand<MouseButtonEventArgs>(DataGrid_PreviewMouseRightButtonDownAsync);
    private IAsyncRelayCommand? dataGrid_PreviewMouseRightButtonDown;
    private async Task DataGrid_PreviewMouseRightButtonDownAsync(MouseButtonEventArgs? e)
    {
        if (e?.Source is not DataGrid dataGrid)
            return;

        System.Windows.DependencyObject dep = (System.Windows.DependencyObject)e.OriginalSource;

        while (dep != null && dep is not DataGridRow)
            dep = VisualTreeHelper.GetParent(dep);

        if (dep is DataGridRow row)
        {
            // 右键在行上
            dataGrid.SelectedItem = row.Item;
            row.IsSelected = true;
            row.Focus();
        }
        else
        {
            // 右键空白：清空选择
            dataGrid.SelectedItem = null;
            e.Handled = true; // 阻止默认右键
        }
    }


    public IAsyncRelayCommand AddItem => addItem ??= new AsyncRelayCommand(AddItemAsync);
    private IAsyncRelayCommand addItem;
    private async Task AddItemAsync()
    {
        NamesModel names = new NamesModel();
        names.Index = DataGridItemsSource.Count;
        param.SetBasics(names);
        if ((await DialogHost.Show(param, "DialogHost")).ToBool())
        {
            NamesModel model = param.GetBasics().GetSource<NamesModel>();
            if (!model.IsNull())
            {
                DataGridItemsSource.Add(model);
            }
        }
    }


    public IAsyncRelayCommand UpdateItem => updateItem ??= new AsyncRelayCommand(UpdateItemAsync);
    private IAsyncRelayCommand updateItem;
    private async Task UpdateItemAsync()
    {
        NamesModel names = DataGridSelectedItem.DeepCopy();
        param.SetBasics(names);
        if ((await DialogHost.Show(param, "DialogHost")).ToBool())
        {
            NamesModel model = param.GetBasics().GetSource<NamesModel>();
            if (!model.IsNull())
            {
                DataGridSelectedItem.Name = model.Name;
                DataGridSelectedItem.Describe = model.Describe;
            }
        }
    }


    public IAsyncRelayCommand RemoveItem => removeItem ??= new AsyncRelayCommand(RemoveItemAsync);
    private IAsyncRelayCommand removeItem;
    private async Task RemoveItemAsync()
    {
        DataGridItemsSource.Remove(DataGridSelectedItem);
        int index = 0;
        foreach (var item in DataGridItemsSource)
        {
            item.Index = index;
            index++;
        }
    }



    public IAsyncRelayCommand Handle => handle ??= new AsyncRelayCommand(HandleAsync);
    private IAsyncRelayCommand handle;
    private async Task HandleAsync()
    {
        if (SavePath.IsNullOrWhiteSpace())
        {
            await MessageBox.Show("存储路径为空".GetLanguageValue(App.LanguageOperate), "提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            return;
        }

        if (DataGridItemsSource.Count > 0)
        {
            foreach (var item in DataGridItemsSource)
            {
                try
                {
                    if (item.Path.IsNullOrWhiteSpace())
                    {
                        await MessageBox.Show(item.Index + "，图片路径为空".GetLanguageValue(App.LanguageOperate), "提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                        return;
                    }
                    //开始处理
                    List<string> labels = CollectFiles(item.Path, LabelFolderName, ["txt"]);
                    List<string> images = CollectFiles(item.Path, ImageFolderName, ["jpg", "jpeg", "png", "bmp", "tif", "tiff"]);

                    foreach (var label in labels)
                    {
                        ReplaceClassId(label, item.Index);
                    }
                    //把数据移动到指定文件夹
                    string saveLabel = Path.Combine(SavePath, LabelFolderName);
                    string saveImage = Path.Combine(SavePath, ImageFolderName);
                    Directory.CreateDirectory(saveLabel);
                    Directory.CreateDirectory(saveImage);

                    // 复制 labels
                    foreach (var label in labels)
                    {
                        string destLabel = Path.Combine(saveLabel, Path.GetFileName(label));
                        File.Copy(label, destLabel, overwrite: true);
                    }

                    // 复制 images
                    foreach (var image in images)
                    {
                        string destImage = Path.Combine(saveImage, Path.GetFileName(image));
                        File.Copy(image, destImage, overwrite: true);
                    }
                    IDictionary<int, string> names = DataGridItemsSource.ToDictionary(item => item.Index, item => item.Name);
                    CreateConfigYaml(SavePath, ImageFolderName, names);

                }
                catch (Exception ex)
                {
                    await MessageBox.Show("处理异常：".GetLanguageValue(App.LanguageOperate) + ex.Message, "提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                }

            }
            await MessageBox.Show("处理完成".GetLanguageValue(App.LanguageOperate), "提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
            OpenFolder(SavePath);
        }
    }

    public string ImageFolderName
    {
        get => imageFolderName;
        set => SetProperty(ref imageFolderName, value);
    }
    private string imageFolderName = "images";


    public string LabelFolderName
    {
        get => labelFolderName;
        set => SetProperty(ref labelFolderName, value);
    }
    private string labelFolderName = "labels";

    /// <summary>
    /// 在指定根目录下，递归查找所有指定名称的文件夹，
    /// 并收集这些文件夹中的指定类型文件
    /// </summary>
    /// <param name="rootPath">根目录</param>
    /// <param name="targetFolderName">目标文件夹名称（如 images、labels 等）</param>
    /// <param name="extensions">
    /// 允许的文件扩展名（如 ".jpg", ".txt"），
    /// 传 null 或空集合表示不限制类型
    /// </param>
    /// <returns>符合条件的文件完整路径集合</returns>
    public List<string> CollectFiles(
        string rootPath,
        string targetFolderName,
        IEnumerable<string>? extensions = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("rootPath 不能为空");

        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException(rootPath);

        if (string.IsNullOrWhiteSpace(targetFolderName))
            throw new ArgumentException("targetFolderName 不能为空");

        HashSet<string>? extSet = null;

        if (extensions != null)
        {
            extSet = new HashSet<string>(
                extensions.Select(e => e.StartsWith(".") ? e.ToLower() : "." + e.ToLower())
            );
        }

        var result = new List<string>();

        // 查找所有目标文件夹
        var targetDirs = Directory.EnumerateDirectories(
            rootPath,
            targetFolderName,
            SearchOption.AllDirectories);

        foreach (var dir in targetDirs)
        {
            var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly);

            foreach (var file in files)
            {
                if (extSet == null)
                {
                    result.Add(file);
                }
                else
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (extSet.Contains(ext))
                        result.Add(file);
                }
            }
        }

        return result;
    }


    /// <summary>
    /// 将 YOLO label 文件中每一行的 class id
    /// 强制修改为指定的 classId，其它数据保持不变
    /// </summary>
    public static void ReplaceClassId(string labelFilePath, int newClassId)
    {
        var lines = File.ReadAllLines(labelFilePath);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 5)
                continue; // 非法行，直接跳过（防御式）

            parts[0] = newClassId.ToString();
            lines[i] = string.Join(" ", parts);
        }

        File.WriteAllLines(labelFilePath, lines);
    }




    /// <summary>
    /// 在指定目录下创建 YOLO 的 config.yaml 文件
    /// </summary>
    /// <param name="savePath">数据集根目录</param>
    /// <param name="imagePath">图片路径</param>
    /// <param name="names">类别字典（key=classId, value=className）</param>
    public void CreateConfigYaml(string savePath, string imagePathName, IDictionary<int, string> names)
    {
        if (string.IsNullOrWhiteSpace(savePath))
            throw new ArgumentException(nameof(savePath));

        if (names == null || names.Count == 0)
            throw new ArgumentException("names 不能为空");

        Directory.CreateDirectory(savePath);

        string yamlPath = Path.Combine(savePath, "config.yaml");

        var sb = new StringBuilder();

        sb.AppendLine("# 缺陷识别数据集配置");
        sb.AppendLine($"path: {savePath}");
        sb.AppendLine($"train: {imagePathName}");
        sb.AppendLine($"val: {imagePathName}");
        sb.AppendLine("test:");
        sb.AppendLine();
        sb.AppendLine("# 类别名称");
        sb.AppendLine("names:");

        foreach (var kv in names)
        {
            sb.AppendLine($"  {kv.Key}: {kv.Value}");
        }

        File.WriteAllText(yamlPath, sb.ToString(), Encoding.UTF8);
    }


    public void OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        if (!Directory.Exists(folderPath))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true
        });
    }
}
