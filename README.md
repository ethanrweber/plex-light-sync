# plex-light-sync

makes hue bulbs follow the colors of the video that plays in plex.

when a movie or an episode plays on a chosen plex player, the service reads the video file from plex with ffmpeg, picks a color from each keyframe, and sends the color to a zigbee2mqtt light group at the moment that the frame plays. home assistant gets a "Movie sync" switch through mqtt discovery.

status: v1 prototype in progress. the service follows the playback of a plex player and logs it; the lights are not connected yet. the design is in [docs/plan.md](docs/plan.md).

## development

you need the .NET 10 sdk and ffmpeg.

1. copy `.env.example` to `.env`, and fill it in. `.env` is gitignored.
2. run the tests:
   ```
   dotnet test
   ```
3. run the service. the first line loads `.env` into the shell:
   ```
   set -a; . ./.env; set +a
   dotnet run --project src/PlexLightSync
   ```
   in vs code, the "plex-light-sync" launch configuration loads `.env` for you.

when something plays on the followed player, the service logs each event: start, pause, resume, skip, and stop. every 10 s, the tv reports its position, and the service logs how far its own estimate was off. values within ±1 s are normal.
