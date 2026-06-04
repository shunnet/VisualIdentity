# See https://aka.ms/customizecontainer for customizing debug containers

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Snet.Yolo.Api.Cuda/Snet.Yolo.Api.Cuda.csproj", "Snet.Yolo.Api/"]
COPY ["Snet.Yolo.Server/Snet.Yolo.Server.csproj", "Snet.Yolo.Server/"]
RUN dotnet restore "./Snet.Yolo.Api.Cuda/Snet.Yolo.Api.Cuda.csproj"
COPY . .
WORKDIR "/src/Snet.Yolo.Api.Cuda"
RUN dotnet build "./Snet.Yolo.Api.Cuda.csproj" -c $BUILD_CONFIGURATION -o /app/build


FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Snet.Yolo.Api.Cuda.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false


FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Install SkiaSharp runtime dependencies in final stage
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

ENTRYPOINT ["dotnet", "Snet.Yolo.Api.Cuda.dll"]
