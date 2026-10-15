# plex-light-sync

makes hue bulbs follow the colors of the video that plays in plex.

when a movie or an episode plays on a chosen plex player, the service reads the video file from plex with ffmpeg, picks a color from each keyframe, and sends the color to a zigbee2mqtt light group at the moment that the frame plays. home assistant gets a "Movie sync" switch through mqtt discovery.

status: planning. the design is in [docs/plan.md](docs/plan.md).
