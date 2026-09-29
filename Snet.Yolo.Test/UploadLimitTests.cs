namespace Snet.Yolo.Test;

using Snet.Yolo.Tasks.Services;
using Xunit;

/// <summary>验证界面上传限制与产品约定保持一致。</summary>
public sealed class UploadLimitTests
{
    /// <summary>单张图片上限应为 100 MiB。</summary>
    [Fact]
    public void MaximumImageFileBytes_IsOneHundredMebibytes()
    {
        Assert.Equal(100L * 1024 * 1024, UploadedFileValidator.MaximumImageFileBytes);
    }

    /// <summary>
    /// 数据集 ZIP 上限必须覆盖 Roboflow 常见的多 GB 数据集。
    /// 这个常量同时是 InputFile.OpenReadStream 的 maxAllowedSize，
    /// 它一旦小于用户手里的包，表现就是“文件选完就报大小无效”，3 GB 的包永远传不进来。
    /// </summary>
    [Fact]
    public void MaximumArchiveBytes_AllowsMultiGigabyteDatasets()
    {
        Assert.Equal(16L * 1024 * 1024 * 1024, UploadCenter.MaxArchiveBytes);
        Assert.True(UploadCenter.MaxArchiveBytes >= 3L * 1024 * 1024 * 1024);

        Assert.True(UploadCenter.IsAcceptable(UploadKind.ProjectYoloArchive, "dataset.zip", 3L * 1024 * 1024 * 1024, out var reason), reason);
        Assert.True(UploadCenter.IsAcceptable(UploadKind.ProjectYoloArchive, "dataset.zip", UploadCenter.MaxArchiveBytes, out reason), reason);
        Assert.False(UploadCenter.IsAcceptable(UploadKind.ProjectYoloArchive, "dataset.zip", UploadCenter.MaxArchiveBytes + 1, out _));
    }

    /// <summary>
    /// 上传/导入的资源上限必须容得下多 GB 数据集：3 GB 的包解出来可能是几万张图。
    /// 这些上限只是防炸弹的护栏，不能让正常数据集卡在上传之后才失败。
    /// </summary>
    [Fact]
    public void DatasetImportGuards_ScaleWithMultiGigabyteArchives()
    {
        Assert.True(Snet.Yolo.Tasks.Core.Serialization.Import.YoloWithImagesImporter.MaximumImageCount >= 10_000);
        Assert.True(Snet.Yolo.Tasks.Core.Serialization.Import.YoloWithImagesImporter.MaximumEntryCount >= Snet.Yolo.Tasks.Core.Serialization.Import.YoloWithImagesImporter.MaximumImageCount * 2);
        Assert.True(Snet.Yolo.Tasks.Core.Serialization.Import.YoloWithImagesImporter.MaximumTotalImageBytes >= 3L * 1024 * 1024 * 1024);
    }
}
