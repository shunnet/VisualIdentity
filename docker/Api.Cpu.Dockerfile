# syntax=docker/dockerfile:1.7
ARG SDK_VERSION=10.0.401
ARG ASPNET_VERSION=10.0.12
ARG PROJECT_NAME=Snet.Yolo.Api.Cpu

FROM mcr.microsoft.com/dotnet/sdk:${SDK_VERSION} AS build
ARG PROJECT_NAME
ARG TARGETARCH
WORKDIR /src

COPY Snet.Yolo.Api.Shared/Snet.Yolo.Api.Shared.projitems Snet.Yolo.Api.Shared/
COPY ${PROJECT_NAME}/${PROJECT_NAME}.csproj ${PROJECT_NAME}/
COPY appsettings.json appsettings.Development.json ./
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
        libfontconfig1 \
        libfreetype6 \
        libgomp1 \
        libnuma1 \
        libx11-6 \
        libxcb1 \
        libxrandr2 \
        libxrender1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
RUN mkdir -p /app/wwwroot \
    && chown -R "$APP_UID:$APP_UID" /app/wwwroot

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
VOLUME ["/app/wwwroot"]
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080'
ENTRYPOINT ["dotnet", "Snet.Yolo.Api.Cpu.dll"]
