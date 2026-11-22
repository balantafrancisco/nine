# 1. Etapa de compilación con .NET 8 SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CornersApi.csproj", "./"]
RUN dotnet restore "CornersApi.csproj"
COPY . .
RUN dotnet publish "CornersApi.csproj" -c Release -o /app/publish

# 2. Etapa final con .NET 8 ASP.NET Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# 3. Instalar dependencias del sistema para Chromium (Playwright)
RUN apt-get update && apt-get install -y \
    libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 \
    libxkbcommon0 libxcomposite1 libxdamage1 libxfixes3 libxrandr2 \
    libgbm1 libasound2 libpango-1.0-0 libcairo2 \
    && rm -rf /var/lib/apt/lists/*

# 4. Copiar la aplicación publicada y instalar el navegador de Playwright
COPY --from=build /app/publish .
RUN pwsh bin/Release/net8.0/playwright.ps1 install --with-deps chromium

ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "CornersApi.dll"]