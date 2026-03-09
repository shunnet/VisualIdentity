# 请参阅 https://aka.ms/customizecontainer 以了解如何自定义调试容器，以及 Visual Studio 如何使用此 Dockerfile 生成映像以更快地进行调试。

# 此阶段用于在快速模式(默认为调试配置)下从 VS 运行时
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# 此阶段用于生成服务项目
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Snet.Yolo.Api.Cpu/Snet.Yolo.Api.Cpu.csproj", "Snet.Yolo.Api/"]
COPY ["Snet.Yolo.Server/Snet.Yolo.Server.csproj", "Snet.Yolo.Server/"]
RUN dotnet restore "./Snet.Yolo.Api.Cpu/Snet.Yolo.Api.Cpu.csproj"
COPY . .
WORKDIR "/src/Snet.Yolo.Api.Cpu"
RUN dotnet build "./Snet.Yolo.Api.Cpu.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 此阶段用于发布要复制到最终阶段的服务项目
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Snet.Yolo.Api.Cpu.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# 更新包索引并安装 SkiaSharp 依赖
RUN apt-get update \
        && apt-get install -y --no-install-recommends \
        libgomp1 \
        libnuma1 \
        libfontconfig1 \
        libfreetype6 \
        libx11-6 \
        libxrender1 \
        libxcb1 \
        libxrandr2 \
    && rm -rf /var/lib/apt/lists/*

# 此阶段在生产中使用，或在常规模式下从 VS 运行时使用(在不使用调试配置时为默认值)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Snet.Yolo.Api.Cpu.dll"]