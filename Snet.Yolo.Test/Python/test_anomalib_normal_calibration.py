"""正常阈值必须用训练内部留出的正常图片标定，不能拿最终验收图片调到通过。"""
from pathlib import Path
from types import SimpleNamespace
import json
import hashlib

import numpy as np
import pandas as pd
import torch
from PIL import Image
from anomalib.data import InferenceBatch
from anomalib.post_processing import PostProcessor

from test_anomalib_early_stopping import load_pipeline, pipeline_config


class RawBrightness(torch.nn.Module):
    """受控的原始异常分数，便于检验阈值变化而非真实缺陷准确率。"""
    def forward(self, image):
        anomaly_map = image[:, 0]
        return InferenceBatch(pred_score=anomaly_map.amax(dim=(1, 2)), anomaly_map=anomaly_map)


class CalibrationModel(torch.nn.Module):
    """使用真实 Anomalib 后处理器的受控模型。"""
    def __init__(self):
        super().__init__()
        self.marker = torch.nn.Parameter(torch.tensor(0.))
        self.pre_processor = torch.nn.Identity()
        self.model = RawBrightness()
        self.post_processor = PostProcessor()
        self.post_processor._image_threshold.fill_(.05)
        self.post_processor._pixel_threshold.fill_(.05)

    @property
    def device(self):
        return self.marker.device

    def forward(self, image):
        return self.post_processor(self.model(self.pre_processor(image)))


def normal_data(tmp_path):
    """3 张内部正常图与训练图片路径保持分离。"""
    paths = []
    for i, value in enumerate([60, 90, 120]):
        path = tmp_path / f'normal-{i}.png'
        Image.new('RGB', (9, 11), (value, value, value)).save(path)
        paths.append(str(path))
    frame = pd.DataFrame({'image_path': paths, 'label_index': [0, 0, 0]})
    return SimpleNamespace(val_data=SimpleNamespace(source_samples=frame),
                           train_data=SimpleNamespace(samples=pd.DataFrame({'image_path': [str(tmp_path / 'training.png')]})))


def test_normal_calibration_corrects_synthetic_threshold_without_acceptance_leakage(tmp_path):
    """过低的合成异常阈值由独立正常数据标定；明显偏离正常值的样本仍判异常。"""
    pipeline = load_pipeline(tmp_path)
    assert callable(getattr(pipeline, 'calibrate_normal_thresholds', None))
    model = CalibrationModel()
    checkpoint = tmp_path / 'model.ckpt'
    torch.save({'epoch': 7, 'state_dict': model.state_dict()}, checkpoint)
    report = pipeline.calibrate_normal_thresholds(model, normal_data(tmp_path), 8, .05)
    assert report['completed'] and report['sampleCount'] == 3
    assert report['quantile'] == 1.0 and report['previousImageThreshold'] == np.float32(.05)
    # 阈值覆盖全部标定图并留出 5% 余量：最高分 120/255 的正常图不会越线。
    assert report['imageThreshold'] >= 120 / 255 * 1.05
    assert report['pixelThreshold'] >= 120 / 255 * 1.05
    assert abs(report['imageScoreMax'] - 120 / 255) < 1e-6 and 'max x 1.05' in report['thresholdPolicy']
    assert len(report['samples']) == 3 and not report['errors']
    pipeline.update_checkpoint_post_processor(model, checkpoint, tmp_path)
    saved = torch.load(checkpoint, weights_only=False)
    assert saved['epoch'] == 7 and saved['state_dict']['marker'] == 0
    assert saved['state_dict']['post_processor._image_threshold'].item() == report['imageThreshold']
    with torch.inference_mode():
        assert not bool(model(torch.full((1, 3, 8, 8), .4)).pred_label.item())
        assert bool(model(torch.ones((1, 3, 8, 8))).pred_label.item())


def test_normal_calibration_rejects_training_overlap_without_changing_thresholds(tmp_path):
    """不能用模型已见过的训练图把正常阈值标定得过低。"""
    pipeline = load_pipeline(tmp_path)
    assert callable(getattr(pipeline, 'calibrate_normal_thresholds', None))
    data = normal_data(tmp_path)
    data.train_data.samples = data.val_data.source_samples
    model = CalibrationModel()
    report = pipeline.calibrate_normal_thresholds(model, data, 8, .05)
    assert not report['completed'] and report['errors']
    assert model.post_processor._image_threshold.item() == np.float32(.05)


def test_normal_calibration_continues_after_one_corrupt_image(tmp_path):
    """单张内部正常图读取失败后继续处理，保留错误以阻止自动注册。"""
    pipeline = load_pipeline(tmp_path)
    data = normal_data(tmp_path)
    Path(data.val_data.source_samples.image_path.iloc[0]).write_bytes(b'broken')
    report = pipeline.calibrate_normal_thresholds(CalibrationModel(), data, 8, .05)
    assert report['completed'] and report['sampleCount'] == 2 and len(report['errors']) == 1
    assert report['samples'][-1]['fileName'] == 'normal-2.png'


