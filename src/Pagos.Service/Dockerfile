# =============================================================================
# Imagen del servicio Pagos. Build multi-etapa:
#   1) "build": compila y publica con el SDK completo de .NET 10
#   2) "final": solo el runtime de ASP.NET Core (imagen mucho más chica)
# Se construye desde la raíz del repo (contexto ".") porque usa TiendaNorte.Contracts.
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo los .csproj y el restore (capa reutilizable)
COPY src/TiendaNorte.Contracts/TiendaNorte.Contracts.csproj src/TiendaNorte.Contracts/
COPY src/Pagos.Service/Pagos.Service.csproj src/Pagos.Service/
RUN dotnet restore src/Pagos.Service/Pagos.Service.csproj

# Después el código y la publicación
COPY src/TiendaNorte.Contracts/ src/TiendaNorte.Contracts/
COPY src/Pagos.Service/ src/Pagos.Service/
RUN dotnet publish src/Pagos.Service/Pagos.Service.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pagos.Service.dll"]
