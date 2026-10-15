# plex-light-sync

makes hue bulbs follow the colors of the video that plays in plex.

when a movie or an episode plays on a chosen plex player, the service reads the video file from plex with ffmpeg, picks a color from each keyframe, and sends the color to a zigbee2mqtt light group at the moment that the frame plays. home assistant gets a "Movie sync" switch through mqtt discovery.

status: v1 works with real lights. next: a home assistant app. the design and its reasons are in [docs/plan.md](docs/plan.md).

## how it works

1. the service polls plex every second, and follows one player.
2. when a movie or an episode plays there, ffmpeg reads the file from plex and decodes only the keyframes. hdr files get a tone map.
3. the service picks one vivid color from each keyframe, and sends it to the light group at the moment that the frame plays, with a 2 s fade.
4. on a pause, the lights hold their color. 30 s after playback stops, they get back the colors from before the sync.

the service changes only the color, never the brightness, and only while the lights are already on. it never turns a light on or off.

## you need

- a plex server, and a player to follow
- an mqtt broker, and zigbee2mqtt with a group of color lights
- home assistant with mqtt discovery (optional: for the switch and the status)

## setup

1. **link a plex token for the service.** it gets its own device under authorized devices in plex, so you can revoke it alone:
   ```
   CLIENT_ID=$(cat /proc/sys/kernel/random/uuid); echo "$CLIENT_ID"
   curl -s -X POST 'https://plex.tv/api/v2/pins?strong=false' -H 'Accept: application/json' \
     -H 'X-Plex-Product: plex-light-sync' -H "X-Plex-Client-Identifier: $CLIENT_ID"
   ```
   the reply has an `id` and a 4-character `code`. enter the code at https://plex.tv/link. then get the token (`authToken`):
   ```
   curl -s "https://plex.tv/api/v2/pins/<id>" -H 'Accept: application/json' -H "X-Plex-Client-Identifier: $CLIENT_ID"
   ```
2. **find the machine id of the player.** start the service with the other settings, and play something on the player. the log shows "Plex player seen: <name> (<machine id>)".
3. **create an mqtt login** for the service on your broker.
4. **copy `.env.example` to `.env`**, and fill it in. `.env` holds secrets: never commit it.

## run with docker

```
docker run -d --name plex-light-sync --restart unless-stopped --env-file .env ghcr.io/ethanrweber/plex-light-sync:main
```

the image runs as a non-root user and has a healthcheck. `main` follows the main branch; a version tag such as `0.1.0` stays fixed.

## settings

environment variables, or the same keys in lowercase in `/data/options.json`.

| key | default | meaning |
|---|---|---|
| `PLEX_URL` | (required) | the plex server, for example `https://192-168-1-2.0123abcd.plex.direct:32400` |
| `PLEX_TOKEN` | (required) | the token of the service (setup step 1) |
| `PLEX_CLIENT_ID` | (required) | the client identifier that linked the token |
| `PLEX_PLAYER_ID` | (required) | the machine id of the player to follow |
| `MQTT_HOST` | (required) | the mqtt broker |
| `MQTT_PORT` | `1883` | |
| `MQTT_USER`, `MQTT_PASSWORD` | (none) | the mqtt login |
| `ZIGBEE2MQTT_GROUP` | (required) | the friendly name of the zigbee2mqtt group of the lights |
| `ZIGBEE2MQTT_TOPIC` | `zigbee2mqtt` | the base topic of zigbee2mqtt |
| `SYNC_FADE` | `2` | seconds of fade to a new color |
| `SYNC_SEND_OFFSET` | `0.3` | seconds to send a color early, for the zigbee delay |
| `SYNC_MIN_GAP` | `2` | shortest time between two colors, in seconds (at least 1: a zigbee network carries about one group command per second) |
| `SYNC_RESTORE_DELAY` | `30` | seconds after playback stops until the old colors come back |
| `SYNC_RESTORE_FADE` | `3` | seconds of fade back to the old colors |
| `COLOR_BLACK_LEVEL` | `0.06` | a frame whose bright pixels stay below this (0–1) counts as black |
| `COLOR_MIN_SATURATION` | `0.2` | pixels with less saturation (0–1) count as gray |
| `COLOR_SATURATION_BOOST` | `1.5` | multiplies the saturation of a color. lower values give softer colors |

## home assistant

the service shows up as the device "Plex light sync" (settings > devices & services > mqtt), with a "Movie sync" switch and a "Status" sensor (idle, syncing, paused, error). when the service stops, both show as unavailable.

turn "Movie sync" off to stop the sync and get the old colors back at once. an automation can turn it on and off, for example by time of day.

## troubleshooting

- **"Movie sync" is unavailable:** the service does not run, or cannot reach the broker. read its log.
- **"the broker refused the connection: NotAuthorized":** the mqtt login is wrong, or the broker did not save it.
- **"settings error: ...":** a setting is missing or wrong. the message names the key.
- **the status is "error":** the service cannot get the file from plex, or ffmpeg fails. the log has the reason, and the service tries again every 5–60 s.
- **colors feel too strong:** lower `COLOR_SATURATION_BOOST`, or dim the lights. the service never changes the brightness.

## development

you need the .NET 10 sdk and ffmpeg.

1. copy `.env.example` to `.env`, and fill it in.
2. run the tests. the video tests run ffmpeg on short clips that they generate; without ffmpeg, those tests skip.
   ```
   dotnet test
   ```
3. run the service. the first line loads `.env` into the shell:
   ```
   set -a; . ./.env; set +a
   dotnet run --project src/PlexLightSync
   ```
   in vs code, the "plex-light-sync" launch configuration loads `.env` for you.

when something plays on the followed player, the service logs each event: start, pause, resume, skip, and stop. every 10 s, the player reports its position, and the service logs how far its own estimate was off. values within ±1 s are normal.

to see each color that the service sends, turn on debug logging for the sync. the setting name has a dot, which a bash variable cannot have, so pass it with `env`:

```
env 'Logging__LogLevel__PlexLightSync.Sync=Debug' dotnet run --project src/PlexLightSync
```

ci (`.github/workflows/ci.yml`) runs the tests on every push, and publishes the image from `main` and from version tags. dependabot proposes package, image, and action updates every week.

## probe: see the colors without lights

the probe prints the color that the service picks for each keyframe of a plex item, with a color swatch in the terminal. use it to tune the color settings.

```
dotnet run --project src/PlexLightSync -- probe <ratingKey> [start h:mm:ss] [frames]
docker run --rm --env-file .env ghcr.io/ethanrweber/plex-light-sync:main probe 12345 0:40:00 20
```

the rating key is in the service log ("Started 12345 ..."), and in the plex web address of the item (`key=/library/metadata/12345`). a frame without a color shows `hold (Black)` or `hold (NoColor)`.
