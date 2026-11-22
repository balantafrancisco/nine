# 1. Etapa de compilación
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CornersApi.csproj", "./"]
RUN dotnet restore "CornersApi.csproj"
COPY . .
RUN dotnet publish "CornersApi.csproj" -c Release -o /app/publish

# 2. Etapa final: Imagen de Playwright 1.64.1 basada en Ubuntu 22.04 (Jammy) que incluye .NET 8
FROM mcr.microsoft.com/playwright/dotnet:v1.64.1-jammy AS final
WORKDIR /app
COPY --from=build /app/publish .

# 3. CLAVE: Decirle a Playwright dónde están los navegadores preinstalados en esta imagen
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

# 4. Configuración del puerto para Render
ENV ASPNETCORE_URLS=http://+:10000
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000

ENTRYPOINT ["dotnet", "CornersApi.dll"]