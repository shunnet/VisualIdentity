namespace Snet.Yolo.Server.anomalib;

/// <summary>
/// 某个异常检测模型的输入尺寸建议。依据是同一批真实数据（150 张正常图训练、38 张留出正常图、121 张含异物的真实异常图，
/// 原图 5120×5120）上的离线对比：
///   · PatchCore：384 px AUROC 0.989、512 px 0.988、640 px 0.984；耗时 19 s / 56 s / 140 s，训练显存峰值 4.5 / 8.0 / 12.5 GB；
///   · PaDiM：192 px 0.940、256 px 0.947、320 px 0.928、384 px 0.903、512 px 0.839 —— 越大越差，显存按边长平方增长。
/// </summary>
/// <param name="Recommended">推荐尺寸（像素，32 的倍数）。</param>
/// <param name="WarnAbove">超过它时页面提示“明显高于建议”。</param>
public sealed record AnomalibSizeGuidance(int Recommended, int WarnAbove)
{
    /// <summary>给定尺寸是否高于建议上限。</summary>
    /// <param name="imageSize">输入尺寸。</param>
    public bool IsAboveRecommendedMaximum(int imageSize) => imageSize > WarnAbove;
}
