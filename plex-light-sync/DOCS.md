# Plex light sync

The app makes a Zigbee2MQTT light group follow the colors of the movie or episode that plays on one Plex player. It changes only the color, never the brightness, and only while the lights are on. It never turns a light on or off.

## Before you start

You need:

- the Mosquitto broker app. This app gets its MQTT login from it.
- Zigbee2MQTT, with a group of the color lights that follow the video.
- a Plex server that this Home Assistant can reach.

## Setup

1. **Link a Plex token for the app.** On any computer with a terminal:
   ```
   CLIENT_ID=$(cat /proc/sys/kernel/random/uuid); echo "$CLIENT_ID"
   curl -s -X POST 'https://plex.tv/api/v2/pins?strong=false' -H 'Accept: application/json' \
     -H 'X-Plex-Product: plex-light-sync' -H "X-Plex-Client-Identifier: $CLIENT_ID"
   ```
   The reply has an `id` and a 4-character `code`. Enter the code at https://plex.tv/link. Then get the token (`authToken`):
   ```
   curl -s "https://plex.tv/api/v2/pins/<id>" -H 'Accept: application/json' -H "X-Plex-Client-Identifier: $CLIENT_ID"
   ```
   The token shows up as the device "plex-light-sync" under Authorized Devices in Plex, so you can revoke it alone.
2. **Find the player ID.** The list of your Plex devices shows it as `clientIdentifier`:
   ```
   curl -s https://plex.tv/devices.xml -H "X-Plex-Token: <token>"
   ```
3. **Fill in the settings** on the Configuration tab: the Plex URL, the token, the client ID (the `CLIENT_ID` from step 1), the player ID, and the Zigbee2MQTT group.
4. **Start the app.** The log shows "MQTT connected" and the bulbs of the group.

## In Home Assistant

The app adds the device "Plex light sync" with two entities:

- **Movie sync**: a switch. Turn it off to stop the sync and get the old colors back at once.
- **Status**: idle, syncing, paused, or error.

## What happens

- When a movie or an episode plays, the lights get a color from each keyframe of the video, with a 2 s fade.
- On a pause, they hold their color.
- 30 s after playback stops, they get back the colors from before the sync. An episode that plays next within those 30 s keeps the sync going.

## Settings

The optional settings tune the timing and the colors. Show them with "Show unused optional configuration options". If the colors feel too strong, lower "Saturation boost", or dim the lights.

## Problems

- **Status "error":** the app cannot get the video from Plex. The log has the reason, and the app tries again every 5 to 60 s.
- **"settings error" in the log:** a setting is missing or wrong. The message names it.

More: https://github.com/ethanrweber/plex-light-sync
