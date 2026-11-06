"""使用实际 Lightning 回调验证内嵌流水线的早停和最佳权重恢复。"""
import importlib.util
import json
from pathlib import Path
import sys
import textwrap
from types import SimpleNamespace

import pytest
import torch
from lightning.pytorch import LightningDataModule, LightningModule, Trainer
from torch.utils.data import DataLoader, TensorDataset
from anomalib.data import ImageBatch, InferenceBatch
from anomalib.metrics import Evaluator
from anomalib import LearningType


def load_pipeline(tmp_path):
    """直接载入产品生成的同一份脚本，不复制其实现。"""
    source_path = Path(__file__).resolve().parents[2] / 'Snet.Yolo.Tasks.Core/Anomalib/AnomalibPythonPipeline.cs'
    source = textwrap.dedent(source_path.read_text(encoding='utf-8-sig').split('private const string Script = """"', 1)[1].split('"""";', 1)[0]).lstrip('\n')
    path = tmp_path / 'pipeline.py'
    path.write_text(source, encoding='utf-8')
    spec = importlib.util.spec_from_file_location('earlystop_product_pipeline', path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


@pytest.fixture
def pipeline(tmp_path):
    """为每个测试提供独立的产品脚本模块。"""
    return load_pipeline(tmp_path)


def test_pipeline_defines_training_controls(pipeline):
    """修改前应失败，防止仅添加界面而未接入实际训练。"""
    assert callable(getattr(pipeline, 'training_callbacks', None))
    assert callable(getattr(pipeline, 'restore_best_weights', None))


def test_non_iterative_models_have_no_early_stopping(pipeline, tmp_path):
    """PaDiM、PatchCore 继续使用其单轮拟合流程。"""
    for name in ['padim', 'patchcore']:
        callbacks, checkpoint, stopping = pipeline.training_callbacks({'model': name}, None, tmp_path)
        assert callbacks == [] and checkpoint is None and stopping is None


def validation_batch(samples):
    """生成同时含正常和异常像素的可分布式验证批次。"""
    return ImageBatch(image=torch.zeros(1, 3, 2, 2), gt_mask=torch.tensor([[[0, 0], [1, 1]]]), gt_label=torch.tensor([1]))


class PlateauModel(LightningModule):
    """可控验证质量：第 2 轮最佳，后续退化；权重记录实际轮次。"""
    def __init__(self):
        super().__init__()
        self.marker = torch.nn.Parameter(torch.tensor(0.0))
        self.evaluator = Evaluator()

    def on_train_epoch_start(self):
        self.marker.data.fill_(self.current_epoch + 1)

    def training_step(self, batch, batch_idx):
        return self.marker * 0

    def forward(self, images):
        anomaly_map = images[:, 0] * 0 + self.marker * 0.01
        score = anomaly_map.amax(dim=(1, 2))
        return InferenceBatch(pred_score=score, anomaly_map=anomaly_map, pred_label=score > 0.5, pred_mask=anomaly_map > 0.5)

    def to_onnx(self, export_root, model_file_name='model', input_size=(128, 128), **kwargs):
        """使用真实 PyTorch ONNX 导出器，不用虚构导出结果替代一致性检查。"""
        output = Path(export_root) / (model_file_name + '.onnx')
        torch.onnx.export(self, torch.zeros(1, 3, *input_size), output, dynamo=False,
                          input_names=['image'], output_names=list(InferenceBatch._fields))
        return output

    def validation_step(self, batch, batch_idx):
        values = [0.1, 0.2, 0.8, 0.9] if self.current_epoch == 1 else [0.1, 0.4, 0.3, 0.8]
        batch.anomaly_map = torch.tensor(values, device=self.device).reshape(1, 2, 2)
        return batch

    def configure_optimizers(self):
        return torch.optim.SGD(self.parameters(), lr=0)

    def configure_callbacks(self):
        return [self.evaluator]

    @property
    def learning_type(self):
        return LearningType.ONE_CLASS

    @property
    def trainer_arguments(self):
        return {'num_sanity_val_steps': 0}

    @property
    def name(self):
        return 'EarlyStoppingSmoke'


def run_plateau(pipeline, tmp_path, *, enabled=True, patience=2, delta=0.001, epochs=8):
    """使用真实 Trainer、Anomalib Evaluator 和产品回调完成小规模 CPU 训练。"""
    torch.set_num_threads(1)
    model = PlateauModel()
    callbacks, checkpoint, stopping = pipeline.training_callbacks({
        'model': 'efficient_ad_small', 'earlyStoppingEnabled': enabled,
        'earlyStoppingPatience': patience, 'earlyStoppingMinDelta': delta,
    }, model, tmp_path)
    trainer = Trainer(max_epochs=epochs, accelerator='cpu', devices=1, logger=False,
                      callbacks=callbacks, enable_progress_bar=False, enable_model_summary=False,
                      num_sanity_val_steps=0, default_root_dir=tmp_path)
    loader = DataLoader(TensorDataset(torch.zeros(1, 1)), batch_size=1)
    validation = DataLoader(TensorDataset(torch.zeros(1, 1)), batch_size=1, collate_fn=validation_batch)
    trainer.fit(model, loader, validation)
    return model, trainer, checkpoint, stopping


def test_plateau_stops_and_restores_best_not_last_weights(pipeline, tmp_path):
    """达到耐心上限就停止，并恢复最佳第 2 轮而非最后第 4 轮。"""
    model, trainer, checkpoint, stopping = run_plateau(pipeline, tmp_path)
    assert trainer.current_epoch == 4 < trainer.max_epochs
    assert stopping.stopped_epoch == 3
    assert checkpoint.best_model_score.item() == pytest.approx(1)
    assert model.marker.item() == 4
    epoch = pipeline.restore_best_weights(model, Path(checkpoint.best_model_path), tmp_path)
    assert epoch == 2 and model.marker.item() == 2 and not model.training


def test_disabled_early_stopping_runs_to_limit_but_keeps_best(pipeline, tmp_path):
    """关闭早停仍监控并保存最佳模型，但训练运行到上限。"""
    model, trainer, checkpoint, stopping = run_plateau(pipeline, tmp_path, enabled=False, epochs=6)
    assert stopping is None and trainer.current_epoch == 6
    assert model.marker.item() == 6
    assert pipeline.restore_best_weights(model, Path(checkpoint.best_model_path), tmp_path) == 2
    assert model.marker.item() == 2


def test_minimum_improvement_is_applied(pipeline, tmp_path):
    """指标上升但未超过指定增量时，仍计入无明显改善。"""
    model, trainer, checkpoint, stopping = run_plateau(pipeline, tmp_path, patience=1, delta=0.3)
    assert trainer.current_epoch == 2 and stopping.stopped_epoch == 1
    assert checkpoint.best_model_score.item() == pytest.approx(1)


def test_epoch_limit_can_finish_before_patience(pipeline, tmp_path):
    """最大轮数先到达时，正常结束并保留检查点。"""
    _, trainer, checkpoint, stopping = run_plateau(pipeline, tmp_path, patience=50, epochs=2)
    assert trainer.current_epoch == 2 and stopping.stopped_epoch == 0
    assert Path(checkpoint.best_model_path).is_file()


def test_missing_metric_fails_instead_of_silent_long_training(pipeline, tmp_path):
    """没有验证指标时严格失败，不静默运行到 10000 轮。"""
    model = PlateauModel()
    callbacks, _, _ = pipeline.training_callbacks({'model': 'efficient_ad_small'}, model, tmp_path)
    stopping = next(callback for callback in callbacks if hasattr(callback, 'patience'))
    trainer = SimpleNamespace(callback_metrics={}, fast_dev_run=False)
    with pytest.raises(RuntimeError, match='not available'):
        stopping._run_early_stopping_check(trainer)


@pytest.mark.parametrize('value', [float('nan'), float('inf'), float('-inf')])
def test_non_finite_metric_stops(pipeline, tmp_path, value):
    """非有限指标触发 Lightning 的异常停止，而非继续浪费训练资源。"""
    callbacks, _, _ = pipeline.training_callbacks({'model': 'efficient_ad_small'}, PlateauModel(), tmp_path)
    stopping = next(callback for callback in callbacks if hasattr(callback, 'patience'))
    should_stop, reason = stopping._evaluate_stopping_criteria(torch.tensor(value))
    assert should_stop and 'not finite' in reason


def test_untrusted_checkpoint_is_rejected_before_loading(pipeline, tmp_path, monkeypatch):
    """当前训练目录之外的文件不能进入 PyTorch 反序列化。"""
    outside = tmp_path / 'outside.ckpt'
    outside.write_bytes(b'not a checkpoint')
    def forbidden(*args, **kwargs):
        pytest.fail('must reject path before torch.load')
    monkeypatch.setattr(torch, 'load', forbidden)
    with pytest.raises(RuntimeError, match='拒绝加载'):
        pipeline.restore_best_weights(PlateauModel(), outside, tmp_path / 'artifacts')


class TinyDataModule(LightningDataModule):
    """替换大数据集的轻量输入，训练引擎和回调仍使用真实实现。"""
    name = 'earlystop'
    category = ''

    def __init__(self, **kwargs):
        super().__init__()

    def train_dataloader(self):
        return DataLoader(TensorDataset(torch.zeros(1, 1)), batch_size=1)

    def val_dataloader(self):
        return DataLoader(TensorDataset(torch.zeros(1, 1)), batch_size=1, collate_fn=validation_batch)


def pipeline_config(tmp_path):
    """创建包含 5 张独立正常校准图片的实际流水线配置。"""
    from PIL import Image
    calibration = tmp_path / 'dataset/calibration/good'
    calibration.mkdir(parents=True)
    (tmp_path / 'dataset/train/good').mkdir(parents=True)
    for i in range(5):
        Image.new('RGB', (4, 4), (i, i, i)).save(calibration / f'{i}.png')
    return {
        'model': 'efficient_ad_small', 'imageSize': 128, 'maxEpochs': 8, 'device': 'cpu',
        'workerCount': 0, 'randomSeed': 42, 'earlyStoppingEnabled': True,
        'earlyStoppingPatience': 2, 'earlyStoppingMinDelta': 0.001,
        'datasetRoot': str(tmp_path / 'dataset'), 'artifactRoot': str(tmp_path / 'artifacts'),
        'maximumNormalFalsePositiveRate': 0.05, 'normalImageCount': 10, 'calibrationImageCount': 5,
        'datasetSha256': '0' * 64,
    }


def invoke_main(pipeline, tmp_path, monkeypatch, config):
    """执行产品入口；保留训练、最佳模型加载、ONNX 导出及门禁的真实控制流。"""
    # 此受控模型不含原始后处理接口；真实正常阈值逻辑由独立 PostProcessor 回归及服务器模型验证覆盖。
    monkeypatch.setattr(pipeline, 'calibrate_normal_thresholds', lambda *args: {
        'completed': True, 'sampleCount': 3, 'quantile': .95, 'previousImageThreshold': .05,
        'previousPixelThreshold': .05, 'imageThreshold': .4, 'pixelThreshold': .4,
        'samples': [{'fileName': f'internal-{i}.png', 'rawImageScore': .4, 'rawPixelMaximum': .4} for i in range(3)], 'errors': [],
    })
    config_path = tmp_path / 'config.json'
    result_path = tmp_path / 'result.json'
    config_path.write_text(json.dumps(config), encoding='utf-8')
    monkeypatch.setattr(sys, 'argv', ['pipeline', '--config', str(config_path), '--result', str(result_path)])
    code = pipeline.main()
    return code, json.loads(result_path.read_text(encoding='utf-8'))


def test_full_pipeline_exports_restored_best_weights_and_passes_parity(pipeline, tmp_path, monkeypatch, capsys):
    """真实 Engine→最佳检查点→ONNX Runtime→门禁，证明导出的不是最后一轮。"""
    import anomalib.data
    import onnxruntime as ort
    assert ort.__version__ == '1.31.1'
    model = PlateauModel()
    monkeypatch.setattr(pipeline, 'create_model', lambda *args: model)
    monkeypatch.setattr(anomalib.data, 'Folder', TinyDataModule)
    code, result = invoke_main(pipeline, tmp_path, monkeypatch, pipeline_config(tmp_path))
    assert code == 0 and result['status'] == 'passed' and result['sampleCount'] == 5
    assert result['normalFalsePositiveCount'] == 0 and result['maxScoreDifference'] < 1e-6
    assert model.marker.item() == 2
    checkpoint = torch.load(tmp_path / 'artifacts/model.ckpt', map_location='cpu', weights_only=False)
    assert checkpoint['epoch'] == 1 and checkpoint['state_dict']['marker'].item() == 2
    assert (tmp_path / 'artifacts/model.manifest.json').is_file()
    assert '提前停止' in capsys.readouterr().out


@pytest.mark.parametrize('value', [None, float('nan'), float('inf')])
def test_invalid_final_metric_never_exports_or_registers(pipeline, tmp_path, monkeypatch, value):
    """即使已有历史最佳检查点，最后指标异常也必须关闭导出与注册门禁。"""
    import anomalib.engine
    class InvalidMetricEngine:
        def __init__(self, **kwargs):
            self.callbacks = kwargs['callbacks']
            self.trainer = SimpleNamespace(is_global_zero=True, world_size=1, current_epoch=1,
                                           callback_metrics={pipeline.EARLY_STOP_MONITOR: value})
        def fit(self, **kwargs):
            checkpoint = self.callbacks[0]
            path = tmp_path / 'artifacts/best-checkpoints/invalid.ckpt'
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b'never deserialize this file')
            checkpoint.best_model_path = str(path)
            checkpoint.best_model_score = torch.tensor(0.75)
        def export(self, **kwargs):
            pytest.fail('invalid validation metrics must not reach export')
    monkeypatch.setattr(anomalib.engine, 'Engine', InvalidMetricEngine)
    monkeypatch.setattr(pipeline, 'create_model', lambda *args: PlateauModel())
    code, result = invoke_main(pipeline, tmp_path, monkeypatch, pipeline_config(tmp_path))
    assert code == 2 and result['status'] == 'failed'
    assert '验证指标缺失或不是有限值' in result['errors'][0]
    assert not (tmp_path / 'artifacts/model.manifest.json').exists()


def test_real_efficientad_receives_bounded_validation_metric(pipeline, tmp_path):
    """实际 EfficientAD Small 不下载教师权重，也能正确接入验证指标和回调。"""
    model = pipeline.create_model('efficient_ad_small', 128)
    callbacks, checkpoint, stopping = pipeline.training_callbacks({'model': 'efficient_ad_small'}, model, tmp_path)
    assert len(model.evaluator.val_metrics) == 1
    metric = model.evaluator.val_metrics[0]
    assert metric.name == pipeline.EARLY_STOP_MONITOR and len(metric.thresholds) == 256
    assert checkpoint.monitor == stopping.monitor == metric.name
    assert stopping.patience == 50 and stopping.min_delta == 0.001
    # 实际 EfficientAD 的检查点包含预处理模块超参数，验证其可信本地恢复路径。
    path = tmp_path / 'efficientad.ckpt'
    parameter = next(model.model.student.parameters())
    expected = parameter.detach().clone()
    torch.save({'epoch': 3, 'state_dict': model.state_dict(), 'hyper_parameters': dict(model.hparams)}, path)
    with torch.no_grad():
        parameter.add_(1)
    assert pipeline.restore_best_weights(model, path, tmp_path) == 4
    assert torch.equal(parameter, expected)
