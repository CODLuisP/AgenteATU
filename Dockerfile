# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AgenteATU/AgenteATU.csproj AgenteATU/
RUN dotnet restore AgenteATU/AgenteATU.csproj

COPY AgenteATU/ AgenteATU/
RUN dotnet publish AgenteATU/AgenteATU.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
ENV TZ=America/Lima
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# appsettings.json se monta en /app/appsettings.json (ver docker-compose.yml)
# Usuario no-root que ya trae la imagen oficial .NET 8+
USER app

ENTRYPOINT ["dotnet", "AgenteATU.dll"]
