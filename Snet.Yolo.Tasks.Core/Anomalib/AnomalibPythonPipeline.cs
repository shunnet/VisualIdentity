namespace Snet.Yolo.Tasks.Core.Anomalib;

using System.Text;

/// <summary>受应用版本控制的 Anomalib 2.6.2 训练、导出与一致性门禁脚本。</summary>
public static class AnomalibPythonPipeline
{
    private static readonly object MaterializeLock = new();

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
        lock (MaterializeLock)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, Script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }
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

        EARLY_STOP_MONITOR = "early_stop_pixel_AUROC"


        def training_callbacks(config: dict, model, artifact_root: Path):
            """EfficientAD 每轮验证像素 AUROC；早停和最佳检查点使用同一指标。"""
            if config["model"] != "efficient_ad_small":
                return [], None, None
            from anomalib.callbacks import ModelCheckpoint
            from anomalib.metrics import AUROC
            from lightning.pytorch.callbacks import EarlyStopping

            # 固定阈值的流式统计不保留整套验证像素，避免高分辨率图像占满显存。
            model.evaluator.val_metrics.append(AUROC(
                fields=["anomaly_map", "gt_mask"], prefix="early_stop_pixel_", thresholds=256,
            ))
            checkpoint = ModelCheckpoint(
                dirpath=artifact_root / "best-checkpoints",
                filename="best-{epoch:05d}",
                auto_insert_metric_name=False,
                monitor=EARLY_STOP_MONITOR,
                mode="max",
                save_top_k=1,
                save_on_train_epoch_end=False,
            )
            stopping = None
            callbacks = [checkpoint]
            if config.get("earlyStoppingEnabled", True):
                stopping = EarlyStopping(
                    monitor=EARLY_STOP_MONITOR,
                    mode="max",
                    patience=config.get("earlyStoppingPatience", 50),
                    min_delta=config.get("earlyStoppingMinDelta", 0.001),
                    strict=True,
                    check_finite=True,
                    check_on_train_epoch_end=False,
                    verbose=True,
                )
                callbacks.append(stopping)
            return callbacks, checkpoint, stopping


        def restore_best_weights(model, checkpoint_path: Path, artifact_root: Path) -> int:
            """只恢复本次训练目录内自行生成的检查点，供 ONNX 导出和一致性校验共用。"""
            import torch
            path = Path(checkpoint_path).resolve()
            if not path.is_relative_to(Path(artifact_root).resolve()) or not path.is_file():
                raise RuntimeError("最佳检查点不在本次训练产物目录中，拒绝加载。")
            # Lightning 检查点包含预处理模块等超参数；仅允许本次训练生成的可信文件。
            state = torch.load(path, map_location="cpu", weights_only=False)
            model.to("cpu")
            model.load_state_dict(state["state_dict"], strict=True)
            model.eval()
            return int(state["epoch"]) + 1


        def atomic_json(path: Path, value: dict) -> None:
            """原子写入 JSON，避免宿主读取到不完整的门禁结果。"""
            path.parent.mkdir(parents=True, exist_ok=True)
            temporary = path.with_suffix(path.suffix + ".tmp")
            temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
            os.replace(temporary, path)


        def prepare_efficientad_data(cache_root: Path) -> Path:
            """以文件锁、摘要和原子发布准备所有项目共用的 ImageNette 数据。"""
            import http.client
            import tempfile
            import time
            import urllib.error
            import urllib.request
            from filelock import FileLock
            from torchvision.datasets import ImageFolder
            from anomalib.data.utils.download import extract
            from anomalib.models.image.efficient_ad.lightning_model import IMAGENETTE_DOWNLOAD_INFO

            info = IMAGENETTE_DOWNLOAD_INFO
            root = Path(cache_root).resolve()
            root.mkdir(parents=True, exist_ok=True)
            dataset = root / "dataset"
            marker = dataset / ".complete.sha256"
            archive = root / "imagenette2.tgz"

            def digest(path: Path) -> str:
                """分块校验大归档，不把整个数据包读入内存。"""
                checksum = hashlib.sha256()
                with path.open("rb") as source:
                    for block in iter(lambda: source.read(1024 * 1024), b""):
                        checksum.update(block)
                return checksum.hexdigest()

            print(f"准备 EfficientAD 共享 ImageNette 数据：{root}", flush=True)
            with FileLock(str(root / ".prepare.lock"), timeout=600):
                # 强制取消可能留下暂存目录；持有锁时没有其他准备任务使用这些目录。
                for abandoned in root.glob(".extract-*"):
                    if abandoned.is_dir() and not abandoned.is_symlink():
                        shutil.rmtree(abandoned)
                if marker.is_file() and marker.read_text(encoding="ascii").strip() == info.hashsum:
                    try:
                        image_count = len(ImageFolder(dataset))
                    except (FileNotFoundError, RuntimeError):
                        print("共享 ImageNette 数据不完整，将重新准备。", flush=True)
                    else:
                        print(f"复用已完成的 ImageNette 缓存：{dataset}，{image_count} 张图片，无需下载。", flush=True)
                        return dataset
                if archive.is_file() and digest(archive) != info.hashsum:
                    print("ImageNette 缓存归档 SHA-256 不符，将重新下载。", flush=True)
                    archive.unlink()
                if not archive.is_file():
                    pending = root / ".imagenette2.tgz.downloading"
                    try:
                        for attempt in range(1, 4):
                            try:
                                print(f"下载 ImageNette [{attempt}/3]：{info.url}", flush=True)
                                downloaded = 0
                                last_progress = time.monotonic()
                                with urllib.request.urlopen(info.url, timeout=30) as response, pending.open("wb") as target:
                                    expected = int(response.headers.get("Content-Length", "0"))
                                    while block := response.read(1024 * 1024):
                                        target.write(block)
                                        downloaded += len(block)
                                        if time.monotonic() - last_progress >= 5:
                                            print(f"ImageNette 已下载 {downloaded / 1048576:.1f} MiB。", flush=True)
                                            last_progress = time.monotonic()
                                    if expected and downloaded != expected:
                                        raise OSError(f"ImageNette 响应中断：应收到 {expected} 字节，实际 {downloaded} 字节。")
                                break
                            except (OSError, urllib.error.URLError, http.client.HTTPException) as error:
                                permanent = isinstance(error, urllib.error.HTTPError) and error.code < 500 and error.code not in (408, 429)
                                if permanent or attempt == 3:
                                    raise RuntimeError(
                                        f"ImageNette 下载失败：{info.url}；已尝试 {attempt} 次；{error}。"
                                        f"请检查网络及 Training:Proxy，或把完整 imagenette2.tgz 放到 {archive} 后重试。"
                                    ) from error
                                print(f"ImageNette 下载暂时失败：{error}；准备重试。", flush=True)
                                time.sleep(attempt * 2)
                        if digest(pending) != info.hashsum:
                            raise RuntimeError(f"ImageNette 下载内容 SHA-256 校验失败，拒绝使用；预期 {info.hashsum}，网址 {info.url}。")
                        os.replace(pending, archive)
                    finally:
                        pending.unlink(missing_ok=True)
                print(f"复用已校验的 ImageNette 归档并解压：{archive}", flush=True)
                staging = Path(tempfile.mkdtemp(prefix=".extract-", dir=root))
                try:
                    staged_archive = staging / archive.name
                    try:
                        os.link(archive, staged_archive)
                    except OSError:
                        shutil.copyfile(archive, staged_archive)
                    # 沿用 Anomalib 的安全解压；它删除暂存归档，保留共享归档以便离线重建。
                    extract(staged_archive, staging)
                    image_count = len(ImageFolder(staging))
                    (staging / marker.name).write_text(info.hashsum, encoding="ascii")
                    if dataset.exists():
                        shutil.rmtree(dataset)
                    os.replace(staging, dataset)
                    print(f"ImageNette 共享数据准备完成：{dataset}，{image_count} 张图片。", flush=True)
                    return dataset
                finally:
                    if staging.exists():
                        shutil.rmtree(staging)


        def create_model(name: str, image_size: int, imagenette_dir: Path | None = None):
            """仅创建 .NET 允许列表中的模型。"""
            from anomalib.models import EfficientAd, Padim, Patchcore
            if name == "padim":
                return Padim(backbone="resnet18", pre_processor=Padim.configure_pre_processor(image_size=(image_size, image_size)))
            if name == "efficient_ad_small":
                imagenette_dir = imagenette_dir or Path(__file__).resolve().parents[1] / ".cache" / "imagenette" / "dataset"
                return EfficientAd(pre_processor=EfficientAd.configure_pre_processor(image_size=(image_size, image_size)), imagenet_dir=imagenette_dir)
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
            """使用与训练一致的浮点抗锯齿缩放，避免先缩成 8 位图片产生量化与采样偏差。"""
            import torch
            from torchvision.transforms.v2 import functional as vision
            with Image.open(image_path) as image:
                array = np.array(image.convert("RGB"), copy=True)
            tensor = torch.from_numpy(array).permute(2, 0, 1).unsqueeze(0)
            if "uint8" in session_input.type:
                return vision.resize(tensor, [image_size, image_size], antialias=True).numpy()
            tensor = tensor.float().div_(255.0)
            return vision.resize(tensor, [image_size, image_size], antialias=True).numpy()


        def calibrate_normal_thresholds(model, datamodule, image_size: int, maximum_false_positive_rate: float) -> dict:
            """仅用训练内部留出的原始正常图标定阈值；最终校准集不参与拟合。"""
            import math
            import torch
            from types import SimpleNamespace
            post = model.post_processor
            previous_image = scalar(post._image_threshold, float("nan"))
            previous_pixel = scalar(post._pixel_threshold, float("nan"))
            report = {"completed": False, "sampleCount": 0, "quantile": 1.0 - maximum_false_positive_rate,
                      "previousImageThreshold": previous_image if np.isfinite(previous_image) else None,
                      "previousPixelThreshold": previous_pixel if np.isfinite(previous_pixel) else None,
                      "imageThreshold": None, "pixelThreshold": None, "samples": [], "errors": []}
            validation = datamodule.val_data
            samples = getattr(validation, "source_samples", getattr(validation, "samples", None))
            if samples is None:
                report["errors"].append("内部验证集没有可用于正常阈值标定的源图片。")
                return report
            normal_paths = [Path(value) for value in samples.loc[samples.label_index == 0, "image_path"]]
            training_paths = {Path(value).resolve() for value in datamodule.train_data.samples.image_path}
            if any(path.resolve() in training_paths for path in normal_paths):
                report["errors"].append("正常阈值标定集与训练集重叠，拒绝使用模型已见过的图片标定。")
                return report
            model.eval()
            for index, path in enumerate(normal_paths, start=1):
                try:
                    inputs = torch.from_numpy(onnx_input(path, SimpleNamespace(type="tensor(float)"), image_size)).to(model.device)
                    with torch.inference_mode():
                        predictions = model.model(model.pre_processor(inputs))
                    image_score = scalar(predictions.pred_score, float("nan"))
                    pixel_minimum = scalar(predictions.anomaly_map.amin(), float("nan"))
                    pixel_maximum = scalar(predictions.anomaly_map.amax(), float("nan"))
                    if not np.isfinite(image_score) or not np.isfinite(pixel_minimum) or not np.isfinite(pixel_maximum):
                        raise RuntimeError("正常图片返回非有限的原始分数。")
                    report["samples"].append({"fileName": path.name, "rawImageScore": image_score,
                                              "rawPixelMinimum": pixel_minimum, "rawPixelMaximum": pixel_maximum})
                    print(f"正常阈值标定[{index}/{len(normal_paths)}] {path.name}：整图原始分数={image_score:.6f}，"
                          f"像素原始最大值={pixel_maximum:.6f}。", flush=True)
                except Exception as error:
                    report["errors"].append(f"{path.name}: {type(error).__name__}: {error}")
                    print(f"正常阈值标定[{index}/{len(normal_paths)}] 处理失败：{error}；继续下一张。", flush=True)
            report["sampleCount"] = len(report["samples"])
            if not report["samples"]:
                report["errors"].append("没有完成任何内部正常图片的阈值标定，保留原阈值。")
                return report
            rank = math.ceil((report["sampleCount"] - 1) * report["quantile"])
            for field, target in [("rawImageScore", post._image_threshold), ("rawPixelMaximum", post._pixel_threshold)]:
                value = sorted(sample[field] for sample in report["samples"])[rank]
                # 采用上侧经验分位数，向上移动一个 float32 单位以避免归一化舍入令边界正常图翻转。
                threshold = torch.nextafter(target.new_tensor(value), target.new_tensor(float("inf")))
                target.copy_(threshold)
            # 归一化区间同时覆盖原验证数据与内部正常数据，保持阈值与分数的解释一致。
            for prefix, minimum_field, maximum_field in [("image", "rawImageScore", "rawImageScore"), ("pixel", "rawPixelMinimum", "rawPixelMaximum")]:
                low, high = getattr(post, prefix + "_min"), getattr(post, prefix + "_max")
                minimum = min(sample[minimum_field] for sample in report["samples"])
                maximum = max(sample[maximum_field] for sample in report["samples"])
                low.copy_(low.new_tensor(min(scalar(low) if torch.isfinite(low) else minimum, minimum)))
                high.copy_(high.new_tensor(max(scalar(high) if torch.isfinite(high) else maximum, maximum)))
                if high <= low:
                    high.copy_(torch.nextafter(low, low.new_tensor(float("inf"))))
            report.update(completed=True, imageThreshold=scalar(post._image_threshold), pixelThreshold=scalar(post._pixel_threshold))
            print(f"正常阈值标定完成：内部正常图 {report['sampleCount']} 张，上侧 {report['quantile']:.0%} 经验分位数；"
                  f"整图原始阈值 {report['previousImageThreshold']} → {report['imageThreshold']:.6f}，"
                  f"像素原始阈值 {report['previousPixelThreshold']} → {report['pixelThreshold']:.6f}。"
                  "最终独立校准图片未参与阈值拟合；该统计不代表真实缺陷检出率。", flush=True)
            return report


        def update_checkpoint_post_processor(model, checkpoint_path: Path, artifact_root: Path) -> None:
            """只更新本次可信检查点的后处理状态，保留最佳轮次与训练权重。"""
            import torch
            path = checkpoint_path.resolve()
            if not path.is_relative_to(artifact_root.resolve()):
                raise RuntimeError("拒绝修改本次训练目录以外的检查点。")
            state = torch.load(path, map_location="cpu", weights_only=False)
            state["state_dict"].update({key: value.detach().cpu().clone() for key, value in model.state_dict().items()
                                      if key.startswith("post_processor.")})
            temporary = path.with_suffix(".calibrating.tmp")
            try:
                torch.save(state, temporary)
                os.replace(temporary, path)
            finally:
                temporary.unlink(missing_ok=True)


        def parity(model, onnx_path: Path, samples: list[Path], image_size: int, maximum_false_positive_rate: float,
                   sample_names: dict | None = None) -> dict:
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
            sample_results: list[dict] = []
            completed = 0
            for index, sample in enumerate(samples, start=1):
                name = (sample_names or {}).get(sample.name, sample.name)
                row = {"index": index, "fileName": name, "passed": False, "torchScore": None,
                       "onnxScore": None, "torchLabel": None, "onnxLabel": None,
                       "scoreDifference": None, "maskIou": None, "imageThreshold": None, "message": ""}
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
                    torch_score = scalar(torch_result.pred_score, float("nan"))
                    exported_score = scalar(onnx_score, float("nan"))
                    if not np.isfinite(torch_score) or not np.isfinite(exported_score):
                        raise RuntimeError("模型返回非有限的整图异常分数。")
                    torch_label = bool(scalar(torch_result.pred_label))
                    exported_label = bool(scalar(onnx_label))
                    score_difference = abs(torch_score - exported_score)
                    iou = mask_iou(torch_result.pred_mask, onnx_mask)
                    if not np.isfinite(iou):
                        raise RuntimeError("模型返回非有限的掩码 IoU。")
                    post_processor = getattr(model, "post_processor", None)
                    threshold = scalar(getattr(post_processor, "normalized_image_threshold", None), float("nan"))
                    issues = []
                    if exported_label:
                        issues.append("正常图片被判为异常")
                    if torch_label != exported_label:
                        issues.append("训练模型与 ONNX 标签不一致")
                    if score_difference > 0.02:
                        issues.append("分数差超过 0.02")
                    if iou < 0.95:
                        issues.append("掩码 IoU 低于 0.95")
                    row.update(torchScore=torch_score, onnxScore=exported_score,
                               torchLabel=torch_label, onnxLabel=exported_label,
                               scoreDifference=score_difference, maskIou=iou,
                               imageThreshold=threshold if np.isfinite(threshold) else None,
                               passed=not issues, message="；".join(issues))
                    maximum_score_difference = max(maximum_score_difference, score_difference)
                    if torch_label != exported_label:
                        label_mismatches += 1
                    if exported_label:
                        normal_false_positives += 1
                    minimum_mask_iou = min(minimum_mask_iou, iou)
                    completed += 1
                    print(f"校准[{index}/{len(samples)}] {name}：{'通过' if row['passed'] else '未通过'}；"
                          f"PyTorch 分数={torch_score:.6f}，异常={torch_label}；"
                          f"ONNX 分数={exported_score:.6f}，异常={exported_label}；"
                          f"整图阈值={row['imageThreshold']}，分数差={score_difference:.6f}，"
                          f"掩码 IoU={iou:.6f}；{row['message']}", flush=True)
                except Exception as error:  # 将每个样本的失败都写入门禁报告。
                    row["message"] = f"{type(error).__name__}: {error}"
                    errors.append(f"{name}: {row['message']}")
                    print(f"校准[{index}/{len(samples)}] {name}：处理失败；{row['message']}；继续下一张。", flush=True)
                sample_results.append(row)
                print(f"VISUALIDENTITY_PARITY:{index}/{len(samples)}", flush=True)
            passed = completed == len(samples) and completed > 0 and not errors and label_mismatches == 0 \
                and maximum_score_difference <= 0.02 and minimum_mask_iou >= 0.95 \
                and normal_false_positives <= completed * maximum_false_positive_rate
            print(f"VISUALIDENTITY_NORMAL_FALSE_POSITIVES:{normal_false_positives}/{completed}", flush=True)
            passed_count = sum(row["passed"] for row in sample_results)
            pass_rate = passed_count / len(samples) if samples else 0.0
            false_positive_rate = normal_false_positives / completed if completed else 0.0
            print(f"校准汇总：成功比较 {completed}/{len(samples)}，逐图通过 {passed_count}/{len(samples)} "
                  f"({pass_rate:.2%})，正常图误报 {normal_false_positives}/{completed} "
                  f"({false_positive_rate:.2%})，最大分数差={maximum_score_difference:.6f}，"
                  f"最小掩码 IoU={minimum_mask_iou:.6f}，判定不一致={label_mismatches}。", flush=True)
            return {
                "status": "passed" if passed else "failed",
                "totalSampleCount": len(samples),
                "sampleCount": completed,
                "normalSampleCount": completed,
                "normalFalsePositiveCount": normal_false_positives,
                "maxScoreDifference": maximum_score_difference,
                "minimumMaskIou": minimum_mask_iou,
                "labelMismatches": label_mismatches,
                "errors": errors,
                "sampleResults": sample_results,
            }


        def write_manifest(config: dict, onnx_path: Path, manifest_path: Path, input_name: str, output_names: list[str],
                           threshold_source: str = "normalCalibration") -> None:
            """在 ONNX 模型旁写入部署契约。"""
            digest = hashlib.sha256()
            with onnx_path.open("rb") as model_file:
                for block in iter(lambda: model_file.read(1024 * 1024), b""):
                    digest.update(block)
            manifest = {
                "schemaVersion": "1.0",
                "family": "anomalib",
                "anomalibVersion": "2.6.2",
                "algorithm": {
                    "padim": "padim",
                    "efficient_ad_small": "efficientAdSmall",
                    "patchcore": "patchCore",
                }[config["model"]],
                "modelSha256": digest.hexdigest(),
                "input": {
                    "name": input_name,
                    "elementType": "float32",
                    "layout": "nchw",
                    "colorSpace": "rgb",
                    "width": config["imageSize"],
                    "height": config["imageSize"],
                    "resizeMode": "stretch",
                    "interpolation": "bilinearAntialias",
                    "valueRange": "zeroToOne",
                    "normalizationEmbedded": True,
                },
                "outputs": {
                    "predictionScore": next((name for name in output_names if "score" in name.lower()), "pred_score"),
                    "predictionLabel": next((name for name in output_names if "label" in name.lower()), "pred_label"),
                    "anomalyMap": next((name for name in output_names if "anomaly" in name.lower() and "map" in name.lower()), "anomaly_map"),
                    "predictionMask": next((name for name in output_names if "mask" in name.lower()), "pred_mask"),
                },
                "postProcessing": {"threshold": 0.5, "thresholdSource": threshold_source},
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
                selected_devices = ([int(value) for value in config["device"][5:].split(",")]
                                    if config["device"].startswith("cuda:") else [0] if config["device"] == "cuda" else [])
                if selected_devices:
                    os.environ["CUDA_VISIBLE_DEVICES"] = ",".join(str(value) for value in selected_devices)
                from anomalib.data import Folder
                from anomalib.deploy import ExportType
                from anomalib.engine import Engine
                import onnxruntime as ort
                import torch

                if selected_devices and torch.cuda.device_count() != len(selected_devices):
                    raise RuntimeError("Selected GPU devices are not visible to PyTorch")

                torch.manual_seed(config["randomSeed"])
                np.random.seed(config["randomSeed"])
                if torch.cuda.is_available():
                    torch.cuda.manual_seed_all(config["randomSeed"])
                dataset_root = Path(config["datasetRoot"]).resolve()
                artifact_root = Path(config["artifactRoot"]).resolve()
                artifact_root.mkdir(parents=True, exist_ok=True)
                imagenette_dir = prepare_efficientad_data(Path(config["imagenetteCacheRoot"])) if config["model"] == "efficient_ad_small" else None
                model = create_model(config["model"], config["imageSize"], imagenette_dir)
                callbacks, best_checkpoint, stopping = training_callbacks(config, model, artifact_root)
                datamodule = Folder(
                    name="visual_identity",
                    root=dataset_root,
                    normal_dir="train/good",
                    train_batch_size=1 if config["model"] in ("efficient_ad_small", "patchcore") else 8,
                    eval_batch_size=1,
                    num_workers=config["workerCount"],
                    test_split_mode="synthetic",
                    test_split_ratio=0.2,
                    val_split_mode="same_as_test",
                    seed=config["randomSeed"],
                )
                accelerator = "gpu" if selected_devices else config["device"]
                engine = Engine(
                    callbacks=callbacks,
                    default_root_dir=artifact_root / "runs",
                    accelerator=accelerator,
                    devices=list(range(len(selected_devices))) if selected_devices else 1,
                    strategy="ddp" if len(selected_devices) > 1 else "auto",
                    max_epochs=config["maxEpochs"],
                    check_val_every_n_epoch=1,
                    deterministic=True,
                    logger=False,
                )
                print("VISUALIDENTITY_PHASE:training", flush=True)
                if best_checkpoint is not None:
                    state = "启用" if stopping is not None else "关闭"
                    print(f"EfficientAD 早停{state}：最大 {config['maxEpochs']} 轮，"
                          f"验证指标 {EARLY_STOP_MONITOR}，耐心 {config.get('earlyStoppingPatience', 50)} 轮，"
                          f"最小改善幅度 {config.get('earlyStoppingMinDelta', 0.001)}。", flush=True)
                engine.fit(model=model, datamodule=datamodule)
                if len(selected_devices) > 1 and engine.trainer.world_size != len(selected_devices):
                    raise RuntimeError("Anomalib did not start the requested number of GPU workers")
                if not engine.trainer.is_global_zero:
                    return 0
                checkpoint = Path((best_checkpoint.best_model_path if best_checkpoint is not None else engine.best_model_path) or "")
                if best_checkpoint is not None:
                    current_score = scalar(engine.trainer.callback_metrics.get(EARLY_STOP_MONITOR), float("nan"))
                    best_score = scalar(best_checkpoint.best_model_score, float("nan"))
                    if not np.isfinite(current_score) or not np.isfinite(best_score) or not checkpoint.is_file():
                        raise RuntimeError("EfficientAD 验证指标缺失或不是有限值，无法选择有效的最佳模型。")
                elif not checkpoint.is_file():
                    candidates = sorted((artifact_root / "runs").rglob("*.ckpt"))
                    if not candidates:
                        raise RuntimeError("Anomalib did not produce a checkpoint")
                    checkpoint = candidates[-1]
                if not checkpoint.resolve().is_relative_to(artifact_root):
                    raise RuntimeError("检查点不在本次训练目录中。")
                stable_checkpoint = artifact_root / "model.ckpt"
                shutil.copy2(checkpoint, stable_checkpoint)
                if best_checkpoint is not None:
                    best_epoch = restore_best_weights(model, stable_checkpoint, artifact_root)
                    reason = "验证指标连续无明显改善，提前停止" if stopping is not None and stopping.stopped_epoch > 0 else "达到训练轮数上限"
                    print(f"EfficientAD {reason}；已完成 {engine.trainer.current_epoch} 轮，"
                          f"最佳模型为第 {best_epoch} 轮，{EARLY_STOP_MONITOR}={best_score:.6f}。", flush=True)
                model.to(engine.trainer.strategy.root_device)
                normal_calibration = calibrate_normal_thresholds(model, datamodule, config["imageSize"], config["maximumNormalFalsePositiveRate"])
                if normal_calibration["completed"]:
                    update_checkpoint_post_processor(model, stable_checkpoint, artifact_root)
                atomic_json(artifact_root / "normal-calibration.json", normal_calibration)
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
                gate = parity(model, stable_onnx, sample_paths, config["imageSize"], config["maximumNormalFalsePositiveRate"],
                              config.get("calibrationFileNames"))
                gate["normalCalibration"] = normal_calibration
                if normal_calibration["errors"]:
                    gate["errors"].extend(normal_calibration["errors"])
                    gate["status"] = "failed"
                session = ort.InferenceSession(str(stable_onnx), providers=["CPUExecutionProvider"])
                write_manifest(
                    config,
                    stable_onnx,
                    artifact_root / "model.manifest.json",
                    session.get_inputs()[0].name,
                    [item.name for item in session.get_outputs()],
                    "normalCalibration" if normal_calibration["completed"] else "syntheticCalibration",
                )
                gate["exportSucceeded"] = True
                atomic_json(result_path, gate)
                return 0  # 校准结果如实保留；完整导出不因质量报告未通过而丢弃。
            except Exception as error:
                if int(os.environ.get("LOCAL_RANK", "0")) == 0:
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
