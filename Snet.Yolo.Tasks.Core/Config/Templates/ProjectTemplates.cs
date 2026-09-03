namespace Snet.Yolo.Tasks.Core.Config.Templates;

using System.Collections.Generic;
using System.Linq;

/// <summary>内置模板元数据。</summary>
public sealed record ProjectTemplate(string Id, string TitleEn, string TitleZh, string Group, string ConfigXml);

/// <summary>
/// 内置标签配置模板库：配置 XML 逐字取自上游
/// label_studio/annotation_templates 各模板的官方 config.xml/config.yml。
/// </summary>
public static class ProjectTemplates
{
    private const string CvGroup = "Computer Vision";
    private const string NlpGroup = "Natural Language Processing";
    private const string AudioGroup = "Audio & Speech";

    /// <summary>全部内置模板。</summary>
    public static IReadOnlyList<ProjectTemplate> All { get; } = Build();

    /// <summary>按 id 查找模板。</summary>
    public static ProjectTemplate? Find(string id) => All.FirstOrDefault(template => template.Id == id);

    private static List<ProjectTemplate> Build() => new()
    {
        new("object-detection-with-bounding-boxes", "Object Detection with Bounding Boxes", "目标检测（边界框）", CvGroup, BboxConfig),
        new("semantic-segmentation-with-polygons", "Semantic Segmentation with Polygons", "语义分割（多边形）", CvGroup, PolygonConfig),
        new("semantic-segmentation-with-masks", "Semantic Segmentation with Masks", "语义分割（笔刷掩码）", CvGroup, BrushConfig),
        new("image-shapes-lab", "Image Annotation Lab (Rect/Polygon/KeyPoint/Ellipse)", "图像标注实验室（矩形/多边形/关键点/椭圆）", CvGroup, ShapesLabConfig),
        new("keypoint-labeling", "Keypoint Labeling", "关键点标注", CvGroup, KeyPointConfig),
        new("image-classification", "Image Classification", "图像分类", CvGroup, ImageClassificationConfig),
        new("named-entity-recognition", "Named Entity Recognition", "命名实体识别", NlpGroup, NerConfig),
        new("text-classification", "Text Classification", "文本分类", NlpGroup, TextClassificationConfig),
        new("automatic-speech-recognition", "Automatic Speech Recognition", "自动语音识别（转写）", AudioGroup, AsrConfig),
        new("automatic-speech-recognition-segments", "Automatic Speech Recognition (Segments)", "自动语音识别（分段转写）", AudioGroup, AsrSegmentsConfig),
        new("sound-event-detection", "Sound Event Detection", "声音事件检测", AudioGroup, SoundEventConfig),
    };

    private const string ShapesLabConfig = "\n<View>\n  <Image name=\"image\" value=\"$image\" zoom=\"true\"/>\n  <RectangleLabels name=\"rect\" toName=\"image\"><Label value=\"Car\" background=\"blue\" hotkey=\"1\"/></RectangleLabels>\n  <PolygonLabels name=\"poly\" toName=\"image\"><Label value=\"Zone\" background=\"red\" hotkey=\"2\"/></PolygonLabels>\n  <KeyPointLabels name=\"kp\" toName=\"image\"><Label value=\"Nose\" background=\"green\" hotkey=\"3\"/></KeyPointLabels>\n  <EllipseLabels name=\"ell\" toName=\"image\"><Label value=\"Ring\" background=\"orange\" hotkey=\"4\"/></EllipseLabels>\n  <BrushLabels name=\"brush\" toName=\"image\"><Label value=\"Mask\" background=\"#9b59b6\" hotkey=\"5\"/></BrushLabels>\n</View>\n";

    private const string BboxConfig = "\n<View>\n  <Image name=\"image\" value=\"$image\"/>\n  <RectangleLabels name=\"label\" toName=\"image\">\n    <Label value=\"Airplane\" background=\"green\"/>\n    <Label value=\"Car\" background=\"blue\"/>\n  </RectangleLabels>\n</View>\n";

