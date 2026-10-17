#:package MQTTnet
// how fast can zigbee2mqtt change the bulbs of the group? sends colors at fixed rates, first as one command
// per bulb (unicast, acked), then as one group command (a broadcast, not acked), and prints what arrived.
//
// usage: dotnet run tools/zigbee-bench.cs -- <the .env of plex-light-sync> [all|unicast|group]
// before: turn the bulbs on, and make sure that nothing plays in plex. the bulbs flash for ~3–5 minutes.
// after: the bulbs get their colors back, also after ctrl+c.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using MQTTnet;

var env = File.ReadLines(args[0])
    .Select(line => line.Trim())
    .Where(line => line.Length > 0 && line[0] != '#' && line.Contains('='))
    .Select(line => line.Split('=', 2))
    .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim());
string Setting(string key, string fallback = "") => env.TryGetValue(key, out var value) && value != "" ? value : fallback;
var mode = args.Length > 1 ? args[1] : "all";

var baseTopic = Setting("ZIGBEE2MQTT_TOPIC", "zigbee2mqtt");
var group = Setting("ZIGBEE2MQTT_GROUP");
var clock = Stopwatch.StartNew();
var states = new ConcurrentQueue<(TimeSpan At, string Bulb, JsonNode State)>();
var errors = new ConcurrentQueue<(TimeSpan At, string Message)>();
string? groupsJson = null, devicesJson = null;

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

using var client = new MqttClientFactory().CreateMqttClient();
client.ApplicationMessageReceivedAsync += message =>
{
    var topic = message.ApplicationMessage.Topic;
    var payload = message.ApplicationMessage.ConvertPayloadToString() ?? "";
    JsonNode? json = null;
    try { json = JsonNode.Parse(payload); } catch (System.Text.Json.JsonException) { }

    if (topic == $"{baseTopic}/bridge/groups")
        groupsJson = payload;
    else if (topic == $"{baseTopic}/bridge/devices")
        devicesJson = payload;
    else if (topic == $"{baseTopic}/bridge/logging")
    {
        if (json?["level"]?.GetValue<string>() is "error" or "warning")
            errors.Enqueue((clock.Elapsed, json["message"]?.GetValue<string>() ?? payload));
    }
    else if (json is JsonObject)
        states.Enqueue((clock.Elapsed, topic[(baseTopic.Length + 1)..], json));
    return Task.CompletedTask;
};

var connect = new MqttClientOptionsBuilder()
    .WithTcpServer(Setting("MQTT_HOST"), int.Parse(Setting("MQTT_PORT", "1883"), CultureInfo.InvariantCulture))
    .WithClientId("zigbee-bench")   // not "plex-light-sync": the same id would kick the app off the broker
    .WithCleanSession();
if (Setting("MQTT_USER") is { Length: > 0 } user)
    connect.WithCredentials(user, Setting("MQTT_PASSWORD"));
await client.ConnectAsync(connect.Build());
await client.SubscribeAsync($"{baseTopic}/bridge/groups");
await client.SubscribeAsync($"{baseTopic}/bridge/devices");
await client.SubscribeAsync($"{baseTopic}/bridge/logging");

// both lists are retained, so they arrive at once
for (var i = 0; i < 50 && (groupsJson is null || devicesJson is null); i++)
    await Task.Delay(100);
var names = (JsonNode.Parse(devicesJson!)?.AsArray() ?? [])
    .ToDictionary(device => device!["ieee_address"]!.GetValue<string>(), device => device!["friendly_name"]!.GetValue<string>());
var members = (JsonNode.Parse(groupsJson!)?.AsArray() ?? [])
    .First(g => g?["friendly_name"]?.GetValue<string>() == group)!["members"]!.AsArray();
string[] bulbs = [.. members.Select(member => names[member!["ieee_address"]!.GetValue<string>()]).Distinct()];
foreach (var bulb in bulbs)
    await client.SubscribeAsync($"{baseTopic}/{bulb}");
Console.WriteLine($"group '{group}': {string.Join(", ", bulbs)}");

var saved = await ReadStatesAsync();
if (!saved.Values.Any(state => state["state"]?.GetValue<string>() == "ON"))
{
    Console.WriteLine("no bulb of the group is on. turn the bulbs on first.");
    return 1;
}

try
{
    if (mode is "all" or "unicast")
    {
        foreach (var (interval, offset) in new[] { (1.0, 11), (0.5, 22), (0.25, 33), (0.1, 44) })
        {
            Console.WriteLine(await UnicastAsync(interval, count: 30, offset));
            await Task.Delay(TimeSpan.FromSeconds(10), stop.Token);
        }
    }
    if (mode is "all" or "group")
    {
        (double Interval, int Offset)[] groupRuns = [(1.0, 55), (0.5, 66)];
        const int groupCount = 60;
        var hues = groupRuns.SelectMany(run => CheckedSteps(groupCount).SelectMany(i => new[] { StepHue(run.Offset, i), StepHue(run.Offset, i - 1) }));
        var colors = await CalibrateAsync(hues);
        foreach (var (interval, offset) in groupRuns)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stop.Token);   // lets the broadcast tables empty
            Console.WriteLine(await GroupAsync(interval, groupCount, offset, colors));
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("stopped");
}
finally
{
    await RestoreAsync();
}
return 0;

