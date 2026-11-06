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
    assert report['quantile'] == .95 and report['previousImageThreshold'] == np.float32(.05)
    assert report['imageThreshold'] >= 120 / 255
    assert report['pixelThreshold'] >= 120 / 255
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
