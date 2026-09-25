namespace Snet.Yolo.Tasks.Core.Anomalib;

using Snet.Yolo.Server.Anomalib;

using System.Text;

/// <summary>受应用版本控制的 Anomalib 2.6.2 训练、导出与一致性门禁脚本。</summary>
public static class AnomalibPythonPipeline
{
    /// <summary>脚本文件名。</summary>
    public const string FileName = "anomalib_pipeline.py";

    /// <summary>Python 脚本正文；动态路径只通过 JSON 配置传入，不插入脚本。</summary>
    public static string Source => Script;

    /// <summary>把内置脚本以 UTF-8 无 BOM 原子写入指定运行时目录。</summary>
    /// <param name="runtimeDirectory">可信应用运行时目录。</param>
    /// <returns>脚本绝对路径。</returns>
    public static string Materialize(string runtimeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeDirectory);
        Directory.CreateDirectory(runtimeDirectory);
        var path = Path.Combine(Path.GetFullPath(runtimeDirectory), FileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, Script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    private const string Script = """"
        # VisualIdentity 管理的脚本；要求 anomalib==2.6.2。
        from __future__ import annotations

        import argparse
        import hashlib
        import json
        import os
        import shutil
        import sys
        from pathlib import Path

        import numpy as np
        from PIL import Image


        def atomic_json(path: Path, value: dict) -> None:
            """原子写入 JSON，避免宿主读取到不完整的门禁结果。"""
            path.parent.mkdir(parents=True, exist_ok=True)
            temporary = path.with_suffix(path.suffix + ".tmp")
            temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
            os.replace(temporary, path)


        def create_model(name: str, image_size: int):
            """仅创建 .NET 允许列表中的模型。"""
            from anomalib.models import EfficientAd, Padim, Patchcore
            if name == "padim":
                return Padim(backbone="resnet18", pre_processor=Padim.configure_pre_processor(image_size=(image_size, image_size)))
            if name == "efficient_ad_small":
                return EfficientAd(pre_processor=EfficientAd.configure_pre_processor(image_size=(image_size, image_size)))
            if name == "patchcore":
                return Patchcore(backbone="wide_resnet50_2", pre_processor=Patchcore.configure_pre_processor(image_size=(image_size, image_size)))
            raise ValueError(f"Unsupported model: {name}")


        def as_numpy(value):
            """将张量转为数组，不保留 GPU 计算图。"""
            if value is None:
                return None
            if hasattr(value, "detach"):
                value = value.detach().cpu().numpy()
            return np.asarray(value)


        def scalar(value, default=0.0) -> float:
            """读取模型输出中的首个标量。"""
            array = as_numpy(value)
            return default if array is None or array.size == 0 else float(array.reshape(-1)[0])


        def mask_iou(left, right) -> float:
            """计算二值掩码交并比，两个空掩码视为完全一致。"""
            left_mask = as_numpy(left).astype(bool).squeeze()
            right_mask = as_numpy(right).astype(bool).squeeze()
            intersection = np.logical_and(left_mask, right_mask).sum()
            union = np.logical_or(left_mask, right_mask).sum()
            return 1.0 if union == 0 else float(intersection / union)


        def find_output(outputs: dict, *candidates: str):
            """按语义节点名绑定输出，避免依赖节点顺序。"""
            lowered = {key.lower(): value for key, value in outputs.items()}
            for candidate in candidates:
                if candidate in lowered:
                    return lowered[candidate]
            for key, value in lowered.items():
                if any(candidate in key for candidate in candidates):
                    return value
            return None


        def onnx_input(image_path: Path, session_input, image_size: int):
            """为含内嵌后处理的导出模型创建 RGB NCHW 输入张量。"""
            with Image.open(image_path) as image:
                rgb = image.convert("RGB").resize((image_size, image_size), Image.Resampling.BILINEAR)
                array = np.asarray(rgb)
            if "uint8" in session_input.type:
                return np.transpose(array, (2, 0, 1))[None, ...]
            return np.transpose(array.astype(np.float32) / 255.0, (2, 0, 1))[None, ...]


        def parity(model, onnx_path: Path, samples: list[Path], image_size: int, maximum_false_positive_rate: float) -> dict:
            """在 CPU 上比较本次训练的内存模型与 ONNX，避免反序列化模型文件。"""
            import onnxruntime as ort
            import torch

            session = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
            session_input = session.get_inputs()[0]
            output_names = [output.name for output in session.get_outputs()]
            model = model.eval().to("cpu")
            maximum_score_difference = 0.0
            minimum_mask_iou = 1.0
            label_mismatches = 0
            normal_false_positives = 0
            errors: list[str] = []
            completed = 0
            for sample in samples:
                try:
                    input_array = np.ascontiguousarray(onnx_input(sample, session_input, image_size))
                    with torch.inference_mode():
                        torch_result = model(torch.from_numpy(input_array))
                    raw_outputs = session.run(output_names, {session_input.name: input_array})
                    onnx_outputs = dict(zip(output_names, raw_outputs, strict=True))
                    onnx_score = find_output(onnx_outputs, "pred_score", "prediction_score")
                    onnx_label = find_output(onnx_outputs, "pred_label", "prediction_label")
                    onnx_mask = find_output(onnx_outputs, "pred_mask", "prediction_mask")
                    if onnx_score is None or onnx_label is None or onnx_mask is None:
                        raise RuntimeError("ONNX is missing pred_score, pred_label, or pred_mask")
                    score_difference = abs(scalar(torch_result.pred_score) - scalar(onnx_score))
                    maximum_score_difference = max(maximum_score_difference, score_difference)
                    if bool(scalar(torch_result.pred_label)) != bool(scalar(onnx_label)):
                        label_mismatches += 1
                    if bool(scalar(onnx_label)):
                        normal_false_positives += 1
                    minimum_mask_iou = min(minimum_mask_iou, mask_iou(torch_result.pred_mask, onnx_mask))
                    completed += 1
                except Exception as error:  # 将每个样本的失败都写入门禁报告。
                    errors.append(f"{sample.name}: {error}")
                print(f"VISUALIDENTITY_PARITY:{completed + len(errors)}/{len(samples)}", flush=True)
            passed = completed == len(samples) and completed > 0 and not errors and label_mismatches == 0 \
                and maximum_score_difference <= 0.02 and minimum_mask_iou >= 0.95 \
                and normal_false_positives <= completed * maximum_false_positive_rate
            print(f"VISUALIDENTITY_NORMAL_FALSE_POSITIVES:{normal_false_positives}/{completed}", flush=True)
            return {
                "status": "passed" if passed else "failed",
                "sampleCount": completed,
                "normalSampleCount": completed,
                "normalFalsePositiveCount": normal_false_positives,
                "maxScoreDifference": maximum_score_difference,
                "minimumMaskIou": minimum_mask_iou,
                "labelMismatches": label_mismatches,
                "errors": errors,
            }


        def write_manifest(config: dict, onnx_path: Path, manifest_path: Path, input_name: str, output_names: list[str]) -> None:
            """在 ONNX 模型旁写入部署契约。"""
            digest = hashlib.sha256(onnx_path.read_bytes()).hexdigest()
            manifest = {
                "schemaVersion": "1.0",
                "family": "anomalib",
                "anomalibVersion": "2.6.2",
                "algorithm": {
                    "padim": "padim",
                    "efficient_ad_small": "efficientAdSmall",
                    "patchcore": "patchCore",
                }[config["model"]],
                "modelSha256": digest,
                "input": {
                    "name": input_name,
                    "elementType": "float32",
                    "layout": "nchw",
                    "colorSpace": "rgb",
                    "width": config["imageSize"],
                    "height": config["imageSize"],
                    "resizeMode": "stretch",
                    "valueRange": "zeroToOne",
                    "normalizationEmbedded": True,
                },
                "outputs": {
                    "predictionScore": next((name for name in output_names if "score" in name.lower()), "pred_score"),
                    "predictionLabel": next((name for name in output_names if "label" in name.lower()), "pred_label"),
                    "anomalyMap": next((name for name in output_names if "anomaly" in name.lower() and "map" in name.lower()), "anomaly_map"),
                    "predictionMask": next((name for name in output_names if "mask" in name.lower()), "pred_mask"),
                },
                "postProcessing": {"threshold": 0.5, "thresholdSource": "manual"},
            }
            atomic_json(manifest_path, manifest)


        def main() -> int:
            """按照宿主生成的 JSON 配置训练、导出并校验单个模型。"""
            parser = argparse.ArgumentParser()
            parser.add_argument("--config", required=True)
            parser.add_argument("--result", required=True)
            args = parser.parse_args()
            result_path = Path(args.result).resolve()
            try:
                config = json.loads(Path(args.config).read_text(encoding="utf-8"))
                from anomalib.data import Folder
                from anomalib.deploy import ExportType
                from anomalib.engine import Engine
                import onnxruntime as ort
                import torch

                torch.manual_seed(config["randomSeed"])
                np.random.seed(config["randomSeed"])
                if torch.cuda.is_available():
                    torch.cuda.manual_seed_all(config["randomSeed"])
                dataset_root = Path(config["datasetRoot"]).resolve()
                artifact_root = Path(config["artifactRoot"]).resolve()
                artifact_root.mkdir(parents=True, exist_ok=True)
                model = create_model(config["model"], config["imageSize"])
                datamodule = Folder(
                    name="visual_identity",
                    root=dataset_root,
                    normal_dir="train/good",
                    train_batch_size=1 if config["model"] == "efficient_ad_small" else 8,
                    eval_batch_size=1,
                    num_workers=config["workerCount"],
                    test_split_mode="synthetic",
                    test_split_ratio=0.2,
                    val_split_mode="same_as_test",
                    seed=config["randomSeed"],
                )
                accelerator = "gpu" if config["device"] == "cuda" else config["device"]
                engine = Engine(
                    default_root_dir=artifact_root / "runs",
                    accelerator=accelerator,
                    devices=1,
                    max_epochs=config["maxEpochs"],
                    deterministic=True,
                    logger=False,
                )
                print("VISUALIDENTITY_PHASE:training", flush=True)
                engine.fit(model=model, datamodule=datamodule)
                checkpoint = Path(engine.best_model_path or "")
                if not checkpoint.is_file():
                    candidates = sorted((artifact_root / "runs").rglob("*.ckpt"))
                    if not candidates:
                        raise RuntimeError("Anomalib did not produce a checkpoint")
                    checkpoint = candidates[-1]
                stable_checkpoint = artifact_root / "model.ckpt"
                shutil.copy2(checkpoint, stable_checkpoint)
                print("VISUALIDENTITY_PHASE:exporting", flush=True)
                onnx_path = Path(engine.export(
                    model=model,
                    export_type=ExportType.ONNX,
                    export_root=artifact_root,
                    input_size=(config["imageSize"], config["imageSize"]),
                ))
                stable_onnx = artifact_root / "model.onnx"
                if onnx_path.resolve() != stable_onnx.resolve():
                    shutil.copy2(onnx_path, stable_onnx)
                sample_paths = sorted((dataset_root / "calibration" / "good").iterdir())
                print("VISUALIDENTITY_PHASE:parity", flush=True)
                gate = parity(model, stable_onnx, sample_paths, config["imageSize"], config["maximumNormalFalsePositiveRate"])
                if gate["status"] == "passed":
                    session = ort.InferenceSession(str(stable_onnx), providers=["CPUExecutionProvider"])
                    write_manifest(
                        config,
                        stable_onnx,
                        artifact_root / "model.manifest.json",
                        session.get_inputs()[0].name,
                        [item.name for item in session.get_outputs()],
                    )
                atomic_json(result_path, gate)
                return 0 if gate["status"] == "passed" else 3
            except Exception as error:
                atomic_json(result_path, {
                    "status": "failed",
                    "sampleCount": 0,
                    "normalSampleCount": 0,
                    "normalFalsePositiveCount": 0,
                    "maxScoreDifference": 1.0,
                    "minimumMaskIou": 0.0,
                    "labelMismatches": 0,
                    "errors": [str(error)],
                })
                print(str(error), file=sys.stderr, flush=True)
                return 2


        if __name__ == "__main__":
            raise SystemExit(main())
        """";
}
