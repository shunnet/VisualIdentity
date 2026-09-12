# syntax=docker/dockerfile:1.7
ARG SDK_VERSION=10.0.401
ARG ASPNET_VERSION=10.0.12
ARG PROJECT_NAME=Snet.Yolo.Tasks.Cpu

FROM mcr.microsoft.com/dotnet/sdk:${SDK_VERSION} AS build
ARG PROJECT_NAME
ARG TARGETARCH
WORKDIR /src

COPY Snet.Yolo.Server/Snet.Yolo.Server.csproj Snet.Yolo.Server/
COPY Snet.Yolo.Tasks.Core/Snet.Yolo.Tasks.Core.csproj Snet.Yolo.Tasks.Core/
COPY Snet.Yolo.Tasks.Shared/Snet.Yolo.Tasks.Shared.projitems Snet.Yolo.Tasks.Shared/
COPY ${PROJECT_NAME}/${PROJECT_NAME}.csproj ${PROJECT_NAME}/
RUN RID="linux-${TARGETARCH}" \
    && dotnet restore "${PROJECT_NAME}/${PROJECT_NAME}.csproj" --runtime "$RID"

COPY . .
RUN RID="linux-${TARGETARCH}" \
    && dotnet publish "${PROJECT_NAME}/${PROJECT_NAME}.csproj" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained false \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${ASPNET_VERSION} AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ffmpeg \
        libfontconfig1 \
        libfreetype6 \
        libgl1 \
        libgomp1 \
        libnuma1 \
        libx11-6 \
        libxcb1 \
        libxrandr2 \
        libxrender1 \
        python3 \
        python3-pip \
        python3-venv \
    && ffmpeg -version \
    && ffprobe -version \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
RUN mkdir -p /app/wwwroot/data /app/wwwroot/db /app/train \
    && chown -R "$APP_UID:$APP_UID" /app/wwwroot /app/train

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
VOLUME ["/app/wwwroot/data", "/app/wwwroot/db", "/app/train"]
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080'
ENTRYPOINT ["dotnet", "Snet.Yolo.Tasks.Cpu.dll"]
