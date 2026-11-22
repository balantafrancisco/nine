# 1. Etapa de compilación
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CornersApi.csproj", "./"]
RUN dotnet restore "CornersApi.csproj"
COPY . .
RUN dotnet publish "CornersApi.csproj" -c Release -o /app/publish

# 2. Etapa final: Imagen oficial de Playwright
FROM mcr.microsoft.com/playwright/dotnet:v1.64.1-noble AS final
WORKDIR /app
COPY --from=build /app/publish .

# 3. CLAVE: Forzar la instalación del navegador en una ruta persistente y conocida
# Esto asegura que el ejecutable esté donde la app lo busca y no en un directorio temporal.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN dotnet tool install --global Microsoft.Playwright.CLI && \
    /root/.dotnet/tools/playwright install chromium --with-deps

# 4. Configuración del puerto
ENV ASPNETCORE_URLS=http://+:10000
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000

ENTRYPOINT ["dotnet", "CornersApi.dll"]