using System.Globalization;
using System.Text.RegularExpressions;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models.data;

namespace Snet.Yolo.Tasks.Services;

/// <summary>独立验证和联合验证共用模型名称及登记时间的展示方式。</summary>
public static partial class ValidationModelDisplayName
{
    /// <summary>优先使用登记名称，兼容旧版训练和上传生成的物理文件名。</summary>
    public static string ForYolo(OnnxData model)
    {
        var name = model.validationName;
        if (string.IsNullOrWhiteSpace(name))
        {
            var stem = Path.GetFileNameWithoutExtension(model.name) ?? string.Empty;
            var legacyTraining = TrainingFileSuffix().Match(stem);
            name = legacyTraining.Success ? stem[..legacyTraining.Index] : ImportedFileSuffix().Replace(stem, string.Empty);
        }
        return Format(name, model.createTime.Kind == DateTimeKind.Utc ? model.createTime.ToLocalTime() : model.createTime);
    }

    /// <summary>使用登记名称或训练工程名称，并追加登记时刻。</summary>
    public static string ForAnomalib(RegisteredAnomalibModel model, IReadOnlyDictionary<string, string> projectNames)
    {
        var name = model.HasCustomName ? model.Name
            : projectNames.GetValueOrDefault(model.ProjectId, AnomalibModelCatalog.Get(model.Model).DisplayName);
        return Format(name, model.RegisteredAtUtc.ToLocalTime());
    }

    /// <summary>生成统一的名称和时分秒，时间精度只用于展示。</summary>
    private static string Format(string name, DateTime localTime)
        => name.Trim() + "·" + localTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>匹配历史训练注册生成的 best、随机标识和旧时间后缀。</summary>
    [GeneratedRegex(@"-best_[0-9a-f]{8}_(?:[0-9]{10}|[0-9a-f]{8})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrainingFileSuffix();

    /// <summary>匹配普通导入的八位随机文件名后缀。</summary>
    [GeneratedRegex(@"_[0-9a-f]{8}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImportedFileSuffix();
}
