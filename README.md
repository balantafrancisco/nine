# Analizador de corners (MVP)

Guarda historial de partidos y cuotas en BD, y calcula por partido: aciertos históricos, % implícito, ventaja y EV.

## Requisitos
- .NET SDK 8 o superior  (`dotnet --version`)
- Node.js 18 o superior   (`node --version`)

## Ejecutar
Terminal 1 (API, http://localhost:5080):
    cd backend/CornersApi
    dotnet run

Terminal 2 (React, http://localhost:5173):
    cd frontend
    npm install
    npm run dev

Abre http://localhost:5173. La primera vez la API crea `corners.db` (SQLite) con los datos de Toulouse vs Estrasburgo y sus cuotas.

## Tests (deben reproducir los números del análisis manual)
    cd backend/CornersApi.Tests
    dotnet test

## Usar SQL Server en vez de SQLite
En `backend/CornersApi/appsettings.json`:
    "Database": { "Provider": "SqlServer" },
    "ConnectionStrings": { "Default": "Server=localhost;Database=CornersDb;Trusted_Connection=True;TrustServerCertificate=True" }
La BD y las tablas se crean solas al iniciar (EnsureCreated).

## Si solo tienes .NET 10
Cambia `<TargetFramework>net8.0</TargetFramework>` a `net10.0` en ambos .csproj.
