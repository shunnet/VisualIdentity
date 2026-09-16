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
}
