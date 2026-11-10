using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snet.Yolo.Server.sam;

/// <summary>不可变的已安装版本定位；旧版本保留供回退与已有图片编码使用。</summary>
/// <param name="Definition">兼容目录中的模型及实际锁定的仓库提交。</param>
/// <param name="Directory">已校验文件的独立目录或首次安装目录。</param>
public sealed record SamInstalledModel(SamModelDefinition Definition, string Directory);
