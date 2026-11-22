# 1. Etapa de compilación
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CornersApi.csproj", "./"]
RUN dotnet restore "CornersApi.csproj"
COPY . .
RUN dotnet publish "CornersApi.csproj" -c Release -o /app/publish

# 2. Etapa final: Usamos la imagen SDK de .NET 8 para tener PowerShell y el runtime
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# 3. Instalar dependencias de Playwright y el navegador Chromium
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN pwsh playwright.ps1 install --with-deps chromium

# 4. Configuración del puerto para Render
ENV ASPNETCORE_URLS=http://+:10000
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000

ENTRYPOINT ["dotnet", "CornersApi.dll"]