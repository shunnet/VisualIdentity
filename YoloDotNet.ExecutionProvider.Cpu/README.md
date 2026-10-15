# Information
YoloDotNet uses modular execution providers to run inference on different hardware backends.
Each provider targets a specific platform or accelerator and may require additional system-level dependencies such as runtimes, drivers, or SDKs.

This repository integrates its components through source project references rather than NuGet installation commands.\
This document describes the requirements and usage of the **CPU execution provider**.


# Core Library Requirement

The CPU provider project already references the repository's **YoloDotNet** project, which contains the shared inference pipeline, models, and APIs. Keep both project directories in the same repository layout; no separate core package installation is required.

# Execution Provider - CPU

The CPU execution provider runs inference on the system CPU using ONNX Runtime’s built-in CPU backend.\
It is the most portable execution provider and requires no additional dependencies.

# Requirements:

- Any x64-compatible CPU
- Windows, Linux, or macOS

**Notes**:
- The CPU execution provider is always available and works on all supported platforms.
- Performance is lower compared to hardware-accelerated providers, but it is ideal for development, testing, and environments without GPU or NPU support.

# Installation

Use the .NET 10 SDK. Run from the repository root and replace `../MyApp/MyApp.csproj` with your application project path:

```bash
dotnet add ../MyApp/MyApp.csproj reference ./YoloDotNet.ExecutionProvider.Cpu/YoloDotNet.ExecutionProvider.Cpu.csproj
dotnet restore ../MyApp/MyApp.csproj
```

Third-party dependencies, including `Microsoft.ML.OnnxRuntime`, restore from the project's `PackageReference` entries. Do not also reference the CUDA provider in the same application. For integration through `Snet.Yolo.Server`, see the [source project reference guide](../README.en.md#-source-project-references).

# Usage Example:
```csharp
using YoloDotNet;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Models;

using var yolo = new Yolo(new YoloOptions
{
    ExecutionProvider = new CpuExecutionProvider(model: "path/to/model.onnx"),

    // ...other options
});
```
