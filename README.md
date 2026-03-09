# <img src="https://api.shunnet.top/pic/nuget.png" height="28"> VisualIdentity  

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)  
[![Repo](https://img.shields.io/badge/Repository-shunnet/VisualIdentity-blue)](https://github.com/shunnet/VisualIdentity)  

> 🚀 **基于 .NET 10 的多模型智能识别平台**  
> 高效 · 灵活 · 易部署  


## 🌟 项目简介  

在 **AI 应用落地** 的过程中，**模型管理** 与 **多任务识别** 一直是开发者的痛点。  
无论是 **检测、分类、分割、姿态估计、定向检测**，往往都需要同时部署多个模型，传统方案在 **效率** 和 **易用性** 上总会遇到瓶颈。  

**VisualIdentity** 正是为了解决这一系列问题而生。  
它结合了 **.NET 10** 的现代化能力、[YoloDotNet](https://github.com/NickSwardh/YoloDotNet) 的高性能推理、以及 **SQLite** 的轻量级管理，为开发者提供一个 **开箱即用** 的智能识别平台。  

✅ 多模型管理  
✅ 单机多任务识别  
✅ 跨平台部署  


## 🎯 应用场景  

- 🏭 **工业质检**：瑕疵检测、异物识别  
- 🛒 **零售分析**：顾客行为、货架检测  
- 🛡️ **智能安防**：异常行为、姿态识别  
- 🎓 **科研教育**：多模型实验平台  
- 🌐 **边缘计算**：轻量化部署到嵌入式或服务器  



## 💡 ONNX 模型导出要求

- 对于**YOLOv26**，导出时 opset=18
- 对于**YOLOv5u–YOLOv12**，导出时 opset=17

> [!重要]
> 使用正确的作集确保与 ONNX 运行时的最佳兼容性和性能。
> 有关如何将模型导出到 ONNX 的更多信息，请参见 https://docs.ultralytics.com/modes/export/


**示例导出命令（Ultralytics CLI）:**
```bash
# For YOLOv5u–YOLOv12 (opset 17)
yolo export model=yolov8n.pt format=onnx opset=17

# For YOLOv26 (opset 18)
yolo export model=yolo26n.pt format=onnx opset=18
```

## 📦 NuGet 安装  

```bash
dotnet add package Snet.Yolo.Server

# CPU
dotnet add package YoloDotNet.ExecutionProvider.Cpu

# 硬件（选择一项）
dotnet add package YoloDotNet.ExecutionProvider.Cuda
dotnet add package YoloDotNet.ExecutionProvider.OpenVino
dotnet add package YoloDotNet.ExecutionProvider.CoreML
dotnet add package YoloDotNet.ExecutionProvider.DirectML
```

### 💡 调用示例

```csharp
using SkiaSharp;
using Snet.Model.data;
using Snet.Yolo.Server;
using Snet.Yolo.Server.handler;
using Snet.Yolo.Server.models.data;
using Snet.Yolo.Server.models.@enum;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Extensions;
using YoloDotNet.Models;

namespace Snet.Yolo.Test
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            //????? 为对应数据

            // 原始图片路径
            string imagePath = "?????";

            //模型路径
            string onnxModel = "?????";

            //识别类型
            OnnxType onnxType = OnnxType.ObjectDetection;

            //直接调用库来进行本地识别操作
            using SKImage image2 = SKImage.FromEncodedData(imagePath);

            // 调用识别
            OperateResult operateResult = await IdentityOperate.Instance(new Yolo.Server.models.data.IdentityData
            {
                Hardware = new CpuExecutionProvider(onnxModel),
                IdentifyType = onnxType,
                SN = $"{onnxType}{onnxModel}"
            }).RunAsync(new ObjectDetectionData
            {
                Confidence = 0.23,
                Iou = 0.7,
                File = image2.Encode().ToArray()
            });

            // 转换结果
            List<ObjectDetection> results2 = operateResult.GetObjectDetectionResult().ToObjectDetection();

            //绘制结果
            using SKBitmap resultImage2 = image2.Draw(results2);

        }
    }
}

```

## ⚙️ 功能特性  

### 🔹 多模型管理  
- 支持 **增 / 删 / 改 / 查**  
- 模型 **版本化 & 快速切换**  
- 一机多模型轻松维护  

### 🔹 单机多任务流畅运行  
- 支持 **检测 / OBB 定向检测 / 分类 / 分割 / 姿态估计**  
- 基于 **YoloDotNet 高速推理内核**  
- **零配置，一键运行**  

### 🔹 跨平台 & 部署友好  
- 支持 **Windows / Linux / Docker 部署**  
- 提供轻量化配置，适配 **边缘设备 & 服务器**  
- **开箱即用，降低开发门槛**  


## 📚 依赖组件  

### [Snet.DB](https://www.nuget.org/packages/Snet.DB)  
- 集成 **Dapper & SqlSugarCore**  
- 支持高性能 **SQL 映射与链式查询**  
- 自动建表，高效开发  
- 保持轻量同时，具备 **生产级性能**  

### [YoloDotNet](https://github.com/NickSwardh/YoloDotNet)  
- **适用于.NET的、超快速的、可投入生产的YOLO推理**
- **YoloDotNet** 是一个模块化、轻量级的C#库，用于实现实时计算机视觉以及.NET环境下基于YOLO的推理。


## 🔬 支持的任务  

| 分类 (Classification) | 检测 (Detection) | OBB 定向检测 | 分割 (Segmentation) | 姿态估计 (Pose) |
|:---:|:---:|:---:|:---:|:---:|
| <img src="https://user-images.githubusercontent.com/35733515/297393507-c8539bff-0a71-48be-b316-f2611c3836a3.jpg" width=300> | <img src="https://user-images.githubusercontent.com/35733515/273405301-626b3c97-fdc6-47b8-bfaf-c3a7701721da.jpg" width=300> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/d15c5b3e-18c7-4c2c-9a8d-1d03fb98dd3c" width=300> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/3ae97613-46f7-46de-8c5d-e9240f1078e6" width=300> | <img src="https://github.com/NickSwardh/YoloDotNet/assets/35733515/b7abeaed-5c00-4462-bd19-c2b77fe86260" width=300> |
| <sub>[pexels.com](https://www.pexels.com/photo/hummingbird-drinking-nectar-from-blooming-flower-in-garden-5344570/)</sub> | <sub>[pexels.com](https://www.pexels.com/photo/men-s-brown-coat-842912/)</sub> | <sub>[pexels.com](https://www.pexels.com/photo/bird-s-eye-view-of-watercrafts-docked-on-harbor-8117665/)</sub> | <sub>[pexels.com](https://www.pexels.com/photo/man-riding-a-black-touring-motorcycle-903972/)</sub> | <sub>[pexels.com](https://www.pexels.com/photo/woman-doing-ballet-pose-2345293/)</sub> |


## ✅ 验证的YOLO模型

以下YOLO模型已使用YoloDotNet进行了**测试和验证**
官方Ultralytics导出和默认头部

| 分类 (Classification) | 检测 (Detection) | 分割 (Segmentation) | 姿态估计 (Pose) | OBB 定向检测 |
|:---:|:---:|:---:|:---:|:---:|
| YOLOv8-cls<br>YOLOv11-cls<br>YOLOv12-cls<br>YOLOv26-cls | YOLOv5u<br>YOLOv8<br>YOLOv9<br>YOLOv10<br>YOLOv11<br>YOLOv12<br>YOLOv26<br>RT-DETR | YOLOv8-seg<br>YOLOv11-seg<br>YOLOv12-seg<br>YOLOv26-seg<br>YOLO-World (v2) | YOLOv8-pose<br>YOLOv11-pose<br>YOLOv12-pose<br>YOLOv26-pose | YOLOv8-obb<br>YOLOv11-obb<br>YOLOv12-obb<br>YOLOv26-obb<br> |


## ⚡ 执行提供者

| Provider           | Windows | Linux | macOS |
|--------------------|---------|-------|-------|
| CPU                | ✅      | ✅    | ✅    |
| CUDA / TensorRT    | ✅      | ✅    | ❌    |
| OpenVINO           | ✅      | ✅    | ❌    |
| CoreML             | ❌      | ❌    | ✅    |
| DirectML           | ✅      | ❌    | ❌    |

> ℹ️ 只能引用**一个**执行提供程序包
> 混合使用不同的提供程序会导致本地运行时冲突


## 🙏 致谢  

- 🌐 [Shunnet.top](https://shunnet.top)  
- 🔥 [Ultralytics](https://github.com/ultralytics/ultralytics)  
- ⚡ [YoloDotNet](https://github.com/NickSwardh/YoloDotNet)  
- 🖥️ [WpfMUI](https://github.com/shunnet/WpfMUI)  


## 📜 许可证  

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)  

本项目基于 **MIT** 开源。  
请阅读 [LICENSE](LICENSE) 获取完整条款。  
⚠️ 软件按 “原样” 提供，作者不对使用后果承担责任。  


## 🌍 查阅  

👉 [点击跳转](https://shunnet.top/EaiUj)  
