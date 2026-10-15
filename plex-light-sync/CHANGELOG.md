# Changelog

## 0.1.1

- Fix: the app stopped at once with "Access to the path '/data/options.json' is denied". Home Assistant lets only root read the settings, so the container now starts as root, copies them for the service user, and runs the service as that non-root user.

## 0.1.0

- First release: the lights of a Zigbee2MQTT group follow the colors of the movie or episode that plays on one Plex player.
- A "Movie sync" switch and a "Status" sensor in Home Assistant.
- HDR files get a tone map.
