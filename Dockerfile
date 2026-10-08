# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ImageAnalysis.sln ./
COPY src/ImageAnalysis.API/ImageAnalysis.API.csproj src/ImageAnalysis.API/
COPY src/ImageAnalysis.Application/ImageAnalysis.Application.csproj src/ImageAnalysis.Application/
COPY src/ImageAnalysis.Domain/ImageAnalysis.Domain.csproj src/ImageAnalysis.Domain/
COPY src/ImageAnalysis.Infrastructure/ImageAnalysis.Infrastructure.csproj src/ImageAnalysis.Infrastructure/
RUN dotnet restore src/ImageAnalysis.API/ImageAnalysis.API.csproj

COPY src/ src/
RUN dotnet publish src/ImageAnalysis.API/ImageAnalysis.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    Storage__RootPath=/data/storage
EXPOSE 8080

# Diretório de object storage local (imagens originais, mapas ELA, heatmaps),
# montado como volume pelo docker-compose.
RUN mkdir -p /data/storage && chown -R $APP_UID /data/storage
VOLUME ["/data/storage"]

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "ImageAnalysis.API.dll"]
