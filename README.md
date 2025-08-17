# <img src="https://api.shunnet.top/pic/shun.png" height=24> VisualIdentity

### VisualIdentity —— 基于 .NET 9 的多模型智能识别平台
1. 在 AI 应用的落地过程中，模型管理与多任务识别的高效运行一直是开发者的痛点。无论是对象检测、分类、分割，还是更复杂的姿态估计和定向检测，往往需要同时部署和管理多个模型，传统方案在效率和易用性上总会遇到瓶颈。
2. VisualIdentity 正是为了解决这一系列问题而生，结合了 .NET 9 的现代化开发能力、[YoloDotNet](https://github.com/NickSwardh/YoloDotNet) 的高性能推理、以及 SQLite 的轻量级管理，为开发者提供了一个 高效、灵活、易部署 的智能识别平台。
3. 它不仅能帮助开发者轻松管理多个 YOLO 模型，还能让单机也具备强大的多任务识别能力，真正做到 开箱即用。
4. 如果你正在寻找一款能够快速构建 多模型、多任务识别应用 的解决方案，VisualIdentity 会是一个理想的选择。

### 应用场景
1. 工业质检：快速检测瑕疵、异物识别
2. 零售分析：顾客行为分析、货架检测
3. 智能安防：异常行为监控、人员姿态识别
4. 科研教育：便捷的多模型实验环境
5. 边缘计算：轻量化部署在嵌入式或服务器上

### 多模型管理
1. 提供 增/删/改/查 完整管理功能
2. 支持模型版本化与快速切换
3. 适合需要同时维护多种识别模型的场景

### 单机多任务流畅运行
1. 同时支持多个任务：检测 / 定向检测 / 分类 / 分割 / 姿态 
2. 基于 YoloDotNet 高速推理内核，单机也能轻松胜任多任务切换
3. 无需复杂配置，即可实现 一键运行

### 跨平台 & 部署友好
1. 支持 Linux 与 Docker 部署
2. 提供轻量化配置方案，适合边缘设备与服务器环境
3. 开箱即用，降低开发与部署门槛

### [Snet.DB](https://www.nuget.org/packages/Snet.DB)
1. 集成 Dapper 与 SqlSugarCore 两款 ORM 框架
2. 提供高性能 SQL 映射与链式查询支持
3. 自动建表，开发更高效
4. 保持轻量化的同时，具备生产级别的性能表现

### [YoloDotNet](https://github.com/NickSwardh/YoloDotNet)
1. C# 生态下极快的、功能齐全的 YOLO 推理库
2. 支持 YOLOv5u - YOLOv12、YOLO World、YOLO-E 全系列模型
3. 功能覆盖： 实时对象检测 OBB（定向边界框检测） 图像分割 分类任务 姿态估计 多目标跟踪

### 支持的版本
```Yolov5u``` ```Yolov8``` ```Yolov9``` ```Yolov10``` ```Yolov11``` ```Yolov12``` ```Yolo-World``` ```YoloE```

### 支持的任务

| Classification | Object Detection | OBB Detection | Segmentation | Pose Estimation |
|:---:|:---:|:---:|:---:|:---:|
| <img src="https://user-images.githubusercontent.com/35733515/297393507-c8539bff-0a71-48be-b316-f2611c3836a3.jpg" width=300> | <img src="https://user-images.githubusercontent.com/35733515/273405301-626b3c97-fdc6-47b8-bfaf-c3a7701721da.jpg" width=300> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/d15c5b3e-18c7-4c2c-9a8d-1d03fb98dd3c" width=300> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/3ae97613-46f7-46de-8c5d-e9240f1078e6" width=300> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/b7abeaed-5c00-4462-bd19-c2b77fe86260" width=300> |
| <sub>[image from pexels.com](https://www.pexels.com/photo/hummingbird-drinking-nectar-from-blooming-flower-in-garden-5344570/)</sub> | <sub>[image from pexels.com](https://www.pexels.com/photo/men-s-brown-coat-842912/)</sub> | <sub>[image from pexels.com](https://www.pexels.com/photo/bird-s-eye-view-of-watercrafts-docked-on-harbor-8117665/)</sub> | <sub>[image from pexels.com](https://www.pexels.com/photo/man-riding-a-black-touring-motorcycle-903972/)</sub> | <sub>[image from pexels.com](https://www.pexels.com/photo/woman-doing-ballet-pose-2345293/)</sub> |

### 支持的程序
![ONNX Runtime](https://img.shields.io/badge/Backend-ONNX_Runtime-1f65dc?style=flat&logo=onnx)
![CPU](https://img.shields.io/badge/CPU-Supported-lightgrey?style=flat&logo=intel)
![CUDA](https://img.shields.io/badge/GPU-CUDA-76B900?style=flat&logo=nvidia)
![TensorRT](https://img.shields.io/badge/Inference-TensorRT-00BFFF?style=flat&logo=nvidia)

### 致谢
https://shunnet.top \
https://github.com/ultralytics/ultralytics \
https://github.com/NickSwardh/YoloDotNet

### 演示（稍等片刻,正在加载）
![演示](demonstrate.gif)

### 许可证

![许可证：GPL v3或更高版本](https://img.shields.io/badge/License-GPL_v3_or_later-blue)  
请阅读 [LICENSE](LICENSE.txt) 完整许可证文本的文件。 \
本软件按“原样”提供，不提供任何形式的保修。 \
作者对因使用该软件而产生的任何损害不承担责任。