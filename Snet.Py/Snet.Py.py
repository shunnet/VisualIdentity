# -*- coding: utf-8 -*-
from ultralytics import YOLO


# Load a model
model = YOLO("best.pt")  # load a custom trained model

# Export the model
# YOLOv5u–YOLOv12
model.export(format="onnx",opset=17)

# YOLOv26
# model.export(format="onnx",opset=18)