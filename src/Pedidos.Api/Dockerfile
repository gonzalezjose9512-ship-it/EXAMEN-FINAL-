# =============================================================================
# Imagen de la API de Pedidos. Build multi-etapa:
#   1) "build": compila y publica con el SDK completo de .NET 10
#   2) "final": solo el runtime de ASP.NET Core (imagen mucho más chica)
# Se construye desde la raíz del repo (contexto ".") porque usa varios proyectos.
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo los .csproj y el restore: si no cambian las dependencias,
# Docker reutiliza esta capa y los builds siguientes son más rápidos
COPY src/TiendaNorte.Contracts/TiendaNorte.Contracts.csproj src/TiendaNorte.Contracts/
COPY src/Pedidos.Domain/Pedidos.Domain.csproj src/Pedidos.Domain/
COPY src/Pedidos.Application/Pedidos.Application.csproj src/Pedidos.Application/
COPY src/Pedidos.Infrastructure/Pedidos.Infrastructure.csproj src/Pedidos.Infrastructure/
COPY src/Pedidos.Api/Pedidos.Api.csproj src/Pedidos.Api/
RUN dotnet restore src/Pedidos.Api/Pedidos.Api.csproj

# Después el código y la publicación
COPY src/ src/
RUN dotnet publish src/Pedidos.Api/Pedidos.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# La imagen de .NET escucha en el puerto 8080 y corre con un usuario sin privilegios
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pedidos.Api.dll"]
