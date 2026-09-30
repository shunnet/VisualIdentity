using Snet.VisualIdentity.JointVerificationDemo.Configuration;
using Snet.VisualIdentity.JointVerificationDemo.Inference;
using Snet.VisualIdentity.JointVerificationDemo.Output;
using Snet.Yolo.Server.anomalib;
using System.Diagnostics;
using System.Text;

namespace Snet.VisualIdentity.JointVerificationDemo;

/// <summary>控制台入口只做配置读取、流程编排和结果输出，模型推理与绘图各自独立。</summary>
internal static class Program
{
    /// <summary>执行一次完整识别；Ctrl+C 取消，退出码为 0 成功、1 失败、2 取消。</summary>
    /// <param name="args">可选的 JSON 配置路径；不指定时读取程序集目录的 demo.json。</param>
    /// <returns>进程退出码，方便视频平台或自动化脚本检查结果。</returns>
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            if (args.Length > 1) { throw new ArgumentException("用法：dotnet Snet.VisualIdentity.JointVerificationDemo.dll [配置文件路径]"); }
            var path = args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "demo.json");
            var options = await DemoOptions.LoadAsync(path, cancellation.Token);
            Console.WriteLine($"设备：{RuntimeFactory.DeviceName}；模式：{options.Mode}；图片：{options.ImagePath}");
            // 视频平台应把 engine 的生命周期提升至摄像头或工作进程级，反复调用 IdentifyAsync。
            var timer = Stopwatch.StartNew();
            await using var engine = new JointVerificationEngine(options);
            var output = await engine.IdentifyAsync(options.ImagePath, LogStage, cancellation.Token);
            timer.Stop();
            using var writer = new ResultWriter(options.FontPath);
            var directory = await writer.SaveAsync(options.ImagePath, output, timer.ElapsedMilliseconds, cancellation.Token);
            Console.WriteLine($"完成：异常区域 {output.Matches.Regions.Count}，已知缺陷 {output.Matches.Regions.Sum(region => region.Detections.Count) + output.Matches.UnmatchedDetections.Count}，整体耗时 {timer.ElapsedMilliseconds} ms。");
            Console.WriteLine($"结果已保存：{directory}");
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("识别已取消。"); return 2; }
        catch (Exception error) { Console.Error.WriteLine($"识别失败：{error}"); return 1; }
        finally { Console.CancelKeyPress -= handler; }
    }

    /// <summary>输出中文阶段日志；平台可将同样的回调接到自己的日志或监控系统。</summary>
    /// <param name="stage">Server 定义的推理阶段。</param>
    private static void LogStage(JointValidationStage stage) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] " + (stage switch
    {
        JointValidationStage.AnomalibStarted => "Anomalib 开始定位异常。",
        JointValidationStage.AnomalibCompleted => "Anomalib 异常定位完成。",
        JointValidationStage.YoloStarted => "YOLO 开始识别缺陷。",
        JointValidationStage.YoloCompleted => "YOLO 缺陷识别完成。",
        _ => "整图判为正常，跳过 YOLO。",
    }));
}
