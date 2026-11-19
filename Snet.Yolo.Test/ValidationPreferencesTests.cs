using Microsoft.Extensions.Logging.Abstractions;
using Snet.Yolo.Server.anomalib;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证重启后的设置、历史模型命名及模型上传归属。</summary>
public sealed class ValidationPreferencesTests
{
    /// <summary>从新服务实例恢复模型参数和选择，各用户、模型和页面互不覆盖。</summary>
    [Fact]
    public void Settings_SurviveNewInstanceAndRemainIsolated()
    {
        var root = Path.Combine(Path.GetTempPath(), "validation-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ValidationPreferences(NullLogger<ValidationPreferences>.Instance, root);
            Assert.True(store.Save("alice", "validation:files:1", new Dictionary<string, double> { ["Confidence"] = 0.42 }));
            Assert.True(store.Save("alice", "validation:files:2", new Dictionary<string, double> { ["Confidence"] = 0.8 }));
            Assert.True(store.Save("bob", "validation:files:1", new Dictionary<string, double> { ["Confidence"] = 0.12 }));
            Assert.True(store.Save("alice", "anomalib-validation:project/run", new AnomalibValidationParameters(.34f, 64)));
            Assert.True(store.Save("alice", "joint:anomalib:project/run", new AnomalibValidationParameters(.7f, 24)));
            Assert.True(store.Save("alice", "joint:selected", new JointValidationSelection("project/run", "2", JointValidationMode.YoloOnly)));

            var restarted = new ValidationPreferences(NullLogger<ValidationPreferences>.Instance, root);
            Assert.Equal(.42, restarted.Read<Dictionary<string, double>>("alice", "validation:files:1")!["Confidence"]);
            Assert.Equal(.8, restarted.Read<Dictionary<string, double>>("alice", "validation:files:2")!["Confidence"]);
            Assert.Equal(.12, restarted.Read<Dictionary<string, double>>("bob", "validation:files:1")!["Confidence"]);
            Assert.Equal(new(.34f, 64), restarted.Read<AnomalibValidationParameters>("alice", "anomalib-validation:project/run"));
            Assert.Equal(new(.7f, 24), restarted.Read<AnomalibValidationParameters>("alice", "joint:anomalib:project/run"));
            Assert.Equal(new("project/run", "2", JointValidationMode.YoloOnly), restarted.Read<JointValidationSelection>("alice", "joint:selected"));
        }
        finally { TestFileCleanup.DeleteDirectory(root); }
    }

    /// <summary>旧版 YOLO 注册文件仍可显示工程名，时刻来自数据库登记时间。</summary>
    [Theory]
    [InlineData("缺陷-best_5b6bf6f2_2049403859.onnx", null, "缺陷·12:34:56")]
    [InlineData("缺陷-best_2ba82def_ae40799a.onnx", null, "缺陷·12:34:56")]
    [InlineData("some_file_12345678.onnx", "手工命名", "手工命名·12:34:56")]
    public void YoloName_UsesRegistrationTimeAndHidesStorageSuffix(string file, string? custom, string expected)
        => Assert.Equal(expected, ValidationModelDisplayName.ForYolo(new OnnxData
        {
            name = file, validationName = custom, createTime = new DateTime(2026, 11, 19, 12, 34, 56),
        }));

    /// <summary>历史上传记录中的公共范围按真实目标恢复，模型 A 和模型 B 得到不同的进度键。</summary>
    [Fact]
    public void ValidationUploads_ResolveScopeFromPersistedTarget()
    {
        var first = new UploadIntent(UploadKind.ValidationImages, "validation:files", ModelIndex: 1);
        var second = first with { ModelIndex = 2 };
        Assert.Equal("validation:files:1", first.ValidationScopeKey);
        Assert.NotEqual(first.ValidationScopeKey, second.ValidationScopeKey);
        var anomaly = new UploadIntent(UploadKind.AnomalibValidationImages, "anomalib-validation:placeholder", "project", RunId: "first");
        Assert.Equal("anomalib-validation:project/first", anomaly.ValidationScopeKey);
        Assert.NotEqual(anomaly.ValidationScopeKey, (anomaly with { RunId = "second" }).ValidationScopeKey);
    }
}
