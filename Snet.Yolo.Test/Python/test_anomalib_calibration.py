"""校准逐图诊断与未通过时保留可手动检查的导出模型。"""
from pathlib import Path
from types import SimpleNamespace
import json

import numpy as np
import pytest
import torch
from PIL import Image
from anomalib.data import InferenceBatch

from test_anomalib_early_stopping import (
    PlateauModel, TinyDataModule, invoke_main, load_pipeline, pipeline_config,
)


class CalibrationModel(torch.nn.Module):
    """第一张正常图误报，后两张正常，用于逐图继续处理的测试。"""
    def forward(self, images):
        score = torch.where(images[:, 0, 0, 0] < 0.01, 0.8, 0.1)
        mask = torch.zeros((1, 4, 4), dtype=torch.bool)
        return InferenceBatch(pred_score=score, anomaly_map=mask.float(), pred_label=score > 0.5, pred_mask=mask)


def test_sample_failures_are_logged_and_remaining_images_are_checked(tmp_path, monkeypatch, capsys):
    """第 1 张误报、第 2 张推理错误后，仍完整校准第 3 张并记录原始分数。"""
    import onnxruntime
    pipeline = load_pipeline(tmp_path)
    samples = []
    for i in range(3):
        path = tmp_path / f'{i}.png'
        Image.new('RGB', (4, 4), (i * 32, 0, 0)).save(path)
        samples.append(path)

    class Session:
        def get_inputs(self):
            return [SimpleNamespace(name='image', type='tensor(float)')]
        def get_outputs(self):
            return [SimpleNamespace(name=name) for name in ['pred_score', 'pred_label', 'pred_mask']]
        def run(self, names, inputs):
            index = round(float(inputs['image'][0, 0, 0, 0]) * 255 / 32)
            if index == 1:
                raise RuntimeError('controlled sample inference failure')
            score = 0.8 if index == 0 else 0.1
            return [np.array([score], np.float32), np.array([score > 0.5]), np.zeros((1, 4, 4), dtype=bool)]

    monkeypatch.setattr(onnxruntime, 'InferenceSession', lambda *args, **kwargs: Session())
    result = pipeline.parity(CalibrationModel(), Path('unused.onnx'), samples, 4, 0.05, {'0.png': 'original-upload.png'})
    assert result['status'] == 'failed' and result['totalSampleCount'] == 3 and result['sampleCount'] == 2
    assert result['normalFalsePositiveCount'] == 1
    rows = result['sampleResults']
    assert [row['index'] for row in rows] == [1, 2, 3]
    assert not rows[0]['passed'] and rows[0]['onnxScore'] == pytest.approx(0.8)
    assert rows[0]['fileName'] == 'original-upload.png'
    assert 'controlled sample inference failure' in rows[1]['message']
    assert rows[2]['passed'] and rows[2]['fileName'] == '2.png'
    output = capsys.readouterr().out
    assert '校准[1/3]' in output and '校准[2/3]' in output and '校准[3/3]' in output
    json.dumps(result, allow_nan=False)


@pytest.mark.parametrize('score', [float('nan'), float('inf'), float('-inf')])
def test_non_finite_scores_are_reported_as_sample_errors(tmp_path, monkeypatch, score):
    """异常数值不能被误计为通过，也不能产生 C# 无法读取的非标准 JSON。"""
    import onnxruntime
    pipeline = load_pipeline(tmp_path)
    path = tmp_path / 'sample.png'
    Image.new('RGB', (4, 4)).save(path)

    class Session:
        def get_inputs(self):
            return [SimpleNamespace(name='image', type='tensor(float)')]
        def get_outputs(self):
            return [SimpleNamespace(name=name) for name in ['pred_score', 'pred_label', 'pred_mask']]
        def run(self, names, inputs):
            return [np.array([score]), np.array([False]), np.zeros((1, 4, 4), dtype=bool)]

    monkeypatch.setattr(onnxruntime, 'InferenceSession', lambda *args, **kwargs: Session())
    result = pipeline.parity(CalibrationModel(), Path('unused.onnx'), [path], 4, 0.05)
    assert result['status'] == 'failed' and result['sampleCount'] == 0
    assert not result['sampleResults'][0]['passed'] and result['sampleResults'][0]['onnxScore'] is None
    assert '非有限' in result['sampleResults'][0]['message']
    json.dumps(result, allow_nan=False)


class AllAnomalousModel(PlateauModel):
    """生成真实可导出的模型，使 5 张正常校准图全部误报。"""
    def forward(self, images):
        anomaly_map = images[:, 0] * 0 + self.marker * 0.4
        score = anomaly_map.amax(dim=(1, 2))
        return InferenceBatch(pred_score=score, anomaly_map=anomaly_map, pred_label=score > 0.5, pred_mask=anomaly_map > 0.5)


def test_failed_calibration_preserves_real_onnx_and_manifest(tmp_path, monkeypatch):
    """质量未通过不能丢弃已成功导出的模型，报告仍如实记为 failed。"""
    import anomalib.data
    pipeline = load_pipeline(tmp_path)
    model = AllAnomalousModel()
    monkeypatch.setattr(pipeline, 'create_model', lambda *args: model)
    monkeypatch.setattr(anomalib.data, 'Folder', TinyDataModule)
    code, result = invoke_main(pipeline, tmp_path, monkeypatch, pipeline_config(tmp_path))
    assert code == 0 and result['exportSucceeded'] and result['status'] == 'failed'
    assert result['normalFalsePositiveCount'] == 5 and result['totalSampleCount'] == 5
    assert len(result['sampleResults']) == 5 and not any(row['passed'] for row in result['sampleResults'])
    assert (tmp_path / 'artifacts/model.onnx').is_file()
    assert (tmp_path / 'artifacts/model.manifest.json').is_file()


def test_corrupt_calibration_image_does_not_block_exported_package(tmp_path, monkeypatch):
    """单张校准图损坏时继续余下图片，保留完整导出和逐图错误。"""
    import anomalib.data
    pipeline = load_pipeline(tmp_path)
    monkeypatch.setattr(pipeline, 'create_model', lambda *args: PlateauModel())
    monkeypatch.setattr(anomalib.data, 'Folder', TinyDataModule)
    config = pipeline_config(tmp_path)
    (tmp_path / 'dataset/calibration/good/0.png').write_bytes(b'corrupt image')
    code, result = invoke_main(pipeline, tmp_path, monkeypatch, config)
    assert code == 0 and result['exportSucceeded'] and result['status'] == 'failed'
    assert result['sampleCount'] == 4 and result['totalSampleCount'] == 5
    assert len(result['sampleResults']) == 5 and result['sampleResults'][-1]['passed']
    assert (tmp_path / 'artifacts/model.manifest.json').is_file()