// 37 and 360 share no factor, so no hue repeats within one run
static int StepHue(int offset, int step) => ((offset + step * 37) % 360 + 360) % 360;

// the group runs read the bulbs back after every 3rd command
static IEnumerable<int> CheckedSteps(int count) => Enumerable.Range(0, count).Where(i => i % 3 == 2);

// one command to each bulb, every interval. zigbee2mqtt publishes the new state when the bulb acks.
async Task<string> UnicastAsync(double interval, int count, int offset)
{
    var start = clock.Elapsed;
    var errorsBefore = errors.Count;
    var sent = new Dictionary<(string Bulb, int Hue), TimeSpan>();
    for (var i = 0; i < count; i++)
    {
        var hue = StepHue(offset, i);
        await WaitUntilAsync(start + TimeSpan.FromSeconds(i * interval));
        foreach (var bulb in bulbs)
        {
            sent[(bulb, hue)] = clock.Elapsed;
            await PublishAsync($"{baseTopic}/{bulb}/set", ColorPayload(hue, interval));
        }
    }
    await Task.Delay(TimeSpan.FromSeconds(5), stop.Token);

    var total = sent.Count;
    var latencies = new List<double>();
    foreach (var (at, bulb, state) in states.Where(s => s.At >= start))
    {
        // zigbee2mqtt's own state message after the ack carries the hue that it sent
        if (state["color"]?["hue"] is JsonValue value && value.TryGetValue<double>(out var hue)
            && sent.Remove((bulb, (int)Math.Round(hue)), out var sentAt))
            latencies.Add((at - sentAt).TotalMilliseconds);
    }
    latencies.Sort();
    var line = $"unicast every {interval:0.00} s: {total} sent, {latencies.Count} acked, {sent.Count} lost";
    if (latencies.Count > 0)
        line += $", latency p50 {Percentile(latencies, 0.5):0} ms, p95 {Percentile(latencies, 0.95):0} ms, max {latencies[^1]:0} ms";
    return line + ErrorSummary(errorsBefore);
}

// a bulb turns a hue into an xy color with its own color wheel, not zigbee2mqtt's. so first set each hue
// that a group run will check with acked unicasts, and record the xy that each bulb reports for it.
async Task<Dictionary<(string Bulb, int Hue), (double X, double Y)>> CalibrateAsync(IEnumerable<int> hues)
{
    var distinct = hues.Distinct().ToList();
    Console.WriteLine($"calibrating {distinct.Count} hues (~{distinct.Count * 0.6:0} s)");
    var colors = new Dictionary<(string Bulb, int Hue), (double X, double Y)>();
    foreach (var hue in distinct)
    {
        foreach (var bulb in bulbs)
            await PublishAsync($"{baseTopic}/{bulb}/set", ColorPayload(hue, 0));
        await Task.Delay(300, stop.Token);
        var read = await ReadColorsAsync(TimeSpan.FromMilliseconds(300));
        foreach (var (bulb, xy) in read)
            colors[(bulb, hue)] = xy;
    }
    return colors;
}

// one group command every interval. a broadcast gets no ack, so every 3rd step reads the bulbs back, and
// checks whether each bulb shows the new color or still the previous one.
async Task<string> GroupAsync(double interval, int count, int offset, Dictionary<(string Bulb, int Hue), (double X, double Y)> colors)
{
    var start = clock.Elapsed;
    var errorsBefore = errors.Count;
    var readDelay = TimeSpan.FromSeconds(Math.Min(0.25, interval / 2));
    var checks = new List<(TimeSpan ReadAt, TimeSpan NextSendAt, int Hue, int PreviousHue)>();
    var checkedSteps = CheckedSteps(count).ToHashSet();
    for (var i = 0; i < count; i++)
    {
        var sendAt = start + TimeSpan.FromSeconds(i * interval);
        await WaitUntilAsync(sendAt);
        await PublishAsync($"{baseTopic}/{group}/set", ColorPayload(StepHue(offset, i), 0));   // no fade: a read during a fade shows a color between
        if (!checkedSteps.Contains(i))
            continue;
        await WaitUntilAsync(sendAt + readDelay);
        var readAt = clock.Elapsed;
        foreach (var bulb in bulbs)
            await PublishAsync($"{baseTopic}/{bulb}/get", """{"color":""}""");
        checks.Add((readAt, sendAt + TimeSpan.FromSeconds(interval), StepHue(offset, i), StepHue(offset, i - 1)));
    }
    await Task.Delay(TimeSpan.FromSeconds(3), stop.Token);

    int right = 0, wrong = 0, open = 0;
    foreach (var (readAt, nextSendAt, hue, previousHue) in checks)
    {
        foreach (var bulb in bulbs)
        {
            // only a reply before the next send counts
            var reply = FirstColorReply(bulb, readAt, nextSendAt);
            if (reply is not { } read || !colors.TryGetValue((bulb, hue), out var expected) || !colors.TryGetValue((bulb, previousHue), out var previous))
                open++;
            else if (Distance(read, expected) < Distance(read, previous))
                right++;
            else
                wrong++;
        }
    }
    return $"group   every {interval:0.00} s: {count} sent, read back {right} new color, {wrong} old color, {open} unknown"
        + ErrorSummary(errorsBefore);
}

