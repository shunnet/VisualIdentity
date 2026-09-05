# -*- coding: utf-8 -*-
"""Export an Ultralytics model without doing work when this module is imported."""

from __future__ import annotations

import argparse

from ultralytics import YOLO


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Export an Ultralytics model.")
    parser.add_argument("model", nargs="?", default="best.pt", help="Model path or Ultralytics model name.")
    parser.add_argument("--format", default="onnx", dest="export_format", help="Export format (default: onnx).")
    parser.add_argument("--opset", type=int, default=17, help="ONNX opset; use 18 for YOLOv26 models.")
    parser.add_argument("--device", help="Optional export device, for example cpu or 0.")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    options: dict[str, object] = {"format": args.export_format, "opset": args.opset}
    if args.device:
        options["device"] = args.device
    YOLO(args.model).export(**options)


if __name__ == "__main__":
    main()
