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

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
# Skip diagnostic IPC server startup. Remove this line if you need to attach
# dotnet-counters / dotnet-dump to a running container.
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080
EXPOSE 8081

# Chiseled image has no shell, so we can't `RUN mkdir/chown` here. COPY --chown
# applies ownership during copy; /app/data is created by the VOLUME directive
# at container start and inherits the runtime user.
COPY --chown=$APP_UID:$APP_UID --from=build /app/publish .

# Persisted state: admin settings, rolling Serilog logs, per-plugin storage,
# and operator-installed third-party plugins all live under /app/data. The
# VOLUME directive marks the path as a mount point so orchestrators
# (Compose, Kubernetes, TrueNAS, ECS, …) treat it as external state, and so
# layer writes to /app/data don't bloat the image. It is NOT a persistence
# guarantee: a bare `docker run` attaches an anonymous volume that is
# orphaned the moment the container is removed. Always mount /app/data to
# a named volume or host path -- see README.md for named-volume and
# bind-mount recipes (the bind-mount section covers TrueNAS Custom App and
# Kubernetes-style deployments where the orchestrator recreates the
# container on every image update).
ENV KNOCKBOX_DATA_ROOT=/app/data
VOLUME ["/app/data"]

USER $APP_UID
ENTRYPOINT ["dotnet", "KnockBox.dll"]