// the bulb's reply to a read has color_mode "xy" and the bulb's own x and y. its "hue" is a stale cached
// value. zigbee2mqtt's own state messages after a set have color_mode "hs", so they never count as a reply.
(double X, double Y)? FirstColorReply(string bulb, TimeSpan from, TimeSpan until)
{
    var reply = states.FirstOrDefault(s => s.Bulb == bulb && s.At >= from && s.At < until
        && s.State["color_mode"]?.GetValue<string>() == "xy");
    return reply.State?["color"] is { } color && color["x"] is JsonValue x && color["y"] is JsonValue y
        ? (x.GetValue<double>(), y.GetValue<double>())
        : null;
}

async Task<Dictionary<string, (double X, double Y)>> ReadColorsAsync(TimeSpan wait)
{
    var readAt = clock.Elapsed;
    foreach (var bulb in bulbs)
        await PublishAsync($"{baseTopic}/{bulb}/get", """{"color":""}""");
    await Task.Delay(wait, stop.Token);
    var colors = new Dictionary<string, (double X, double Y)>();
    foreach (var bulb in bulbs)
    {
        if (FirstColorReply(bulb, readAt, clock.Elapsed) is { } xy)
            colors[bulb] = xy;
    }
    return colors;
}

async Task<Dictionary<string, JsonNode>> ReadStatesAsync()
{
    var askedAt = clock.Elapsed;
    foreach (var bulb in bulbs)
        await PublishAsync($"{baseTopic}/{bulb}/get", """{"state":"","color":"","color_temp":"","color_mode":""}""");
    await Task.Delay(TimeSpan.FromSeconds(3));
    return states.Where(s => s.At >= askedAt).GroupBy(s => s.Bulb).ToDictionary(g => g.Key, g => g.Last().State);
}

// the same rules as the app: never "state", so no bulb turns on or off
async Task RestoreAsync()
{
    foreach (var (bulb, state) in saved)
    {
        JsonObject? payload = state["color_mode"]?.GetValue<string>() switch
        {
            "color_temp" => new() { ["color_temp"] = state["color_temp"]?.DeepClone() },
            "xy" => new() { ["color"] = new JsonObject { ["x"] = state["color"]?["x"]?.DeepClone(), ["y"] = state["color"]?["y"]?.DeepClone() } },
            "hs" => new() { ["color"] = new JsonObject { ["hue"] = state["color"]?["hue"]?.DeepClone(), ["saturation"] = state["color"]?["saturation"]?.DeepClone() } },
            _ => null,
        };
        if (payload is null || state["state"]?.GetValue<string>() != "ON")
            continue;
        payload["transition"] = 1;
        await PublishAsync($"{baseTopic}/{bulb}/set", payload.ToJsonString());
    }
    Console.WriteLine("restored the saved colors");
}

string ErrorSummary(int errorsBefore)
{
    var messages = errors.Skip(errorsBefore).Select(e => e.Message).ToList();
    if (messages.Count == 0)
        return ", 0 zigbee2mqtt errors";
    var examples = messages.Distinct().Take(3).Select(m => $"\n    {m}");
    return $", {messages.Count} zigbee2mqtt errors:{string.Concat(examples)}";
}

static string ColorPayload(int hue, double transition) =>
    string.Create(CultureInfo.InvariantCulture, $$"""{"color":{"hue":{{hue}},"saturation":100},"transition":{{transition}}}""");

static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

static double Percentile(List<double> sorted, double p) => sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];

// no stop token: the restore after ctrl+c must still publish
Task PublishAsync(string topic, string payload) =>
    client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).Build());

async Task WaitUntilAsync(TimeSpan at)
{
    var wait = at - clock.Elapsed;
    if (wait > TimeSpan.Zero)
        await Task.Delay(wait, stop.Token);
}
