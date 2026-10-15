# plex-light-sync: plan

the living room sconces follow the colors of the movie or episode that plays on the living room tv. a small c# service does the work. it connects to home assistant through mqtt.

"the sconces" in this plan means the zigbee2mqtt group of the three hue bulbs. a keyframe is a complete frame that a video file stores every few seconds.

## how it works

when plex reports that a video plays on the living room tv:

1. the service starts ffmpeg at the playback position.
2. ffmpeg reads the file from plex and decodes only the keyframes. it writes each keyframe as a 64×36 frame, with its timestamp. hdr files go through a tone map first.
3. the service picks a vivid color from each frame, and queues the color by timestamp.
4. when the playback clock reaches a timestamp, the service sends that color to the sconces through zigbee2mqtt, with a 2 s fade. it sends a little early (the send offset), to cover the zigbee delay.

the service must schedule by timestamp. the decoder holds each keyframe until the next one arrives, so frames come out up to one keyframe gap late (measured: 1.5–5 s). frames can also come out of order. the service drops any frame that is already in the past, except the newest one: that frame is on screen now, so the service sends it at once. without this, the sconces wait up to 10 s for a color after a start or a seek.

**read rate.** without limits, ffmpeg reads 16–23× faster than playback. the service reads at most 30 s ahead. after that, it pauses its reads, the output pipe fills, and ffmpeg waits. after a fast start, ffmpeg reads at the same rate as the tv.

**playback clock.** the service keeps its own stopwatch of the playback position. it polls `/status/sessions` every 1 s, and the tv reports its real position every 10 s. at each report, the service compares the two:

- if they differ by 3 s or less, the service corrects its stopwatch and continues. small differences are normal: the poll can be up to 1 s late.
- if they differ by more than 3 s, someone skipped (for example, "skip intro" or a rewind). the service restarts ffmpeg at the reported position.
- if the state is `paused` or `buffering`, the stopwatch stops.

**file lookup.** the service takes the part id from the session, and gets the file url from `/library/metadata/<ratingKey>`. in a transcoded session, the media details in the session can describe the transcode, not the file. av1 files always transcode on this tv.

**errors.** if plex, ffmpeg, or mqtt fails, the service sets the status to `error`, holds the current colors, and retries with backoff.

## ffmpeg command

tested on 2026-10-15 with ffmpeg 8.0.1 (alpine) and 6.1.1 (ubuntu 24.04): same frames, same timestamps. stdout carries the frames, and stderr carries one `<index> <seconds>` line per frame. the frame order can differ between ffmpeg versions.

`-threads 1` halves the memory of ffmpeg at the same speed: 230 MB instead of 396–434 MB for a 4k hdr file, and 126 MB instead of 211–249 MB for 1080p. the service decodes only one keyframe every few seconds, so one thread is enough.

```
ffmpeg -nostdin -loglevel error -headers "X-Plex-Token: <token>" \
  -threads 1 -skip_frame nokey -noaccurate_seek -ss <position> -i <plex file url> \
  -map 0:v:0 -vf scale=64:36 -fps_mode passthrough -f rawvideo -pix_fmt rgb24 \
  -stats_mux_pre_fmt "{n} {t}" -stats_mux_pre pipe:2 pipe:1
```

for hdr files (transfer `smpte2084`), replace `scale=64:36` with this tone map. dolby vision files use their hdr10 layer:

```
zscale=w=64:h=36,zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=rgb24
```

## color

1. ignore near-black and near-gray pixels. this also removes letterbox bars.
2. judge a dark frame against its own brightest pixels, so that a night-blue or a red alarm light still shows.
3. the pixels that remain vote by hue, weighted by chroma. the 30° hue window with the most votes wins, and the color is the mean of its pixels with a saturation boost. a vote by hue picks teal or orange; an rgb average of the two is gray.
   the winning window needs at least 1% of the pixels.
4. if no pixel remains (a black or colorless frame), hold the last color.
5. if the new color is almost the same as the current one, do not send it. a new command restarts the fade, so small changes only make the light less smooth.
6. send at most one color every 2 s. if a newer color arrives during the wait, it replaces the color in the queue.

