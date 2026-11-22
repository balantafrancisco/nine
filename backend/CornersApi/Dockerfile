# 1. Etapa de compilación con .NET 8 SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CornersApi.csproj", "./"]
RUN dotnet restore "CornersApi.csproj"
COPY . .
RUN dotnet publish "CornersApi.csproj" -c Release -o /app/publish

# 2. Etapa final: Imagen de Playwright que ya incluye .NET 8 y Chromium
FROM mcr.microsoft.com/playwright/dotnet:v1.40.0-jammy AS final
WORKDIR /app

# 3. Copiar la aplicación publicada
COPY --from=build /app/publish .

# 4. Configuración del puerto (Render usa 10000 por defecto)
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

# 5. Iniciar la aplicación
ENTRYPOINT ["dotnet", "CornersApi.dll"]