# Usamos la imagen oficial de Playwright para .NET (versión 1.64.1 según tu csproj)
# Si la imagen de la versión exacta falla, puedes usar v1.40.0-jammy
FROM mcr.microsoft.com/playwright/dotnet:v1.64.1-noble

WORKDIR /app

# Copiar el archivo del proyecto y restaurar dependencias
COPY *.csproj ./
RUN dotnet restore

# Copiar el resto de los archivos y compilar en modo Release
COPY . ./
RUN dotnet publish -c Release -o out

# Configurar el puerto (Render/Azure usan 10000 o 8080)
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

# Iniciar la aplicación
WORKDIR /app/out
ENTRYPOINT ["dotnet", "CornersApi.dll"]