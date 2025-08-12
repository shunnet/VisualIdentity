# <img src="https://api.shunnet.top/pic/shun.png" height=24> VisualIdentity

### VisualIdentity
**基于 .net9 与 [YoloDotNet](https://github.com/NickSwardh/YoloDotNet) 开发，使用轻量级Sqlite数据库实现多模型管理，可以多任务单机运行，开箱即用** 

### 引用库
1. [Snet.DB](https://shunnet.top)
2. [YoloDotNet](https://github.com/NickSwardh/YoloDotNet)

### 功能
1. 模型管理（增/删/改/查）
2. 单机多个不同识别任务顺畅切换识别（检测/定向检测/分类/分割/姿态）
3. 开箱即用

### YoloDotNet
**是一个速度极快、功能齐全的C#库，用于使用YOLOv5u-v12、YOLO World和YOLO-E模型进行实时对象检测、OBB、分割、分类、姿态估计和跟踪。** 

### 支持的版本:
```Yolov5u``` ```Yolov8``` ```Yolov9``` ```Yolov10``` ```Yolov11``` ```Yolov12``` ```Yolo-World``` ```YoloE```

### 支持的任务:

| Classification | Object Detection | OBB Detection | Segmentation | Pose Estimation |
|:---:|:---:|:---:|:---:|:---:|
| <img src="https://user-images.githubusercontent.com/35733515/297393507-c8539bff-0a71-48be-b316-f2611c3836a3.jpg" width=300> | <img src="https://user-images.githubusercontent.com/35733515/273405301-626b3c97-fdc6-47b8-bfaf-c3a7701721da.jpg" width=300> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/d15c5b3e-18c7-4c2c-9a8d-1d03fb98dd3c" width=300> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/3ae97613-46f7-46de-8c5d-e9240f1078e6" width=300> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/b7abeaed-5c00-4462-bd19-c2b77fe86260" width=300> |
| <sub>[image from pexels.com](https://www.pexels.com/photo/hummingbird-drinking-nectar-from-blooming-flower-in-garden-5344570/)</sub> | <sub>[image from pexels.com](https://www.pexels.com/photo/men-s-brown-coat-842912/)</sub> | <sub>[image from pexels.com](https://www.pexels.com/photo/bird-s-eye-view-of-watercrafts-docked-on-harbor-8117665/)</sub> | <sub>[image from pexels.com](https://www.pexels.com/photo/man-riding-a-black-touring-motorcycle-903972/)</sub> | <sub>[image from pexels.com](https://www.pexels.com/photo/woman-doing-ballet-pose-2345293/)</sub> |

### 支持的执行提供程序
![ONNX Runtime](https://img.shields.io/badge/Backend-ONNX_Runtime-1f65dc?style=flat&logo=onnx)
![CPU](https://img.shields.io/badge/CPU-Supported-lightgrey?style=flat&logo=intel)
![CUDA](https://img.shields.io/badge/GPU-CUDA-76B900?style=flat&logo=nvidia)
![TensorRT](https://img.shields.io/badge/Inference-TensorRT-00BFFF?style=flat&logo=nvidia)


# 致谢
https://github.com/ultralytics/ultralytics \
https://github.com/NickSwardh/YoloDotNet