def test_uninitialized_threshold_is_reported_as_null(tmp_path):
    """尚未初始化的旧阈值不能令完整报告产生非法的 NaN JSON。"""
    pipeline = load_pipeline(tmp_path)
    model = CalibrationModel()
    model.post_processor._image_threshold.fill_(float('nan'))
    model.post_processor._pixel_threshold.fill_(float('nan'))
    report = pipeline.calibrate_normal_thresholds(model, normal_data(tmp_path), 8, .05)
    assert report['completed'] and report['previousImageThreshold'] is None
    json.dumps(report, allow_nan=False)


def test_parity_input_matches_training_float_antialiased_resize(tmp_path):
    """校准不再先量化到 PIL 8 位缩图，保持训练的浮点抗锯齿缩放。"""
    from torchvision.transforms.v2 import functional as F
    pipeline = load_pipeline(tmp_path)
    pixels = ((np.arange(11 * 9 * 3).reshape(11, 9, 3) * 37) % 256).astype(np.uint8)
    path = tmp_path / 'pattern.png'
    Image.fromarray(pixels).save(path)
    raw = torch.from_numpy(pixels).permute(2, 0, 1).float().div(255).unsqueeze(0)
    expected = F.resize(raw, [4, 4], antialias=True).numpy()
    actual = pipeline.onnx_input(path, SimpleNamespace(type='tensor(float)'), 4)
    np.testing.assert_allclose(actual, expected, atol=1e-7)


def test_manifest_hashes_model_as_stream_without_full_file_allocation(tmp_path, monkeypatch):
    """GB 级导出不能为计算摘要再次分配整个 ONNX 的内存。"""
    pipeline = load_pipeline(tmp_path)
    config = pipeline_config(tmp_path)
    content = b'controlled-exported-model'
    model = tmp_path / 'model.onnx'
    model.write_bytes(content)
    def forbidden(*args, **kwargs):
        raise AssertionError('must not read the entire model into memory')
    monkeypatch.setattr(Path, 'read_bytes', forbidden)
    manifest = tmp_path / 'manifest.json'
    pipeline.write_manifest(config, model, manifest, 'image', ['pred_score', 'pred_label', 'anomaly_map', 'pred_mask'])
    assert json.loads(manifest.read_text())['modelSha256'] == hashlib.sha256(content).hexdigest()


def test_threshold_covers_all_calibration_images_with_margin(tmp_path):
    """阈值不低于最高分的 1.05 倍，也不低于均值加 3.5 倍标准差；单样本时只加余量。"""
    pipeline = load_pipeline(tmp_path)
    scores = [41.4, 47.0, 48.1, 52.0, 53.0, 44.0, 46.5]
    threshold = pipeline.fit_threshold(scores)
    assert threshold >= max(scores) * 1.05
    assert threshold >= np.mean(scores) + 3.5 * np.std(scores, ddof=1)
    assert pipeline.fit_threshold([10.0]) == 10.5
    # 分数集中时由 1.05 倍最高分主导，保证门禁里略高于最高分的新正常图仍然通过。
    assert pipeline.fit_threshold([100.0, 100.1, 99.9]) == 100.1 * 1.05


def test_threshold_rule_passes_normal_images_that_the_old_quantile_rule_rejected(tmp_path):
    """回归：原先取 95% 分位数，约 5% 的新正常图必然越线，零误报门禁因此频繁失败。"""
    pipeline = load_pipeline(tmp_path)
    rng = np.random.default_rng(7)
    failures = 0
    for _ in range(200):
        normals = rng.normal(47, 3.2, 42)
        internal, gate = normals[:27], normals[27:]
        failures += int((gate > pipeline.fit_threshold(internal)).any())
    assert failures <= 4, failures  # 15 张门禁图出现误报的概率应低于 2%


def test_patchcore_plan_scales_with_vram_budget(tmp_path):
    """显存充足时不抽样；嵌入超过可用显存 80% 时按比例抽样；记忆库保持在 2000 至 30000 之间。"""
    pipeline = load_pipeline(tmp_path)
    full = pipeline.plan_patchcore(640, 108, 15 << 30)
    assert full['keepFraction'] == 1.0 and full['memoryBank'] == 6912 and full['patches'] == 108 * 80 * 80
    big = pipeline.plan_patchcore(1024, 108, 15 << 30)
    assert 0.05 <= big['keepFraction'] < 1.0 and big['keptPatches'] <= big['patches']
    assert 2000 <= big['memoryBank'] <= 30000 and big['coresetRatio'] <= 1.0
    tiny = pipeline.plan_patchcore(128, 10, 15 << 30)
    assert tiny['memoryBank'] == min(2000, tiny['keptPatches'])
    halved = pipeline.plan_patchcore(640, 108, 15 << 30, keep_scale=0.5)
    assert halved['keepFraction'] == 0.5
    assert pipeline.plan_patchcore(640, 108, None)['keepFraction'] == 1.0


