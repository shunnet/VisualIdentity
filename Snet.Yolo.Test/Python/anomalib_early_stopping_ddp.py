"""两个真实 CPU/Gloo 进程验证与产品相同的 DDP 提前停止及最佳权重恢复。"""
import argparse
import json
import os
from pathlib import Path

import torch
from anomalib.engine import Engine
from torch.utils.data import DataLoader, TensorDataset

from test_anomalib_early_stopping import PlateauModel, load_pipeline, validation_batch


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--artifacts', type=Path, required=True)
    args = parser.parse_args()
    root = args.artifacts.resolve()
    rank_root = root / ('rank-' + os.environ.get('LOCAL_RANK', '0'))
    rank_root.mkdir(parents=True, exist_ok=True)
    pipeline = load_pipeline(rank_root)
    torch.set_num_threads(1)
    model = PlateauModel()
    callbacks, checkpoint, stopping = pipeline.training_callbacks({
        'model': 'efficient_ad_small', 'earlyStoppingPatience': 2, 'earlyStoppingMinDelta': 0.001,
    }, model, root)
    engine = Engine(default_root_dir=root / 'runs', callbacks=callbacks, accelerator='cpu', devices=2,
                    strategy='ddp', max_epochs=8, logger=False, enable_progress_bar=False, enable_model_summary=False)
    loader = DataLoader(TensorDataset(torch.zeros(2, 1)), batch_size=1)
    validation = DataLoader(TensorDataset(torch.zeros(2, 1)), batch_size=1, collate_fn=validation_batch)
    engine.fit(model=model, train_dataloaders=loader, val_dataloaders=validation)
    trainer = engine.trainer
    assert trainer.world_size == 2 and trainer.current_epoch == 4 and stopping.stopped_epoch == 3
    assert Path(checkpoint.best_model_path).is_file()
    if trainer.is_global_zero:
        best_epoch = pipeline.restore_best_weights(model, Path(checkpoint.best_model_path), root)
        assert best_epoch == 2 and model.marker.item() == 2
        (root / 'ddp-result.json').write_text(json.dumps({
            'worldSize': trainer.world_size, 'epochs': trainer.current_epoch,
            'stoppedEpoch': stopping.stopped_epoch, 'bestEpoch': best_epoch,
            'bestScore': checkpoint.best_model_score.item(), 'restoredWeight': model.marker.item(),
        }), encoding='utf-8')


if __name__ == '__main__':
    main()
