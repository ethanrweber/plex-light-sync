# plex-light-sync

makes hue bulbs follow the colors of the video that plays in plex.

when a movie or an episode plays on a chosen plex player, the service reads the video file from plex with ffmpeg, picks a color from each keyframe, and sends the color to a zigbee2mqtt light group at the moment that the frame plays. home assistant gets a "Movie sync" switch through mqtt discovery.

status: v1 prototype in progress. the whole sync works in tests; the first run with real lights is next. the design is in [docs/plan.md](docs/plan.md).

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

in home assistant, the service shows up as the device "Plex light sync" (settings > devices & services > mqtt), with a "Movie sync" switch and a "Status" sensor. when the service stops, both show as unavailable.

when something plays on the followed player, the service logs each event: start, pause, resume, skip, and stop. every 10 s, the tv reports its position, and the service logs how far its own estimate was off. values within ±1 s are normal.

to see each color that the service sends, turn on debug logging for the sync. the setting name has a dot, which a bash variable cannot have, so pass it with `env`:

```
env 'Logging__LogLevel__PlexLightSync.Sync=Debug' dotnet run --project src/PlexLightSync
```

to stop the sync and get the old colors back at once, turn "Movie sync" off.

## probe: see the colors without lights

the probe prints the color that the service picks for each keyframe of a plex item, with a color swatch in the terminal. use it to tune the color settings.

```
dotnet run --project src/PlexLightSync -- probe <ratingKey> [start h:mm:ss] [frames]
dotnet run --project src/PlexLightSync -- probe 12345 0:40:00 20
```

the rating key is in the service log ("Started 12345 ..."), and in the plex web address of the item (`key=/library/metadata/12345`). a frame without a color shows `hold (Black)` or `hold (NoColor)`.

the video tests run ffmpeg on short clips that they generate, so `dotnet test` needs ffmpeg. without it, those tests skip.
