# Changelog

## 0.3.1

- Fix: when playback stopped while another Plex app on the same device still had a paused session, the lights kept the movie color, and did not get their old colors back. The app now stays on the session that it follows, changes to another session on the device only when that one plays, and takes the end of the followed session as a stop.
- Fix: a short address such as "192.168.1" passed the settings check, and the app followed 192.168.0.1. Now an IPv4 address must have four parts.
- The log shows a player again when it appears at a new address.

## 0.3.0

- After the update, the app still follows "Plex player ID". To follow the device, set "Player address", and clear "Plex player ID".
- New setting "Player address": the IP address of the device to follow, for example the TV. Every Plex app on the device counts, so a change of app on the TV, or a reinstall of an app, needs no change in the settings. Give the device a fixed address on your router.
- "Plex player ID" is now optional. Set the player address, the player ID, or both.
- The log line for each new player shows its address: "Plex player seen: <name> at <address> (<ID>)".

## 0.2.0

- The lights follow scenes, not single frames. The frames around each moment vote on the color, and a new color must last about 3 s ("Window") before the lights follow it, so flashes and quick cuts do not reach the lights. The app reads ahead, so a change still lands on its scene cut, with a 2 s fade centered on the cut.
- Videos up to 1080p give the vote two frames per second (before: only the keyframes, every 4–10 s). 4K videos still give only their keyframes: they take too much CPU to decode. If a 1080p video decodes too slowly, the app falls back to keyframes for that video.
- New setting "Window". "Minimum gap" can now go down to 0.5 s.
- Fix: the drift log line printed "-+0.0 s" for a drift that rounds to zero.

## 0.1.1

- Fix: the app stopped at once with "Access to the path '/data/options.json' is denied". Home Assistant lets only root read the settings, so the container now starts as root, copies them for the service user, and runs the service as that non-root user.

## 0.1.0

- First release: the lights of a Zigbee2MQTT group follow the colors of the movie or episode that plays on one Plex player.
- A "Movie sync" switch and a "Status" sensor in Home Assistant.
- HDR files get a tone map.
