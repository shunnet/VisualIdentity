# syntax=docker/dockerfile:1.7
ARG SDK_VERSION=10.0.401
ARG ASPNET_VERSION=10.0.12
ARG CUDA_IMAGE=nvidia/cuda:13.0.3-cudnn-runtime-ubuntu22.04
ARG PROJECT_NAME=Snet.Yolo.Api
ARG TARGETARCH

FROM mcr.microsoft.com/dotnet/sdk:${SDK_VERSION} AS build
ARG PROJECT_NAME
ARG TARGETARCH
WORKDIR /src
COPY Snet.Yolo.Server/Snet.Yolo.Server.csproj Snet.Yolo.Server/
COPY YoloDotNet/YoloDotNet.csproj YoloDotNet/
COPY Snet.Yolo.Api.Shared/Snet.Yolo.Api.Shared.projitems Snet.Yolo.Api.Shared/
COPY ${PROJECT_NAME}/${PROJECT_NAME}.csproj ${PROJECT_NAME}/
COPY appsettings.json appsettings.Development.json ./
RUN RID="linux-${TARGETARCH}"; if [ "$TARGETARCH" = "amd64" ]; then RID="linux-x64"; fi \
    && dotnet restore "${PROJECT_NAME}/${PROJECT_NAME}.csproj" --runtime "$RID"
COPY . .
RUN RID="linux-${TARGETARCH}"; if [ "$TARGETARCH" = "amd64" ]; then RID="linux-x64"; fi \
    && dotnet publish "${PROJECT_NAME}/${PROJECT_NAME}.csproj" \
    --configuration Release --runtime "$RID" --self-contained false --no-restore \
    --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${ASPNET_VERSION} AS dotnet-runtime
FROM ${CUDA_IMAGE} AS runtime-amd64
COPY --from=dotnet-runtime /usr/share/dotnet /usr/share/dotnet
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 libicu70 libssl3 zlib1g \
    && groupadd --gid 1654 app \
    && useradd --uid 1654 --gid 1654 --create-home app \
    && rm -rf /var/lib/apt/lists/*
ENV DOTNET_ROOT=/usr/share/dotnet PATH="${PATH}:/usr/share/dotnet"

FROM dotnet-runtime AS runtime-arm64
# 此平台只有 CPU 原生资产，部署时显式覆盖 API 的 GPU 默认值。
ENV OnnxRuntime__Hardware=Cpu

FROM runtime-${TARGETARCH} AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libfontconfig1 libfreetype6 libgomp1 libnuma1 libx11-6 libxcb1 libxrandr2 libxrender1 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
RUN mkdir -p /app/wwwroot && chown -R 1654:1654 /app/wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    NVIDIA_VISIBLE_DEVICES=all \
    NVIDIA_DRIVER_CAPABILITIES=compute,utility
VOLUME ["/app/wwwroot"]
USER 1654
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080'
ENTRYPOINT ["dotnet", "Snet.Yolo.Api.dll"]
