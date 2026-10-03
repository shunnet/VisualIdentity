namespace Snet.Yolo.Server.sam;

/// <summary>图片辅助标注支持的固定模型。</summary>
public enum SamModelKind
{
    /// <summary>轻量 MobileSAM。</summary>
    MobileSam,
    /// <summary>SAM 2.1 Hiera Tiny。</summary>
    Sam21Tiny,
    /// <summary>原版 SAM ViT-B。</summary>
    SamVitB,
    /// <summary>原版 SAM ViT-L，大型编码器。</summary>
    SamVitL,
    /// <summary>原版 SAM ViT-H，编码器使用独立外部权重文件。</summary>
    SamVitH,
    /// <summary>SAM 3 Tracker 图片点选分割；不包含文本概念分割与视频跟踪。</summary>
    Sam3
}

/// <summary>固定权重及其完整性信息；不接受用户输入的下载地址。</summary>
public sealed record SamModelDefinition(SamModelKind Kind, string Name, string Folder, string Repository, string Revision,
    string Encoder, long EncoderBytes, string EncoderHash, string Decoder, long DecoderBytes, string DecoderHash,
    string? Archive = null, long ArchiveBytes = 0, string? ArchiveHash = null,
    string? EncoderData = null, long EncoderDataBytes = 0, string? EncoderDataHash = null,
    string? DecoderData = null, long DecoderDataBytes = 0, string? DecoderDataHash = null)
{
    /// <summary>实际模型文件合计体积（MB），包含外部权重。</summary>
    public long ModelMegabytes => (EncoderBytes + DecoderBytes + EncoderDataBytes + DecoderDataBytes + 999_999) / 1_000_000;
}

/// <summary>固定 ONNX 模型版本；下载体积与运行时内存开销不是同一指标。</summary>
public static class SamModels
{
    /// <summary>按固定顺序显示模型。</summary>
    public static IReadOnlyList<SamModelDefinition> All { get; } = Array.AsReadOnly(new[] {
        new SamModelDefinition(SamModelKind.MobileSam, "MobileSAM", "", "Acly/MobileSAM", "0d3b403339b4674a82493d5e97964dd78089ddc8",
            "mobile_sam_image_encoder.onnx", 28_157_093, "580f5fb648ea1062c0aabc26217aed56921985f03f0cbbd852bba81d760cc749",
            "sam_mask_decoder_multi.onnx", 16_496_559, "8976b90a87ba50a6a72217a5ff994f7d25ce16f2229fcc1ed259e1294c622ffe"),
        new SamModelDefinition(SamModelKind.Sam21Tiny, "SAM 2.1 Tiny", "sam2.1-tiny", "vietanhdev/segment-anything-2.1-onnx-models", "6a3ac868340a3196a349050a6efae22a5acc0330",
            "sam2.1_hiera_tiny.encoder.onnx", 109_471_931, "667384d1e686de6828b841ac8a24db0fafa2b3452494225f82eeedac56141230",
            "sam2.1_hiera_tiny.decoder.onnx", 16_519_561, "c40f5aa7d37b681cd500481a85d44839fd81c93dce1e86271a2c866470d22105",
            "sam2.1_hiera_tiny_20260221.zip", 116_507_723, "c602cb3f6fd297312a415f885f0df3f0eef9fbb334b069c9d30e828f2ae7c69a"),
        new SamModelDefinition(SamModelKind.SamVitB, "SAM ViT-B", "sam-vit-b", "vietanhdev/segment-anything-onnx-models", "9effc01a9e135621d710d49159f1ffb0b6f724dc",
            "sam_vit_b_01ec64.encoder.onnx", 359_217_309, "04591a344ef042b4519ca98c6084ad14df8795d6d48be8d58e37966f47536aac",
            "sam_vit_b_01ec64.decoder.onnx", 16_500_212, "a49fff08496bc18faebd46e9268d483535feec50306c906a56a70ecf05ae4ac8",
            "sam_vit_b_01ec64.zip", 348_403_199, "e41630c522ac51309e1fedf6c7050c042a1823baf852c99a01abf677ebba430e"),
        new SamModelDefinition(SamModelKind.SamVitL, "SAM ViT-L", "sam-vit-l", "vietanhdev/segment-anything-onnx-models", "9effc01a9e135621d710d49159f1ffb0b6f724dc",
            "sam_vit_l_0b3195.encoder.onnx", 1_234_264_293, "2f74af6001586a13ab1a855f5db512b710a94504d00be1e3f858da9537c467d9",
            "sam_vit_l_0b3195.decoder.onnx", 16_500_248, "1a81769535ccf1d8f45f28018d84a4e7b3aa99d64468aa3cf6941af6dcee6f5e",
            "sam_vit_l_0b3195.zip", 1_161_467_885, "020632061d9d141306c74b08738875b468337d8a241d8ef9d1ab1f66d63e5c6a"),
        new SamModelDefinition(SamModelKind.SamVitH, "SAM ViT-H", "sam-vit-h", "vietanhdev/segment-anything-onnx-models", "9effc01a9e135621d710d49159f1ffb0b6f724dc",
            "sam_vit_h_4b8939.encoder.onnx", 1_761_008, "74b90f2bac8d6e5b605478f94f7bc9f53a2046396b56f18a2529be11195ba4f0",
            "sam_vit_h_4b8939.decoder.onnx", 16_500_272, "22cf85e35d14182f4b4712364264c06b22edbef63f065189586f080ef4e2f325",
            "sam_vit_h_4b8939.zip", 2_383_268_602, "aa49ea636afca48598d894dcf58fcf54942e4b96fa4577bed9d903ac4e7fc74e",
            "sam_vit_h_4b8939.encoder_data.bin", 2_548_104_192, "a0a745f5147c9efaec96cc37d5fcb68994838c5d8327a1cd03a2bb30a7838c41"),
        new SamModelDefinition(SamModelKind.Sam3, "SAM 3 Tracker", "sam3", "onnx-community/sam3-tracker-ONNX", "429305c8a5b3de597243d919a07e4e6bdcd00ef7",
            "onnx/vision_encoder.onnx", 1_275_304, "9f284aab8c3d8e81e9c79f7b566f9cea43b7bc9afdd920eee2390fb65b3db897",
            "onnx/prompt_encoder_mask_decoder.onnx", 213_114, "4f9ac85291d634ae36a21ce940e3c09671cc05b6511966e5d3d96988b12b95f8",
            EncoderData: "onnx/vision_encoder.onnx_data", EncoderDataBytes: 1_869_466_624,
            EncoderDataHash: "838e1f0b2d0394ed3bd3b3499775dd6676524e1dfc5a7371948a76dcb69e4dd3",
            DecoderData: "onnx/prompt_encoder_mask_decoder.onnx_data", DecoderDataBytes: 22_072_320,
            DecoderDataHash: "2d870726d484cb496760fd139c21f115cf1b945c6b69583489faa2ac79f1d2ae") });

    /// <summary>维护者验证过的版本目录；同一模型按旧到新排列，历史版本保留用于回退。</summary>
    public static IReadOnlyList<SamModelDefinition> CompatibleVersions { get; } = All;

    /// <summary>拒绝不支持的模型标识。</summary>
    public static SamModelDefinition Get(SamModelKind kind)
        => All.FirstOrDefault(m => m.Kind == kind) ?? throw new ArgumentOutOfRangeException(nameof(kind));
}
