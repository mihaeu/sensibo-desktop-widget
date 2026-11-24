using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SensiboWidget.Core;

public sealed class SensiboApiException(int statusCode, TimeSpan? retryAfter = null) : Exception("Sensibo request failed.")
{
    public int StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public bool IsAuthenticationFailure => StatusCode is 401 or 403;
}

public sealed class SensiboClient : IDisposable
{
    private readonly HttpClient http;
    private const string Fields = "id,room,productModel,measurements,connectionStatus";

    public SensiboClient(string key, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl))
            throw new ArgumentException("Enter a valid API key.", nameof(key));
        http = new HttpClient(handler ?? new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false
        }) { BaseAddress = new Uri("https://home.sensibo.com/api/v2/"), Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Add("X-Api-Key", key.Trim());
        http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
    }

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await http.GetAsync($"users/me/pods?fields={Fields}", timeout.Token).ConfigureAwait(false);
        CheckResponse(response);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        return ParseDevices(json.RootElement);
    }

    public static IReadOnlyList<Device> ParseDevices(JsonElement root)
    {
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "success"
            || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Unexpected device response.");
        var devices = new List<Device>();
        foreach (var item in result.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var id) || string.IsNullOrWhiteSpace(id.GetString())) continue;
            var name = item.TryGetProperty("room", out var room) && room.ValueKind == JsonValueKind.Object
                && room.TryGetProperty("name", out var roomName) ? roomName.GetString() : null;
            var model = item.TryGetProperty("productModel", out var productModel) ? productModel.GetString() : null;
            var readings = item.TryGetProperty("measurements", out var m) && m.ValueKind == JsonValueKind.Object
                ? new Readings().Merge(m) : new Readings();
            bool? online = item.TryGetProperty("connectionStatus", out var connection)
                && connection.ValueKind == JsonValueKind.Object && connection.TryGetProperty("isAlive", out var alive)
                && alive.ValueKind is JsonValueKind.True or JsonValueKind.False ? alive.GetBoolean() : null;
            devices.Add(new(id.GetString()!, name ?? id.GetString()!, model ?? "unknown", readings, online));
        }
        return devices;
    }

    public async Task MonitorAsync(string deviceId, Action<MonitorUpdate> publish, CancellationToken token)
    {
        Device? device = null;
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            var retry = TimeSpan.FromSeconds(Math.Min(300, 60 * Math.Pow(2, Math.Min(failures, 3))));
            try
            {
                using var opening = CancellationTokenSource.CreateLinkedTokenSource(token);
                opening.CancelAfter(TimeSpan.FromSeconds(30));
                using var request = new HttpRequestMessage(HttpMethod.Get, "stream");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, opening.Token).ConfigureAwait(false);
                CheckResponse(response);
                opening.CancelAfter(Timeout.InfiniteTimeSpan);
                await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var heartbeat = TimeSpan.FromSeconds(240);
                var lastSnapshot = DateTimeOffset.MinValue;
                var resyncRequested = false;
                await foreach (var evt in ServerEvents.ReadAsync(stream, () => heartbeat, token).ConfigureAwait(false))
                {
                    if (evt.Name == "resync") { resyncRequested = true; break; }
                    using var json = evt.Data.Length > 0 ? JsonDocument.Parse(evt.Data) : null;
                    var data = json?.RootElement;
                    if (evt.Name == "hello" && data is { } hello
                        && hello.TryGetProperty("heartbeatSeconds", out var seconds) && seconds.TryGetInt32(out var count))
                        heartbeat = TimeSpan.FromSeconds(Math.Clamp(count * 2L, 30, 600));
                    var matching = data is { } payload && payload.TryGetProperty("id", out var id) && id.GetString() == deviceId;
                    if (evt.Name == "hello" || (evt.Name == "changed" && matching)
                        || DateTimeOffset.UtcNow - lastSnapshot >= TimeSpan.FromMinutes(15))
                    {
                        device = (await GetDevicesAsync(token).ConfigureAwait(false)).FirstOrDefault(d => d.Id == deviceId);
                        lastSnapshot = DateTimeOffset.UtcNow;
                        failures = 0;
                        publish(new(device, true, device is null ? "Device not found. Open Settings." : "Connected"));
                    }
                    if (evt.Name == "measurements" && matching && device is not null
                        && data is { } measurement && measurement.TryGetProperty("measurements", out var values))
                    {
                        device = device with { Readings = device.Readings.Merge(values) };
                        failures = 0;
                        publish(new(device, true, "Connected"));
                    }
                }
                if (!resyncRequested) throw new EndOfStreamException("The event stream closed.");
                retry = TimeSpan.FromSeconds(3);
                publish(new(device, false, "Reconnecting…"));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (SensiboApiException exception) when (exception.IsAuthenticationFailure)
            {
                publish(new(device, false, "API key rejected. Open Settings."));
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException
                or OperationCanceledException or SensiboApiException or InvalidOperationException)
            {
                if (exception is SensiboApiException { RetryAfter: { } minimum } && minimum > retry) retry = minimum;
                publish(new(device, false, "Connection unavailable. Retrying automatically."));
                failures++;
            }
            await Task.Delay(retry + TimeSpan.FromMilliseconds(Random.Shared.Next(250, 1250)), token).ConfigureAwait(false);
            // A slow snapshot also keeps readings useful when the stream is temporarily unavailable.
            try
            {
                device = (await GetDevicesAsync(token).ConfigureAwait(false)).FirstOrDefault(d => d.Id == deviceId);
                publish(new(device, false, "Snapshot received; reconnecting live updates…"));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (SensiboApiException exception) when (exception.IsAuthenticationFailure)
            {
                publish(new(device, false, "API key rejected. Open Settings."));
                return;
            }
            catch (SensiboApiException exception)
            {
                if (exception.RetryAfter is { } wait) await Task.Delay(wait, token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException
                or OperationCanceledException or InvalidOperationException) { }
        }
    }

    private static void CheckResponse(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var retry = response.Headers.RetryAfter?.Delta;
        if (response.Headers.RetryAfter?.Date is { } date) retry = date - DateTimeOffset.UtcNow;
        if (retry < TimeSpan.Zero) retry = TimeSpan.Zero;
        throw new SensiboApiException((int)response.StatusCode, retry);
    }

    public void Dispose() => http.Dispose();
}