a group command is a zigbee broadcast. a zigbee network can carry about one broadcast per second, and step 6 keeps the service at half of that or less. the 1 s poll goes to plex over the lan, not over zigbee.

## rules

- the service changes the sconces only if "Movie sync" is on and the sconces are already on. it never turns them on.
- it changes only the color. the brightness stays where someone set it.
- when sync starts, it saves the color and the color mode of each bulb. a white bulb (color temperature) goes back to white.
- it keeps the saved colors in a retained mqtt message, so that a restart in the middle of a movie does not lose them.
- on a pause, it holds the current color.
- 30 s after the last playback stops, it restores the saved colors. the delay prevents a flash between autoplayed episodes.
- if someone turns "Movie sync" off, it restores the saved colors immediately.
- a restore fades over 3 s. it changes only the bulbs that are on, and never turns a bulb on or off.
- if someone changes the color during playback, the next keyframe overrides it. to take control, turn "Movie sync" off.
- time-of-day rules belong in home assistant: an automation can turn "Movie sync" on and off.

trigger: movies and episodes from the libraries. trailers, extras, live tv, and music do not trigger it. any plex account that watches on the living room tv triggers it.

## connections

| to | how | for |
|---|---|---|
| plex | poll `/status/sessions` every 1 s | the state, file, and position of the player with the configured machine id |
| plex | ffmpeg, over https | the video file |
| home assistant | mqtt discovery | the "Movie sync" switch, a status sensor (idle, syncing, paused, error), and availability |
| zigbee2mqtt | subscribe `zigbee2mqtt/bridge/groups` and `zigbee2mqtt/bridge/devices` | the bulbs in the group, by friendly name |
| zigbee2mqtt | subscribe `zigbee2mqtt/<bulb>` | the state of each bulb, to save and restore it |
| zigbee2mqtt | publish `zigbee2mqtt/<bulb>/get` | asks a new bulb for its state. zigbee2mqtt does not retain bulb states. |
| zigbee2mqtt | publish `zigbee2mqtt/<group>/set` | the color |

the switch keeps its state across restarts, and is on after the first start. home assistant retains the switch commands, so after a restart the broker gives the service the last command at once. if the service stops, home assistant shows its entities as unavailable.

## design

- **.NET 10** (LTS), regular runtime (not native aot): generic host with a `BackgroundService`, MQTTnet, `HttpClient` + `System.Text.Json`.
- **ffmpeg is a child process**, not a library binding. you can run the same command by hand to debug it.
- **the color step is the seam for v2.** it takes a frame and returns one color per light. v1 returns the same color for every light.
- **settings** come from environment variables or `/data/options.json`. the same build runs on the docker vm and in haos. the keys are flat snake_case: .NET config keys ignore case, so `plex_token` in `options.json` and the `PLEX_TOKEN` environment variable are the same key. for the prototype, a gitignored `.env` holds them.
- **the plex token** is a dedicated token, linked through plex.tv/link. it shows as its own device under authorized devices in plex, so you can revoke it alone. it never goes into a log. bitwarden keeps the copy.
- **upkeep**: dependabot for nuget packages, the docker base image, and github actions. mit license. the readme explains how it works, the settings, and how to debug with the ffmpeg command.
- **tests**: xUnit and `FakeTimeProvider` for the color step and the state machine. the video tests generate short clips with ffmpeg (known colors, known keyframe times, one hdr clip), and run the real pipeline on them. no plex in ci.

settings: plex url, plex token, player machine id, mqtt host and login (prototype only), zigbee2mqtt group, fade, send offset, minimum send gap, color thresholds (`color_black_level`, `color_min_saturation`), saturation boost (`color_saturation_boost`), restore delay, restore fade.

## repo

public github repo `plex-light-sync`. the haos app files live in the same repo.

