#!/bin/sh
# home assistant starts the app's container as root, and writes the settings form to
# /data/options.json, which only root can read. so this script copies the settings for the service
# user, and then runs the service as that user, never as root. with docker run (no
# /data/options.json), it only drops to the service user.
set -eu

if [ "$(id -u)" = "0" ]; then
    if [ -f /data/options.json ]; then
        install -m 600 -o app -g app /data/options.json /tmp/options.json
        export OPTIONS_FILE=/tmp/options.json
    fi
    exec su-exec app dotnet /app/PlexLightSync.dll "$@"
fi

exec dotnet /app/PlexLightSync.dll "$@"
