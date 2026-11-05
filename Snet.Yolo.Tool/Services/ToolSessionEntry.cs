using Snet.Yolo.Server;
using Snet.Yolo.Server.yolo.Models.Interfaces;

namespace Snet.Yolo.Tool.Services;

/// <summary>由运行服务的锁保护的模型会话及活动使用计数。</summary>
/// <param name="modelPath">模型绝对路径。</param>
/// <param name="modelStamp">模型最后修改时间。</param>
/// <param name="modelLength">模型文件长度。</param>
/// <param name="operate">缓存的识别操作。</param>
/// <param name="provider">条目持有的原生执行提供程序。</param>
internal sealed class ToolSessionEntry(string modelPath, long modelStamp, long modelLength, IdentityOperate operate, IExecutionProvider provider)
{
    /// <summary>模型的绝对路径。</summary>
    public string ModelPath { get; } = modelPath;
    /// <summary>模型最后修改时间，用于识别同路径文件的更新。</summary>
    public long ModelStamp { get; } = modelStamp;
    /// <summary>创建会话时的模型文件长度。</summary>
    public long ModelLength { get; } = modelLength;
    /// <summary>可串行复用的识别操作。</summary>
    public IdentityOperate Operate { get; } = operate;
    /// <summary>原生执行提供程序；即使首张图片加载失败，也由条目负责释放。</summary>
    public IExecutionProvider Provider { get; } = provider;
    /// <summary>当前仍在使用会话的识别数量。</summary>
    public int Users { get; set; }
    /// <summary>是否已因模型、设备变化或程序关闭而退役。</summary>
    public bool Retired { get; set; }
    /// <summary>是否已经取得一次性的释放权。</summary>
    public bool Disposed { get; set; }
}