    private const string PolygonConfig = "\n<View>\n  <Header value=\"Select label and click the image to start\"/>\n  <Image name=\"image\" value=\"$image\" zoom=\"true\"/>\n  <PolygonLabels name=\"label\" toName=\"image\" strokeWidth=\"3\" pointSize=\"small\" opacity=\"0.9\">\n    <Label value=\"Airplane\" background=\"red\"/>\n    <Label value=\"Car\" background=\"blue\"/>\n  </PolygonLabels>\n</View>\n";

    private const string BrushConfig = "\n<View>\n  <Image name=\"image\" value=\"$image\" zoom=\"true\"/>\n  <BrushLabels name=\"tag\" toName=\"image\">\n    <Label value=\"Airplane\" background=\"rgba(255, 0, 0, 0.7)\"/>\n    <Label value=\"Car\" background=\"rgba(0, 0, 255, 0.7)\"/>\n  </BrushLabels>\n</View>\n";

    private const string KeyPointConfig = "\n<View>\n  <KeyPointLabels name=\"kp-1\" toName=\"img-1\">\n    <Label value=\"Face\" background=\"red\" />\n    <Label value=\"Nose\" background=\"green\" />\n  </KeyPointLabels>\n  <Image name=\"img-1\" value=\"$img\" />\n</View>\n";

    private const string ImageClassificationConfig = "\n<View>\n  <Image name=\"image\" value=\"$image\"/>\n  <Choices name=\"choice\" toName=\"image\">\n    <Choice value=\"Adult content\"/>\n    <Choice value=\"Weapons\" />\n    <Choice value=\"Violence\" />\n  </Choices>\n</View>\n";

    private const string NerConfig = "\n<View>\n  <Labels name=\"label\" toName=\"text\">\n    <Label value=\"PER\" background=\"red\"/>\n    <Label value=\"ORG\" background=\"darkorange\"/>\n    <Label value=\"LOC\" background=\"orange\"/>\n    <Label value=\"MISC\" background=\"green\"/>\n  </Labels>\n\n  <Text name=\"text\" value=\"$text\"/>\n</View>\n";

    private const string TextClassificationConfig = "\n<View>\n  <Text name=\"text\" value=\"$text\"/>\n  <View style=\"box-shadow: 2px 2px 5px #999; padding: 20px; margin-top: 2em; border-radius: 5px;\">\n    <Header value=\"Choose text sentiment\"/>\n    <Choices name=\"sentiment\" toName=\"text\" choice=\"single\" showInLine=\"true\">\n      <Choice value=\"Positive\"/>\n      <Choice value=\"Negative\"/>\n      <Choice value=\"Neutral\"/>\n    </Choices>\n  </View>\n</View>\n";

    private const string AsrConfig = "\n<View>\n  <Audio name=\"audio\" value=\"$audio\" zoom=\"true\" hotkey=\"ctrl+enter\" />\n  <Header value=\"Provide Transcription\" />\n  <TextArea name=\"transcription\" toName=\"audio\" rows=\"4\" editable=\"true\" maxSubmissions=\"1\" />\n</View>\n";

    private const string AsrSegmentsConfig = "\n<View>\n  <Labels name=\"labels\" toName=\"audio\">\n    <Label value=\"Speech\" />\n    <Label value=\"Noise\" />\n  </Labels>\n\n  <Audio name=\"audio\" value=\"$audio\"/>\n\n  <TextArea name=\"transcription\" toName=\"audio\" rows=\"2\" editable=\"true\" perRegion=\"true\" required=\"true\" />\n</View>\n";

    private const string SoundEventConfig = "\n<View>\n  <Labels name=\"label\" toName=\"audio\" zoom=\"true\" hotkey=\"ctrl+enter\">\n    <Label value=\"Event A\" background=\"red\"/>\n    <Label value=\"Event B\" background=\"green\"/>\n  </Labels>\n  <Audio name=\"audio\" value=\"$audio\"/>\n</View>\n";
}
