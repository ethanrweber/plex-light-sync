# syntax=docker/dockerfile:1

# build: restore first (a layer that changes only with the packages), then publish
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-alpine3.24 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PlexLightSync/PlexLightSync.csproj src/PlexLightSync/
RUN dotnet restore src/PlexLightSync/PlexLightSync.csproj
COPY src/ src/
RUN dotnet publish src/PlexLightSync/PlexLightSync.csproj --configuration Release --no-restore --output /app

# run: the .NET runtime, plus ffmpeg from alpine (with zscale and tonemap for hdr files). tzdata lets
# the log use local time when TZ is set, as home assistant does for its apps.
FROM mcr.microsoft.com/dotnet/runtime:10.0.13-alpine3.24
RUN apk add --no-cache ffmpeg tzdata su-exec
WORKDIR /app
COPY --from=build /app .

# the container starts as root, but only to copy the home assistant app settings: the service itself
# runs as "app", the non-root user of the .NET images. see entrypoint.sh.
COPY --chmod=755 entrypoint.sh /usr/local/bin/entrypoint.sh

# the sync engine touches this file every 10 s; older than a minute means that the loop is stuck
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s \
  CMD test -n "$(find /tmp/plex-light-sync.alive -mmin -1 2>/dev/null)"

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
