# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Single-source copy so adding a new plugin does not require a Dockerfile edit.
# Trades off the "restore-before-source-copy" layer-cache trick (which required
# listing every csproj explicitly) for fewer maintenance touchpoints; plugin
# additions are rare and the cost of a full restore on source-only changes is
# small at our scale.
COPY . .
RUN dotnet restore host/KnockBox/KnockBox.csproj

WORKDIR /src/host/KnockBox
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
EXPOSE 8081

COPY --from=build /app/publish .
RUN mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app

# Persisted state: admin settings, rolling Serilog logs, per-plugin storage,
# and operator-installed third-party plugins all live under /app/data. The
# VOLUME directive ensures Docker creates an anonymous volume by default if
# the operator runs the image without `-v`, so data survives container
# recreation. Production deployments should mount a named volume or bind
# mount; see README.md.
ENV KNOCKBOX_DATA_ROOT=/app/data
VOLUME ["/app/data"]

USER $APP_UID
ENTRYPOINT ["dotnet", "KnockBox.dll"]