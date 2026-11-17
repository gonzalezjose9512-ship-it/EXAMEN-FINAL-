# =============================================================================
# Imagen del servicio Inventario. Build multi-etapa:
#   1) "build": compila y publica con el SDK completo de .NET 10
#   2) "final": solo el runtime de ASP.NET Core (imagen mucho más chica)
# Se construye desde la raíz del repo (contexto ".") porque usa TiendaNorte.Contracts.
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo los .csproj y el restore (capa reutilizable)
COPY src/TiendaNorte.Contracts/TiendaNorte.Contracts.csproj src/TiendaNorte.Contracts/
COPY src/Inventario.Service/Inventario.Service.csproj src/Inventario.Service/
RUN dotnet restore src/Inventario.Service/Inventario.Service.csproj

# Después el código y la publicación
COPY src/TiendaNorte.Contracts/ src/TiendaNorte.Contracts/
COPY src/Inventario.Service/ src/Inventario.Service/
RUN dotnet publish src/Inventario.Service/Inventario.Service.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Inventario.Service.dll"]