def test_thinning_store_keeps_fraction_of_each_embedding(tmp_path):
    """训练嵌入入库前随机抽样固定比例，且可复现。"""
    pipeline = load_pipeline(tmp_path)
    first, second = pipeline.ThinningStore(0.25, 3), pipeline.ThinningStore(0.25, 3)
    for store in (first, second):
        store.append(torch.arange(400.).reshape(100, 4))
    assert first[0].shape == (25, 4)
    assert torch.equal(first[0], second[0])
    keep_all = pipeline.ThinningStore(1.0, 3)
    keep_all.append(torch.zeros(10, 4))
    assert keep_all[0].shape == (10, 4)


def test_emit_writes_marker_prefixed_json_line(tmp_path, capsys):
    """结构化进度行必须以固定前缀开头、单行 JSON，宿主据此刷新实时指标。"""
    pipeline = load_pipeline(tmp_path)
    pipeline.emit('epoch', epoch=3, trainLoss=1.5)
    line = capsys.readouterr().out.strip()
    assert line.startswith('VISUALIDENTITY_JSON:')
    assert json.loads(line[len('VISUALIDENTITY_JSON:'):]) == {'type': 'epoch', 'epoch': 3, 'trainLoss': 1.5}
    assert pipeline.finite(float('nan')) is None and pipeline.finite(float('inf'), 0) == 0 and pipeline.finite(torch.tensor(2.5)) == 2.5


def test_working_dataset_is_resized_png_in_sibling_directory(tmp_path):
    """训练前把原图一次性缩小为训练尺寸的无损 PNG；原图保持不变用于阈值标定与门禁。"""
    pipeline = load_pipeline(tmp_path)
    good = tmp_path / 'dataset' / 'train' / 'good'
    good.mkdir(parents=True)
    for index in range(3):
        Image.new('RGB', (40, 30), (index * 50, 10, 200)).save(good / f'{index}.bmp')
    working = pipeline.prepare_working_dataset(tmp_path / 'dataset', 16, workers=1)
    produced = sorted((working / 'train' / 'good').iterdir())
    assert [path.name for path in produced] == ['0.png', '1.png', '2.png']
    assert all(Image.open(path).size == (16, 16) for path in produced)
    assert len(list(good.iterdir())) == 3


def fit_config(device='cuda:0'):
    """fit_with_memory_retry 需要的最小配置。"""
    return {'model': 'patchcore', 'imageSize': 640, 'randomSeed': 42, 'maxEpochs': 1, 'device': device}


class OutOfMemoryOnceEngine:
    """第一次 fit 抛出显存不足，之后成功；calls 记录累计调用次数。"""
    calls = 0

    def __init__(self, **kwargs):
        self.kwargs = kwargs

    def fit(self, **kwargs):
        type(self).calls += 1
        if type(self).calls == 1:
            raise torch.OutOfMemoryError('CUDA out of memory. Tried to allocate 20.00 GiB')


def test_patchcore_oom_retry_halves_sampling_and_releases_memory_outside_the_except_block(tmp_path, monkeypatch):
    """显存不足后抽样比例减半重试；释放显存必须发生在 except 块之外，否则块内的回溯仍引用着模型，显存释放不掉。"""
    import sys
    import anomalib.engine
    pipeline = load_pipeline(tmp_path)
    OutOfMemoryOnceEngine.calls = 0
    monkeypatch.setattr(anomalib.engine, 'Engine', OutOfMemoryOnceEngine)
    monkeypatch.setattr(pipeline, 'create_model', lambda *args: object())
    monkeypatch.setattr(pipeline, 'free_vram_bytes', lambda: 16 << 30)
    scales, handling = [], []
    original_plan = pipeline.plan_patchcore
    monkeypatch.setattr(pipeline, 'plan_patchcore', lambda *args: (scales.append(args[3]), original_plan(*args))[1])
    monkeypatch.setattr(pipeline, 'release_gpu_memory', lambda: handling.append(sys.exc_info()[0]))
    pipeline.fit_with_memory_retry(fit_config(), None, tmp_path, None, 135, [0], [], {})
    assert OutOfMemoryOnceEngine.calls == 2 and scales == [1.0, 0.5]
    assert handling == [None]  # 调用释放时已经不在异常处理之中


def test_cpu_training_does_not_plan_against_gpu_memory(tmp_path, monkeypatch):
    """CPU 训练的嵌入放在内存里：不能按 GPU 的剩余显存去抽样 patch，也不应读取显存。"""
    import anomalib.engine
    pipeline = load_pipeline(tmp_path)
    OutOfMemoryOnceEngine.calls = 1  # 下一次 fit 直接成功
    monkeypatch.setattr(anomalib.engine, 'Engine', OutOfMemoryOnceEngine)
    monkeypatch.setattr(pipeline, 'create_model', lambda *args: object())

    def forbidden():
        raise AssertionError('CPU 训练不应读取 GPU 显存')

    monkeypatch.setattr(pipeline, 'free_vram_bytes', forbidden)
    run_info = {}
    pipeline.fit_with_memory_retry(fit_config('cpu'), None, tmp_path, None, 135, [], [], run_info)
    assert run_info['patchcorePlan']['keepFraction'] == 1.0 and run_info['patchcorePlan']['freeVramMiB'] is None
