# escape=`
# syntax=docker/dockerfile:1.7
ARG SDK_VERSION=10.0.401
ARG ASPNET_VERSION=10.0.12
ARG WINDOWS_VERSION=ltsc2022
ARG PROJECT_NAME

FROM mcr.microsoft.com/dotnet/sdk:${SDK_VERSION}-windowsservercore-${WINDOWS_VERSION} AS build
ARG PROJECT_NAME
WORKDIR C:/src

COPY Snet.Yolo.Api.Shared/Snet.Yolo.Api.Shared.projitems Snet.Yolo.Api.Shared/
COPY ${PROJECT_NAME}/${PROJECT_NAME}.csproj ${PROJECT_NAME}/
COPY appsettings.json appsettings.Development.json ./
RUN dotnet restore "${PROJECT_NAME}/${PROJECT_NAME}.csproj" --runtime win-x64

COPY . .
RUN dotnet publish "${PROJECT_NAME}/${PROJECT_NAME}.csproj" `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --no-restore `
    --output C:/app/publish `
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${ASPNET_VERSION}-windowsservercore-${WINDOWS_VERSION} AS final
ARG PROJECT_NAME
WORKDIR C:/app
COPY --from=build C:/app/publish .
RUN powershell -NoLogo -NoProfile -Command "New-Item -ItemType Directory -Force C:/app/wwwroot | Out-Null"

ENV APP_ASSEMBLY=${PROJECT_NAME}.dll `
    ASPNETCORE_HTTP_PORTS=8080 `
    DOTNET_EnableDiagnostics=0
VOLUME ["C:/app/wwwroot"]
USER ContainerUser
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 `
    CMD powershell -NoLogo -NoProfile -Command "if (-not (Test-NetConnection 127.0.0.1 -Port 8080 -InformationLevel Quiet)) { exit 1 }"
ENTRYPOINT ["cmd", "/S", "/C", "dotnet %APP_ASSEMBLY%"]
