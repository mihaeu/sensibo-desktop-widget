using System.Net;
using System.Text.Json;

namespace SensiboWidget.Core;

public sealed record TemperatureSample(double? Celsius, DateTimeOffset ValidAt);
public sealed record ForecastPoint(DateTimeOffset ValidAt, double? Celsius, int? Code, bool? IsDay);
public sealed record WeatherConditions(TemperatureSample Outside, IReadOnlyList<ForecastPoint> Hourly)
{
    public static TimeZoneInfo BarcelonaZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    public ForecastPoint? ForToday(int hour, DateTimeOffset now)
    {
        var date = TimeZoneInfo.ConvertTime(now, BarcelonaZone).Date;
        return Hourly.FirstOrDefault(point =>
        {
            var local = TimeZoneInfo.ConvertTime(point.ValidAt, BarcelonaZone);
            return local.Date == date && local.Hour == hour;
        });
    }

    public static (string Icon, string Description) Describe(int? code, bool? isDay) => code switch
    {
        0 => (isDay == false ? "🌙" : "☀️", "Clear sky"),
        1 => (isDay == false ? "🌙" : "🌤️", "Mainly clear"),
        2 => (isDay == false ? "☁️" : "⛅", "Partly cloudy"),
        3 => ("☁️", "Overcast"),
        45 or 48 => ("🌫️", "Fog"),
        51 or 53 or 55 => ("🌦️", "Drizzle"),
        56 or 57 => ("🌧️", "Freezing drizzle"),
        61 or 63 or 65 or 80 or 81 or 82 => ("🌧️", "Rain"),
        66 or 67 => ("🌧️", "Freezing rain"),
        71 or 73 or 75 or 77 or 85 or 86 => ("🌨️", "Snow"),
        95 or 96 or 99 => ("⛈️", "Thunderstorm"),
        _ => ("—", "Conditions unavailable")
    };
}

public sealed record WeatherFeed<T>(T? Value = default, DateTimeOffset? FetchedAt = null,
    DateTimeOffset NextAttempt = default, bool Failed = false) where T : class
{
    public bool IsStale(DateTimeOffset now, TimeSpan interval) =>
        Failed || FetchedAt is not { } fetched || now - fetched > interval * 2;
}

public sealed class WeatherClient : IDisposable
{
    public static readonly TimeSpan OutsideInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SeaInterval = TimeSpan.FromHours(1);
    private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast?latitude=41.3874&longitude=2.1686&current=temperature_2m&hourly=temperature_2m,weather_code,is_day&temperature_unit=celsius&timeformat=unixtime&timezone=Europe%2FMadrid&forecast_days=2";
    private const string SeaUrl = "https://marine-api.open-meteo.com/v1/marine?latitude=41.38&longitude=2.20&current=sea_surface_temperature&cell_selection=sea&timeformat=unixtime";
    private readonly HttpClient http;
    private readonly SemaphoreSlim gate = new(1, 1);

    public WeatherFeed<WeatherConditions> Weather { get; private set; } = new();
    public WeatherFeed<TemperatureSample> Sea { get; private set; } = new();

    public WeatherClient(HttpMessageHandler? handler = null)
    {
        // A separate client prevents Sensibo credentials from reaching weather services.
        http = new HttpClient(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }) { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 1024 * 1024 };
    }

    public async Task RefreshAsync(DateTimeOffset now, CancellationToken token)
    {
        if (!await gate.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            await Task.WhenAll(RefreshWeatherAsync(), RefreshSeaAsync()).ConfigureAwait(false);
        }
        finally { gate.Release(); }

        async Task RefreshWeatherAsync() => Weather = await RefreshFeedAsync(
            Weather, ForecastUrl, OutsideInterval, ParseWeather, now, token).ConfigureAwait(false);
        async Task RefreshSeaAsync() => Sea = await RefreshFeedAsync(
            Sea, SeaUrl, SeaInterval, ParseSea, now, token).ConfigureAwait(false);
    }

    private async Task<WeatherFeed<T>> RefreshFeedAsync<T>(WeatherFeed<T> feed, string url,
        TimeSpan interval, Func<JsonElement, T> parse, DateTimeOffset now, CancellationToken token) where T : class
    {
        if (now < feed.NextAttempt) return feed;
        var next = now + interval;
        try
        {
            using var response = await http.GetAsync(url, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var retry = response.Headers.RetryAfter;
                var retryAt = retry?.Date ?? (retry?.Delta is { } delay ? now + delay : next);
                return feed with { NextAttempt = retryAt > next ? retryAt : next, Failed = true };
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            return new(parse(json.RootElement), now, next);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            return feed with { NextAttempt = next, Failed = true };
        }
    }

    public static WeatherConditions ParseWeather(JsonElement root)
    {
        var current = Required(root, "current", JsonValueKind.Object);
        var outside = new TemperatureSample(Temperature(current, "temperature_2m", -100, 70), Timestamp(Required(current, "time", JsonValueKind.Number)));
        var hourly = Required(root, "hourly", JsonValueKind.Object);
        var times = Required(hourly, "time", JsonValueKind.Array);
        var temperatures = Required(hourly, "temperature_2m", JsonValueKind.Array);
        var codes = Required(hourly, "weather_code", JsonValueKind.Array);
        var days = Required(hourly, "is_day", JsonValueKind.Array);
        var count = times.GetArrayLength();
        if (count == 0 || count > 100 || temperatures.GetArrayLength() != count || codes.GetArrayLength() != count || days.GetArrayLength() != count)
            throw new JsonException("Invalid hourly weather arrays.");
        var points = new List<ForecastPoint>(count);
        for (var i = 0; i < count; i++)
        {
            var day = Integer(days[i]);
            points.Add(new(Timestamp(times[i]), Temperature(temperatures[i], -100, 70), Integer(codes[i]), day is 0 or 1 ? day == 1 : null));
        }
        return new(outside, points);
    }

    public static TemperatureSample ParseSea(JsonElement root)
    {
        var current = Required(root, "current", JsonValueKind.Object);
        return new(Temperature(current, "sea_surface_temperature", -5, 50), Timestamp(Required(current, "time", JsonValueKind.Number)));
    }

    private static JsonElement Required(JsonElement parent, string name, JsonValueKind kind) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == kind
            ? value : throw new JsonException("Missing weather data.");

    private static double? Temperature(JsonElement parent, string name, double min, double max) =>
        parent.TryGetProperty(name, out var value) ? Temperature(value, min, max) : null;

    private static double? Temperature(JsonElement value, double min, double max) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= min && number <= max ? number : null;

    private static int? Integer(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static DateTimeOffset Timestamp(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var seconds) || seconds is < -62135596800 or > 253402300799)
            throw new JsonException("Invalid weather timestamp.");
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    public void Dispose() { http.Dispose(); gate.Dispose(); }
}
