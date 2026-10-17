# plex-light-sync: plan

the living room sconces follow the colors of the movie or episode that plays on the living room tv. a small c# service does the work. it connects to home assistant through mqtt.

"the sconces" in this plan means the zigbee2mqtt group of the three hue bulbs. a keyframe is a complete frame that a video file stores every few seconds.

## how it works

when plex reports that a video plays on the living room tv:

1. the service starts ffmpeg at the playback position.
2. ffmpeg reads the file from plex. for a file up to 1920 pixels wide (1080p), it decodes the frames that other frames refer to, and writes one frame per minimum gap (two per second). for a wider file (4k), it decodes only the keyframes. it writes each frame as 64×36 pixels, with its timestamp. hdr files go through a tone map first.
3. the service picks a vivid color from each frame, and keeps it in a timeline by timestamp.
4. every 100 ms, the frames around a moment vote on the color of the sconces (see "color"). the moment is the playback position plus the send offset (the zigbee delay) plus half the fade, so that the 2 s fade is centered on a scene cut. a new color goes to the sconces through zigbee2mqtt.

the service must work by timestamp. with keyframes only, the decoder holds each keyframe until the next one arrives, so frames come out up to one keyframe gap late (measured: 1.5–5 s). frames can also come out of order. with keyframes only, the newest frame at or before the moment decides: after a start or a seek, that is the frame on screen now, so the color goes out at once. without this, the sconces wait up to 10 s for a color after a start or a seek. with two frames per second, the vote waits after a start or a seek until the frames fill the whole window (a few seconds): a vote on half a window can pick a color that the full window rejects a moment later.

**too slow.** if the frames fall more than 15 s behind the playback, ffmpeg cannot decode the file in real time. then the service reads only the keyframes of that item, also after a seek, and logs a warning. the next item starts with two frames per second again. a normal start lags up to ~10 s, because ffmpeg begins at the keyframe before the start position.

**read rate.** without limits, ffmpeg reads 16–23× faster than playback with keyframes only, and 2.7–4.2× with two frames per second from 1080p. the service reads at most 30 s ahead. after that, it pauses its reads, the output pipe fills, and ffmpeg waits. after a fast start, ffmpeg reads at the same rate as the tv.

**playback clock.** the service keeps its own stopwatch of the playback position. it polls `/status/sessions` every 1 s, and the tv reports its real position every 10 s. at each report, the service compares the two:

- if they differ by 3 s or less, the service corrects its stopwatch and continues. small differences are normal: the poll can be up to 1 s late.
- if they differ by more than 3 s, someone skipped (for example, "skip intro" or a rewind). the service restarts ffmpeg at the reported position.
- if the state is `paused` or `buffering`, the stopwatch stops.

**file lookup.** the service takes the part id from the session, and gets the file url from `/library/metadata/<ratingKey>`. in a transcoded session, the media details in the session can describe the transcode, not the file. av1 files always transcode on this tv.

**errors.** if plex, ffmpeg, or mqtt fails, the service sets the status to `error`, holds the current colors, and retries with backoff.

## ffmpeg command

tested on 2026-10-15 with ffmpeg 8.0.1 (alpine) and 6.1.1 (ubuntu 24.04): same frames, same timestamps. stdout carries the frames, and stderr carries one `<index> <seconds>` line per frame. the frame order can differ between ffmpeg versions.

`-threads 1` halves the memory of ffmpeg at the same speed: 230 MB instead of 396–434 MB for a 4k hdr file, and 126 MB instead of 211–249 MB for 1080p. one thread decodes the keyframes of any file, and two frames per second of a 1080p file, faster than real time.

```
ffmpeg -nostdin -loglevel error -headers "X-Plex-Token: <token>" \
  -threads 1 -skip_frame nokey -noaccurate_seek -ss <position> -i <plex file url> \
  -map 0:v:0 -vf scale=64:36 -fps_mode passthrough -f rawvideo -pix_fmt rgb24 \
  -stats_mux_pre_fmt "{n} {t}" -stats_mux_pre pipe:2 pipe:1
```

for a file up to 1920 pixels wide, replace `-skip_frame nokey` with `-skip_frame noref`, and put `fps=2,` (one frame per minimum gap) at the start of the filter. "noref" skips only the frames that no other frame refers to, so the picture stays intact, and the decode work halves. the fps filter comes first, so that the scale and the tone map run on the kept frames only. the timestamps fall on a 0.5 s grid from the start position, and each grid point takes the nearest decoded frame.

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
5. the frames from 3 s before to 3 s after the moment (the window) vote on the color of the sconces. a frame is one vote, and black or colorless frames do not vote. the 30° hue window with the most frames wins, and the color is the mean of its frames.
6. the current color stays unless the winner is clearly different (more than 20° of hue or 0.25 of saturation) and has at least 1.5 times the votes of the current color. so a color that lasts less than about half the window never wins, and two shots that alternate (a conversation) do not make the sconces alternate. the service reads ahead, so the window sees the future, and a real scene change still lands on its cut.
7. send at most one color every 0.5 s (the minimum gap). with the vote, changes are seconds apart; the gap is a safety limit for zigbee.

