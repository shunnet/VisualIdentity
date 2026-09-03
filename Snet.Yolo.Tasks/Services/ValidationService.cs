using Snet.Yolo.Server;
using Snet.Yolo.Server.@interface;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using Snet.Model.data;
using System.Net.Http;
using System.Text;

namespace Snet.Yolo.Tasks.Services;

/// <summary>
/// 验证服务：模型管理(ManageOperate) + 推理(本机 IdentityOperate / 远程 Api.*)。
/// </summary>
public sealed class ValidationService
{
    private readonly ManageOperate _manage;
    private readonly IHttpClientFactory _http;

    public ValidationService(ManageOperate manage, IHttpClientFactory http)
    {
        _manage = manage;
        _http = http;
    }

    /// <summary>查询全部模型。</summary>
    public async Task<IReadOnlyList<OnnxData>> GetModelsAsync()
    {
        var r = await _manage.QueryAsync();
        return r.GetDetails(out List<OnnxData>? list) ? (list ?? new()) : new();
    }

    /// <summary>添加模型。</summary>
    public async Task<OperateResult> AddModelAsync(Stream onnx, string name, string describe, OnnxType type)
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".onnx");
        using (var f = File.Create(tmp)) { await onnx.CopyToAsync(f); }
        var r = await _manage.AddAsync(tmp, describe, type);
        try { File.Delete(tmp); } catch { }
        return r;
    }

    /// <summary>删除模型。</summary>
    public Task<OperateResult> DeleteModelAsync(int index) => _manage.DeleteAsync(index, true);

    /// <summary>更新模型。</summary>
    public Task<OperateResult> UpdateModelAsync(int index, string describe, OnnxType? type) => _manage.UpdateAsync(index, describe, type);

    /// <summary>本机推理（进程内 IdentityOperate）。</summary>
    public async Task<OperateResult> RunLocalAsync(OnnxData model, byte[] image, string paramJson)
    {
        using var op = new IdentityOperate();
        IData data = model.onnxType switch
        {
            OnnxType.Classification => new ClassificationData(image, ParseClasses(paramJson)),
            OnnxType.ObbDetection => new ObbDetectionData(image, ParseConf(paramJson, "Confidence"), ParseConf(paramJson, "Iou")),
            OnnxType.Segmentation => new SegmentationData(image, ParseConf(paramJson, "Confidence"), ParseConf(paramJson, "PixelConfedence"), ParseConf(paramJson, "Iou")),
            OnnxType.PoseEstimation => new PoseEstimationData(image, ParseConf(paramJson, "Confidence"), ParseConf(paramJson, "Iou")),
            _ => new ObjectDetectionData(image, ParseConf(paramJson, "Confidence"), ParseConf(paramJson, "Iou")),
        };
        // 用模型路径的 index 定位 —— IdentityOperate 自身维护模型上下文
        return await op.RunAsync(data);
    }

    /// <summary>远程 API 推理。</summary>
    public async Task<OperateResult> RunRemoteAsync(string baseUrl, OnnxData model, byte[] image, string paramJson)
    {
        var client = _http.CreateClient();
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(model.index.ToString()), "onnxIndex");
        content.Add(new StringContent(paramJson), "paramJson");
        content.Add(new ByteArrayContent(image), "file", "image.jpg");
        var resp = await client.PostAsync($"{baseUrl.TrimEnd('/')}/Operate/IdentityAsync", content);
        var json = await resp.Content.ReadAsStringAsync();
        return System.Text.Json.JsonSerializer.Deserialize<OperateResult>(json) ?? OperateResult.CreateFailureResult("解析失败: " + json);
    }

    private static int ParseClasses(string json) { try { return JsonDoc(json)["Classes"]?.GetValue<int>() ?? 1; } catch { return 1; } }
    private static double ParseConf(string json, string key) { try { return JsonDoc(json)[key]?.GetValue<double>() ?? 0.2; } catch { return 0.2; } }
    private static System.Text.Json.Nodes.JsonObject JsonDoc(string json) { try { return System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject() ?? new(); } catch { return new(); } }
}
