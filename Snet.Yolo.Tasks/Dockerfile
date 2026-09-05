# 从仓库根目录构建：docker build -f Snet.Yolo.Tasks/Dockerfile -t snet-yolo-tasks .
# 支持通过 buildx 构建 linux/amd64 与 linux/arm64。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Snet.Yolo.Server/Snet.Yolo.Server.csproj Snet.Yolo.Server/
COPY Snet.Yolo.Tasks.Core/Snet.Yolo.Tasks.Core.csproj Snet.Yolo.Tasks.Core/
COPY Snet.Yolo.Tasks/Snet.Yolo.Tasks.csproj Snet.Yolo.Tasks/
RUN dotnet restore Snet.Yolo.Tasks/Snet.Yolo.Tasks.csproj

COPY Snet.Yolo.Server/ Snet.Yolo.Server/
COPY Snet.Yolo.Tasks.Core/ Snet.Yolo.Tasks.Core/
COPY Snet.Yolo.Tasks/ Snet.Yolo.Tasks/
RUN dotnet publish Snet.Yolo.Tasks/Snet.Yolo.Tasks.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# 图像处理和 CPU 推理依赖；训练服务使用 python3 创建独立虚拟环境。
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libgomp1 libnuma1 libfontconfig1 libfreetype6 \
        libx11-6 libxrender1 libxcb1 libxrandr2 libgl1 \
        python3 python3-pip python3-venv \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

# 上传、模型、数据库使用 wwwroot，训练环境和产物使用 train。
# 这两个目录应挂载可写卷，保留容器重建前的数据。
RUN mkdir -p /app/wwwroot /app/train \
    && chown -R $APP_UID:$APP_UID /app/wwwroot /app/train
USER $APP_UID

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080'

ENTRYPOINT ["dotnet", "Snet.Yolo.Tasks.dll"]