a group command is a zigbee broadcast: every router of the network sends it on once. each device keeps a table of recent broadcasts, so that it does not send one on twice. when the table is full, new broadcasts can be lost. the size of the table and how long an entry stays depend on the zigbee stack: 8 entries for 8 s on digi xbee, 15 entries (silicon labs docs) for about 3 s on emberznet, which the zbt-2 coordinator runs. hue bulbs do not publish theirs. so the network decides the safe rate, and `tools/zigbee-bench.cs` measures it (see "v0.2: faster colors"). the 1 s poll goes to plex over the lan, not over zigbee.

## rules

- the service changes the sconces only if "Movie sync" is on and the sconces are already on. it never turns them on.
- it changes only the color. the brightness stays where someone set it.
- when sync starts, it saves the color and the color mode of each bulb. a white bulb (color temperature) goes back to white.
- it keeps the saved colors in a retained mqtt message, so that a restart in the middle of a movie does not lose them.
- on a pause, it holds the current color.
- 30 s after the last playback stops, it restores the saved colors. the delay prevents a flash between autoplayed episodes.
- if someone turns "Movie sync" off, it restores the saved colors immediately.
- a restore fades over 3 s. it changes only the bulbs that are on, and never turns a bulb on or off.
- if someone changes the color during playback, the next color overrides it. to take control, turn "Movie sync" off.
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

1. add `repository.yaml` and the app folder with `config.yaml` (`services: mqtt:need`, `arch: amd64`, the settings). (done 2026-10-15. `hassio_api` is not necessary: the supervisor lets every app call `/services`, if its config.yaml declares the service.)
2. tag a release. ci pushes the image with that version, and the `version` in `config.yaml` must match it.
3. in home assistant, add the repository, and install the app.
4. stop the prototype on the docker vm.
5. if step 13 of v1 shows that the ha vm needs more memory than its 2 GB, increase it.

## limits

measured on 2026-10-14 and 2026-10-15.

- **4k: keyframes.** for a file wider than 1920 pixels, the color changes only at a keyframe: typically every 4–6 s, at most every 10 s (4k files often have a fixed 10 s gap). one core decodes 4k at only 0.6× real time, and the ha vm has 2 cores, which it shares with home assistant.
- **seeks.** the tv reports its position every 10 s, so the service can find a seek up to 10 s late.
- **plex only.** netflix and youtube do not go through plex.
- **wall switch.** if the wall switch is off, the bulbs have no power, and nothing happens.

## v0.2: faster colors

feedback after two days: the sync is fun, but feels like a gimmick. the main reason: the color changed only at keyframes, every 4–10 s, so the sconces followed a scene cut up to 10 s late. the 2 s send gap was rarely the limit. in 30 s of video, keyframes gave 3 colors (la la land, 4k), 4 (an x264 episode), 11 (an x265 movie, an av1 episode), and 39 (a remux with short keyframe gaps: the only file where the 2 s gap mattered).

decisions (2026-10-16, before the live test):

- keep the group command. send at most one color every 0.5 s, with a 0.5 s fade. the lowest allowed minimum gap is 0.5 s: the fastest rate that was tested.
- the frames per second follow the minimum gap (one frame per gap), so the two settings cannot disagree.
- files up to 1920 pixels wide get two frames per second. the width, not the height: a wide 4k movie is 3840×1504, and a wide 1080p movie is 1920×800. an unknown width gets keyframes only.
- wider files (4k) keep keyframes only. the ha vm has 2 cores, and more cores for the vm are fewer for plex.
- keep the rule that skips almost identical colors.
- the engine loop ticks every 100 ms instead of 250 ms, so that a color every 0.5 s keeps its rhythm.

**live test 1: a color per frame is worse.** on the dev run (the killing s01e01, 1080p), the service sent 172 colors in 7 min (about 25 per minute), 90 of them less than 1 s after the one before. two patterns: short outliers (green, 1.5 s of blue, green), and jitter inside a scene (orange 24° ↔ 34° every 1–2 s, each step just over the 8° limit). the user: rapid or clustered changes disturb the flow; low brightness (15%) is less distracting; a 2 s fade felt noticeably better than 0.5 s at every brightness.

