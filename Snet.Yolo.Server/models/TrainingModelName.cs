using System.Globalization;

namespace Snet.Yolo.Server.models;

/// <summary>统一训练模型进入验证列表时的时间后缀。</summary>
public static class TrainingModelName
{
    /// <summary>将训练完成的 UTC 时间转换为服务器本地时间，追加 HHmmssffff 后缀。</summary>
    /// <param name="name">不含扩展名的模型名称。</param>
    /// <param name="completedAtUtc">本次训练产物的完成时间，而非点击验证按钮的时间。</param>
    /// <returns>含四位小数秒的可读模型名称。</returns>
    public static string Append(string name, DateTime completedAtUtc)
        => name + "_" + completedAtUtc.ToLocalTime().ToString("HHmmssffff", CultureInfo.InvariantCulture);
}
