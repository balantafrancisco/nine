# 1. Etapa de compilación con .NET 8 SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CornersApi.csproj", "./"]
RUN dotnet restore "CornersApi.csproj"
COPY . .
RUN dotnet publish "CornersApi.csproj" -c Release -o /app/publish

# 2. Etapa final: Imagen de Playwright con la versión correcta
FROM mcr.microsoft.com/playwright/dotnet:v1.64.1-noble AS final
WORKDIR /app
COPY --from=build /app/publish .

# 3. Configuración del puerto
ENV ASPNETCORE_URLS=http://+:10000
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000

ENTRYPOINT ["dotnet", "CornersApi.dll"]