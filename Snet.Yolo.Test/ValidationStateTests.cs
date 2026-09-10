using Snet.Yolo.Tasks.Services;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证进程级图片状态的用户、模型和图片隔离行为。</summary>
public sealed class ValidationStateTests
{
    /// <summary>确保每个模型只恢复自己的图片队列和选中图片。</summary>
    [Fact]
    public void ImagesAndSelection_AreIsolatedByUserAndModel()
    {
        var state = new ValidationState();
        var first = state.AddImage("snet", 1, "first.jpg", "/uploads/val/first.jpg");
        var second = state.AddImage("snet", 1, "second.jpg", "/uploads/val/second.jpg");
        state.AddImage("snet", 2, "other-model.jpg", "/uploads/val/other-model.jpg");
        state.AddImage("other-user", 1, "other-user.jpg", "/uploads/val/other-user.jpg");

        Assert.True(state.SelectImage("snet", 1, first.Id));
        var model = state.GetModel("snet", 1);

        Assert.Equal(first.Id, model.SelectedImageId);
        Assert.Equal(new[] { "first.jpg", "second.jpg" }, model.Images.Select(image => image.Name));
        Assert.Equal("other-model.jpg", Assert.Single(state.GetModel("snet", 2).Images).Name);
        Assert.Equal("other-user.jpg", Assert.Single(state.GetModel("other-user", 1).Images).Name);
        Assert.NotEqual(second.Id, model.SelectedImageId);
    }

    /// <summary>确保识别结果只写回执行识别的那一张图片。</summary>
    [Fact]
    public void Result_IsStoredAgainstTheMatchingImage()
    {
        var state = new ValidationState();
        var first = state.AddImage("snet", 1, "first.jpg", "/uploads/val/first.jpg");
        var second = state.AddImage("snet", 1, "second.jpg", "/uploads/val/second.jpg");

        state.SetResult("snet", 1, first.Id, "result-one", new[] { new ValidationDetection("part", "90%", "1,2") });

        var model = state.GetModel("snet", 1);
        var firstSnapshot = Assert.Single(model.Images, image => image.Id == first.Id);
        var secondSnapshot = Assert.Single(model.Images, image => image.Id == second.Id);
        Assert.Equal("result-one", firstSnapshot.ResultJson);
        Assert.Equal("part", Assert.Single(firstSnapshot.Detections).Name);
        Assert.Null(secondSnapshot.ResultJson);
        Assert.Empty(secondSnapshot.Detections);
    }

    /// <summary>确保新进程对应的新状态实例不会恢复旧实例的数据。</summary>
    [Fact]
    public void NewInstance_StartsEmpty()
    {
        var previousProcess = new ValidationState();
        previousProcess.SelectModel("snet", 1);
        previousProcess.AddImage("snet", 1, "first.jpg", "/uploads/val/first.jpg");

        var restartedProcess = new ValidationState();

        Assert.Null(restartedProcess.GetSelectedModel("snet"));
        Assert.Empty(restartedProcess.GetModel("snet", 1).Images);
    }
}
