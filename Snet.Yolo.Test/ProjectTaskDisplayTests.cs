using System.Reflection;
using Snet.Yolo.Tasks.Components.Pages;
using Snet.Yolo.Tasks.Core.Config;
using Snet.Yolo.Tasks.Core.Workspace;
using Xunit;

namespace Snet.Yolo.Test;

/// <summary>验证工程卡片、详情与训练页使用一致的真实配置类型，不把 OBB 当作普通矩形检测。</summary>
public sealed class ProjectTaskDisplayTests
{
    /// <summary>五种创建模板在全部页面显示同一种任务，并保留对应图标。</summary>
    [Theory]
    [InlineData(YoloTaskType.Detect, "bi-bounding-box", "YoloDetect")]
    [InlineData(YoloTaskType.Segment, "bi-vector-pen", "YoloSegment")]
    [InlineData(YoloTaskType.Classify, "bi-tags", "YoloClassify")]
    [InlineData(YoloTaskType.Pose, "bi-dot", "YoloPose")]
    [InlineData(YoloTaskType.Obb, "bi-record-circle", "YoloObb")]
    public void CreatedProject_AllPagesShowSelectedTask(YoloTaskType task, string icon, string key)
        => AssertPages(YoloTaskRegistry.ConfigFor(task), icon, key);

    /// <summary>兼容旧控件和混合配置，XML 注释不能改变任务判定。</summary>
    [Theory]
    [InlineData("<BrushLabels name='mask' toName='image'/>", "", "bi-vector-pen", "YoloSegment")]
    [InlineData("<Labels name='class' toName='image'/>", "", "bi-tags", "YoloClassify")]
    [InlineData("<EllipseLabels name='ell' toName='image'/>", "", "bi-record-circle", "YoloObb")]
    [InlineData("<PolygonLabels name='poly' toName='image'/><EllipseLabels name='ell' toName='image'/>", "", "bi-vector-pen", "YoloSegment")]
    [InlineData("<KeyPointLabels name='kp' toName='image'/>", "yoloTask='OBB'", "bi-record-circle", "YoloObb")]
    [InlineData("<KeyPointLabels name='kp' toName='image'/><Choices name='class' toName='image'/>", "", "bi-dot", "YoloPose")]
    [InlineData("<!-- KeyPointLabels EllipseLabels --><RectangleLabels name='rect' toName='image'/>", "", "bi-bounding-box", "YoloDetect")]
    public void ExistingConfig_AllPagesUseSemanticTask(string controls, string attributes, string icon, string key)
        => AssertPages($"<View {attributes}><Image name='image' value='$image'/>{controls}</View>", icon, key);

    /// <summary>不可解析或空配置在所有页面一致回退，不因渲染工程卡片抛出异常。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("<View><KeyPointLabels")]
    public void InvalidConfig_AllPagesFallBackToDetection(string xml)
        => AssertPages(xml, "bi-bounding-box", "YoloDetect");

    /// <summary>调用实际编译的 Razor 页面显示逻辑，独立核对图标与本地化键。</summary>
    private static void AssertPages(string xml, string icon, string key)
    {
        const BindingFlags members = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        var project = new WorkspaceProject { LabelConfigXml = xml };
        var card = ((string Icon, string Key))typeof(Home).GetMethod("ProjTypeInfo", members)!.Invoke(null, [project])!;
        Assert.Equal((icon, key), card);

        var detail = new ProjectDetails();
        typeof(ProjectDetails).GetField("_project", members)!.SetValue(detail, project);
        Assert.Equal((icon, key), ((string Icon, string Key))typeof(ProjectDetails).GetProperty("TypeInfo", members)!.GetValue(detail)!);
        Assert.Equal(key == "YoloClassify", typeof(ProjectDetails).GetProperty("IsClassify", members)!.GetValue(detail));

        var task = typeof(TrainPage).GetMethod("ParseTaskType", members)!.Invoke(null, [xml]);
        Assert.Equal(key, typeof(TrainPage).GetMethod("TaskTypeKey", members)!.Invoke(null, [task]));
    }
}
