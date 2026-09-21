# syntax=docker/dockerfile:1.7
ARG SDK_VERSION=10.0.401
ARG ASPNET_VERSION=10.0.12
ARG CUDA_IMAGE=nvidia/cuda:12.6.3-cudnn-runtime-ubuntu22.04
ARG PROJECT_NAME=Snet.Yolo.Api.Cuda

FROM mcr.microsoft.com/dotnet/sdk:${SDK_VERSION} AS build
ARG PROJECT_NAME
WORKDIR /src

COPY Snet.Yolo.Server/Snet.Yolo.Server.csproj Snet.Yolo.Server/
COPY YoloDotNet/YoloDotNet.csproj YoloDotNet/
COPY YoloDotNet.ExecutionProvider.Cuda/YoloDotNet.ExecutionProvider.Cuda.csproj YoloDotNet.ExecutionProvider.Cuda/
COPY Snet.Yolo.Api.Shared/Snet.Yolo.Api.Shared.projitems Snet.Yolo.Api.Shared/
COPY ${PROJECT_NAME}/${PROJECT_NAME}.csproj ${PROJECT_NAME}/
COPY appsettings.json appsettings.Development.json ./
RUN dotnet restore "${PROJECT_NAME}/${PROJECT_NAME}.csproj" --runtime linux-x64

COPY . .
RUN dotnet publish "${PROJECT_NAME}/${PROJECT_NAME}.csproj" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${ASPNET_VERSION} AS dotnet-runtime
FROM ${CUDA_IMAGE} AS final
WORKDIR /app

COPY --from=dotnet-runtime /usr/share/dotnet /usr/share/dotnet
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libfontconfig1 \
        libfreetype6 \
        libgomp1 \
        libgssapi-krb5-2 \
        libicu70 \
        libnuma1 \
        libssl3 \
        libx11-6 \
        libxcb1 \
        libxrandr2 \
        libxrender1 \
        zlib1g \
    && groupadd --gid 1654 app \
    && useradd --uid 1654 --gid 1654 --create-home app \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
RUN mkdir -p /app/wwwroot \
    && chown -R 1654:1654 /app/wwwroot

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_ROOT=/usr/share/dotnet \
    PATH="${PATH}:/usr/share/dotnet"
VOLUME ["/app/wwwroot"]
USER 1654
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080'
ENTRYPOINT ["dotnet", "Snet.Yolo.Api.Cuda.dll"]
