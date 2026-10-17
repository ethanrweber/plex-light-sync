# Changelog

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