**the vote (decisions, 2026-10-16).** keep two frames per second as the input, but send the color of the scene, not of each frame:

- the frames in a window around the moment vote, and the current color keeps the sconces unless it is clearly outvoted (see "color"). an idea from the user ("momentum": the longer a color stays, the slower it gives way) became this rule. momentum looks at the past, so a real scene change after a long scene comes late. the service reads ahead, so it judges a new color by how long it will last, not by how long the old one lasted.
- the fade is 2 s, and centered on the cut: the color goes out 1 s (half the fade) before its moment.
- the window is a setting (`sync_window`, ±3 s). a larger window ignores longer shots, but does not delay real changes.

the window size came from the frames of the killing s01e01, 1:45–13:00 (`probe`, then the same vote in a script):

| window | changes per minute | changes less than 4 s apart | back and forth within 12 s |
|---|---|---|---|
| one color per frame (live test 1) | about 25 | | |
| ±2 s | 5.1 | 20 | 11 |
| ±3 s (the default) | 3.7 | 9 | 3 |
| ±4 s | 2.6 | 3 | 1 |

with ±2 s, conversations still made the sconces alternate (orange 41°, blue 210°, orange 36°, blue 195°, orange 52° in 14 s).

**open: a small vivid object in a muted scene.** the killing s01e01 at ~20:30: a white and beige school hallway, and the sconces turn deep red from a scarf. the picker ignores gray pixels, so the few vivid pixels win (1% is enough), and the saturation boost makes them deeper. the vote does not help: the scarf stays on screen long enough. a later change to the picker can scale the saturation by how colorful the whole frame is.

**decode speed**, 30 s of video at two frames per second, one thread, on the r730 (xeon e5-2683 v3), × real time. to keep up, a file needs more than 1×:

| file | keyframes only | all frames | `noref` (ffmpeg 6.1.1) | `noref` (ffmpeg 8.1.2, the app image) |
|---|---|---|---|---|
| 4k hdr x265 (la la land) | 10.8 | 0.4 | 0.6 | |
| 1080p x265 10-bit (mortal kombat ii) | 29.8 | 2.2 | 3.4 | 4.2 |
| 1080p avc remux (a nightmare on elm street 3) | 13.9 | 1.1 | 2.2 | 2.7 |
| 1080p x264 episode (the killing) | 42.6 | 1.6 | 2.5 | 3.4 |
| 1080p av1 episode (nippon sangoku) | 54.8 | 3.0 | 4.5 | |
| 1080p hdr x265 (uncut gems), with the tone map | | | | 3.3 |

with two threads, 4k decodes at only 0.7×. `-skip_frame bidir` (skip all b-frames) is faster, but it can damage the frames that refer to b-frames. `-skip_loop_filter all` gains only ~15%. ffmpeg uses 68–104 MB for 1080p in this mode.

**zigbee benchmark** (`dotnet run tools/zigbee-bench.cs -- .env`), 2026-10-16, the 3-bulb sconce group, zbt-2 coordinator:

| commands | every | result |
|---|---|---|
| one per bulb (unicast, acked) | 1 / 0.5 / 0.25 / 0.1 s | 90 of 90 acked at each rate, latency p50 56–67 ms, max 109 ms |
| one group command (broadcast) | 1 s | 60 of 60 read-backs show the new color, 0 errors |
| one group command (broadcast) | 0.5 s | 60 of 60 read-backs show the new color, 0 errors |

the bulbs turn a hue into xy with their own color wheel (hue 120 gives x 0.258, y 0.634 on the bulb, but zigbee2mqtt computes 0.172, 0.747). so the group test first records the xy of each hue on each bulb. the "hue" in a bulb's read reply is a stale cached value; only its x and y are real.

the hue entertainment api reaches 25 updates per second, but only through a hue bridge: the bridge packs up to 10 lights into one custom message, sends it to one bulb, and that bulb sends it on as a one-hop broadcast. zigbee2mqtt cannot send that message. parked.

## v2 ideas

notes from the first live runs: in dark scenes, a full-brightness color can feel too strong. dimmer sconces (about 30%) look better, and a bulb inside the field of view while watching is better off.

- choose which bulbs sync. until then, a separate zigbee2mqtt group with only those bulbs does the same.
- give each bulb its own color, from its position relative to the tv.
- let the brightness follow the picture, so that dark scenes dim the room.
- replace the 1 s poll with the plex websocket, for instant pause and seek events.
- 4k with the vote: with keyframes only, the vote has one frame every few seconds, so it smooths much less (user, 2026-10-16). hardware decoding (a gpu for the ha vm) or colors computed ahead of time (for example nightly on the docker vm) would give 4k two frames per second too.
