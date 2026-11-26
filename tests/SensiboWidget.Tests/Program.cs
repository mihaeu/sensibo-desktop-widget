using System.Net;
using System.Text;
using System.Text.Json;
using SensiboWidget.Core;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}

using var sample = JsonDocument.Parse("""
{"status":"success","result":[{"id":"one","room":{"name":"Room"},"productModel":"airq","connectionStatus":{"isAlive":true},"measurements":{"temperature":20.3,"humidity":69.3,"tvoc":0,"co2":967,"time":{"time":"2026-11-24T11:34:57Z"}}},{"id":"two"}]}
""");
var devices = SensiboClient.ParseDevices(sample.RootElement);
Check(devices.Count == 2 && devices[0].Name == "Room" && devices[0].Online == true, "device discovery and online state");
Check(devices[0].Readings.Tvoc == 0 && devices[1].Readings.Tvoc is null, "zero is preserved and missing is unknown");
using var patch = JsonDocument.Parse("""{"humidity":50,"co2":null}""");
var merged = devices[0].Readings.Merge(patch.RootElement);
Check(merged.Temperature == 20.3 && merged.Humidity == 50 && merged.Co2 is null
    && merged.MeasuredAt == devices[0].Readings.MeasuredAt, "partial event merge and explicit null");
using var malformed = JsonDocument.Parse("""{"tvoc":"unknown","time":null}""");
var invalid = merged.Merge(malformed.RootElement);
Check(invalid.Tvoc is null && invalid.MeasuredAt is null, "malformed measurement fields become unknown");

var area = new PixelRect(-2560, -200, 2560, 1400);
var bounds = new PixelRect(-190, 10, 176, 176);
var snapped = WindowPlacement.Snap(area, bounds, 20);
Check(snapped.X == -176 && snapped.Y == 10, "snap right edge on a monitor with negative coordinates");
var saved = WindowPlacement.Capture("DISPLAY2", area, bounds with { X = snapped.X });
var restored = WindowPlacement.Restore(area, 264, 264, saved);
Check(restored.X == -264, "restore edge after DPI or window size changes");
var fallback = WindowPlacement.Restore(new(0, 0, 1920, 1040), 176, 176, saved);
Check(fallback.X == 1744 && fallback.Y >= 0, "restore visibly on a replacement monitor");
var badPosition = WindowPlacement.Restore(area, 176, 176, new("x", double.NaN, double.PositiveInfinity));
Check(badPosition.X == -176 && badPosition.Y == -200, "invalid saved coordinates use safe defaults");
var tiny = WindowPlacement.Snap(new(0, 0, 100, 100), new(-10, -10, 176, 176), 16);
Check(tiny == new PixelPoint(0, 0), "clamp when work area is smaller than widget");

await using var sse = new MemoryStream(Encoding.UTF8.GetBytes(": heartbeat\r\nevent: measurements\r\ndata: {\"id\":\"one\",\r\ndata: \"measurements\":{\"temperature\":21}}\r\n\r\nevent: resync\ndata: {}\n\n"));
var events = new List<ServerEvent>();
await foreach (var item in ServerEvents.ReadAsync(sse, () => TimeSpan.FromSeconds(5), CancellationToken.None)) events.Add(item);
Check(events.Count == 3 && events[0].Name == "heartbeat" && events[2].Name == "resync", "SSE CRLF, heartbeat, and resync framing");
using var eventJson = JsonDocument.Parse(events[1].Data);
Check(eventJson.RootElement.GetProperty("measurements").GetProperty("temperature").GetInt32() == 21, "SSE multiline data");

var requested = false;
using (var client = new SensiboClient("fake-key", new StubHandler(request =>
{
    requested = request.Method == HttpMethod.Get && request.RequestUri!.Host == "home.sensibo.com"
        && !request.RequestUri.Query.Contains("fake-key")
        && request.Headers.GetValues("X-Api-Key").Single() == "fake-key"
        && request.Headers.AcceptEncoding.Any(v => v.Value == "gzip");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sample.RootElement.GetRawText()) };
})))
{
    Check((await client.GetDevicesAsync(CancellationToken.None)).Count == 2 && requested, "GET, header authentication, gzip, and JSON parsing");
}

using (var client = new SensiboClient("fake-key", new StubHandler(_ =>
{
    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
    response.Headers.RetryAfter = new(TimeSpan.FromSeconds(180));
    return response;
})))
{
    try { await client.GetDevicesAsync(CancellationToken.None); throw new Exception("Expected rate limit"); }
    catch (SensiboApiException error) { Check(error.StatusCode == 429 && error.RetryAfter == TimeSpan.FromSeconds(180)
        && !error.ToString().Contains("fake-key"), "rate-limit delay and safe error text"); }
}

var calls = 0;
using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
using (var client = new SensiboClient("fake-key", new StubHandler(request =>
{
    calls++;
    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
})))
{
    MonitorUpdate? update = null;
    await client.MonitorAsync("one", value => update = value, cancel.Token);
    Check(calls == 1 && update?.Connected == false && update.Message.Contains("API key rejected"), "authentication failure stops retries");
}

using var liveCancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
using (var client = new SensiboClient("fake-key", new StubHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
{
    Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/stream")
        ? "event: hello\ndata: {\"heartbeatSeconds\":120}\n\nevent: measurements\ndata: {\"id\":\"other\",\"measurements\":{\"temperature\":99}}\n\nevent: measurements\ndata: {\"id\":\"one\",\"measurements\":{\"temperature\":22}}\n\n"
        : sample.RootElement.GetRawText())
})))
{
    var updates = new List<MonitorUpdate>();
    try
    {
        await client.MonitorAsync("one", value =>
        {
            updates.Add(value);
            if (value.Device?.Readings.Temperature == 22) liveCancel.Cancel();
        }, liveCancel.Token);
    }
    catch (OperationCanceledException) when (liveCancel.IsCancellationRequested) { }
    Check(updates.Any(u => u.Device?.Readings.Temperature == 22 && u.Device.Readings.Humidity == 69.3)
        && updates.All(u => u.Device?.Readings.Temperature != 99), "stream snapshot, selected-device filtering, merge and cancellation");
}
var failedStreamCalls = 0;
using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
using (var client = new SensiboClient("fake-key", new StubHandler(_ =>
{
    failedStreamCalls++;
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
})))
{
    var disconnected = false;
    try { await client.MonitorAsync("one", update => disconnected |= !update.Connected, stop.Token); }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    Check(failedStreamCalls == 1 && disconnected, "empty stream backs off and remains cancellable");
}
var rateCalls = 0;
using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
using (var client = new SensiboClient("fake-key", new StubHandler(_ =>
{
    rateCalls++;
    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
    response.Headers.RetryAfter = new(TimeSpan.FromSeconds(600));
    return response;
})))
{
    try { await client.MonitorAsync("one", _ => { }, stop.Token); }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    Check(rateCalls == 1, "rate-limited stream does not poll or retry before delay");
}
await using (var stalled = new WaitingStream())
{
    var timedOut = false;
    try
    {
        await foreach (var item in ServerEvents.ReadAsync(stalled, () => TimeSpan.FromMilliseconds(50), CancellationToken.None)) { }
    }
    catch (OperationCanceledException) { timedOut = true; }
    Check(timedOut, "missing heartbeat cancels a stalled read");
}
await WeatherChecks.RunAsync(Check);
Console.WriteLine($"All {checks} checks passed.");
if (args.Contains("--weather-live")) await WeatherChecks.LiveAsync();

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
}

sealed class WaitingStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
}