```
plex-light-sync/
  src/PlexLightSync/
    Plex/        sessions, playback clock
    Video/       ffmpeg process, frame and timestamp reader
    Color/       frame -> color per light
    Lights/      mqtt: discovery entities, zigbee2mqtt commands and state
    Sync/        state machine: start, pause, seek, stop, restore
    Program.cs   host and settings
    Probe.cs     developer tool: prints the colors of a plex item
  tests/PlexLightSync.Tests/
  plex-light-sync/config.yaml   the haos app
  repository.yaml
  Dockerfile
  .github/workflows/ci.yml      test, build the image, push it to ghcr
  README.md
```

## v1: prototype on the docker vm

1. create the repo.
2. install the tools: `sudo apt install dotnet-sdk-10.0 ffmpeg`. (done 2026-10-15: .NET 10.0.112, ffmpeg 6.1.1. the core command works.)
3. link a dedicated plex token. put it in bitwarden. (done 2026-10-15: device `plex-light-sync`, token in `.env`.)
4. during a movie, run the core command by hand. write down the machine id of the tv from `/status/sessions`. (done: the id is in `.env`, from the plex device list. the service logs every player that it sees, which confirms the id during a movie.)
5. write the plex client and the playback clock. make sure that the clock matches the tv. (code done 2026-10-15. to check the clock, play a movie and read the drift lines in the log.)
6. write the video reader and the color step, with tests. (done 2026-10-15. the `probe` command prints the colors of a plex item in the terminal.)
7. give the ha vm a dhcp reservation, so that the service can reach mosquitto at a fixed address. (done 2026-10-15.)
8. create an mqtt login for the service in the mosquitto app. (done 2026-10-15.)
9. write the mqtt part. make sure that the entities show up in home assistant. (code done 2026-10-15: the entities and the bulb states arrive. it does not change the lights yet.)
10. write the sync state machine, with tests for play, pause, seek, stop, and restore. (done 2026-10-15.)
11. run the service during a movie and an hdr movie. include a few dark scenes, an av1 episode (transcoded), and a skip. (first run 2026-10-15: an sdr movie, an hdr movie, skips by fast forward and by chapter, and the restore 30 s after a stop all worked. later the same day: dark scenes, an av1 episode (transcoded), a switch to the next item within 30 s, and "Movie sync" off and on during playback also worked. a bulb that someone turned off stays off.)
12. tune the fade, the send offset, the thresholds, and the saturation boost.
13. measure the memory use of ffmpeg during a 4k movie, to know what the ha vm needs. (done 2026-10-15: the service uses 79 MB, and ffmpeg 230 MB for 4k hdr or 126 MB for 1080p, with `-threads 1`. so a 4k movie needs about 310 MB.)
14. add the dockerfile, ci, and the readme. (done 2026-10-15: an alpine image with ffmpeg 8.1.2, non-root, with a healthcheck; ci runs the tests and publishes to ghcr.io; dependabot.)

## move to a haos app

1. add `repository.yaml` and the app folder with `config.yaml` (`services: mqtt:need`, `hassio_api: true` so that the app can ask the supervisor for the mqtt login, `arch: amd64`, the settings).
2. tag a release. ci pushes the image with that version, and the `version` in `config.yaml` must match it.
3. in home assistant, add the repository, and install the app.
4. stop the prototype on the docker vm.
5. if step 13 of v1 shows that the ha vm needs more memory than its 2 GB, increase it.

## limits

measured on 2026-10-14 and 2026-10-15.

- **keyframes.** the color changes only at a keyframe: typically every 4–6 s, at most every 10 s, and mostly at scene cuts.
- **seeks.** the tv reports its position every 10 s, so the service can find a seek up to 10 s late.
- **plex only.** netflix and youtube do not go through plex.
- **wall switch.** if the wall switch is off, the bulbs have no power, and nothing happens.

## v2 ideas

notes from the first live runs: in dark scenes, a full-brightness color can feel too strong. dimmer sconces (about 30%) look better, and a bulb inside the field of view while watching is better off.

- choose which bulbs sync. until then, a separate zigbee2mqtt group with only those bulbs does the same.
- give each bulb its own color, from its position relative to the tv.
- let the brightness follow the picture, so that dark scenes dim the room.
- replace the 1 s poll with the plex websocket, for instant pause and seek events.
