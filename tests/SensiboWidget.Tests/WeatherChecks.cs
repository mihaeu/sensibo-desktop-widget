using System.Net;
using System.Text.Json;
using SensiboWidget.Core;

static class WeatherChecks
{
    private static string Forecast(DateTimeOffset now, double? temperature = 15.8) => JsonSerializer.Serialize(new
    {
        current = new { time = now.ToUnixTimeSeconds(), temperature_2m = temperature },
        hourly = new
        {
            time = new[] { now.ToUnixTimeSeconds(), now.AddHours(4).ToUnixTimeSeconds(), now.AddHours(13).ToUnixTimeSeconds() },
            temperature_2m = new double?[] { 14, 18, null },
            weather_code = new int?[] { 1, 0, null },
            is_day = new[] { 1, 1, 0 }
        }
    });

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    public static async Task RunAsync(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 11, 26, 7, 0, 0, TimeSpan.Zero);
        var outsideCalls = 0;
        var seaCalls = 0;
        using (var client = new WeatherClient(new StubHandler(request =>
        {
            check(request.Method == HttpMethod.Get && request.RequestUri!.Scheme == "https"
                && !request.Headers.Contains("X-Api-Key") && !request.RequestUri.Query.Contains("apikey"), "weather requests contain no account credentials");
            if (request.RequestUri!.Host == "marine-api.open-meteo.com")
            {
                seaCalls++;
                return Json(JsonSerializer.Serialize(new { current = new { time = now.ToUnixTimeSeconds(), sea_surface_temperature = 17.2 } }));
            }
            outsideCalls++;
            return Json(Forecast(now));
        })))
        {
            await client.RefreshAsync(now, CancellationToken.None);
            check(outsideCalls == 1 && seaCalls == 1 && client.Weather.Value?.Outside.Celsius == 15.8 && client.Sea.Value?.Celsius == 17.2,
                "initial weather and sea readings are independent");
            await client.RefreshAsync(now.AddMinutes(14), CancellationToken.None);
            check(outsideCalls == 1 && seaCalls == 1, "no early weather refresh");
            await client.RefreshAsync(now.AddMinutes(15), CancellationToken.None);
            check(outsideCalls == 2 && seaCalls == 1, "outside refresh at 15 minutes leaves sea cached");
            await client.RefreshAsync(now.AddMinutes(59), CancellationToken.None);
            check(outsideCalls == 3 && seaCalls == 1, "sea is not refreshed before one hour");
            await client.RefreshAsync(now.AddHours(1), CancellationToken.None);
            check(outsideCalls == 3 && seaCalls == 2, "sea refreshes hourly without an early outside refresh");
            check(client.Weather.Value!.ForToday(8, now)?.Celsius == 14 && client.Weather.Value.ForToday(21, now)?.Celsius is null,
                "forecast periods select Barcelona hours and preserve missing values");
        }

        var fail = false;
        using (var client = new WeatherClient(new StubHandler(request =>
        {
            if (request.RequestUri!.Host == "marine-api.open-meteo.com") return Json("{\"current\":{\"time\":0,\"sea_surface_temperature\":null}}");
            if (!fail) return Json(Forecast(now, 0));
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromHours(2));
            return response;
        })))
        {
            await client.RefreshAsync(now, CancellationToken.None);
            fail = true;
            await client.RefreshAsync(now.AddMinutes(15), CancellationToken.None);
            check(client.Weather.Value?.Outside.Celsius == 0 && client.Weather.Failed && client.Weather.FetchedAt == now
                && client.Weather.NextAttempt == now.AddMinutes(135), "rate limits retain last good data and honor Retry-After");
            check(client.Sea.Value?.Celsius is null && !client.Sea.Failed, "missing sea temperature is unknown, not zero");
        }

        foreach (var malformed in new[] { "{}", "[]", "{\"current\":null}", Forecast(now).Replace("\"is_day\":[1,1,0]", "\"is_day\":[]") })
        {
            using var json = JsonDocument.Parse(malformed);
            try { WeatherClient.ParseWeather(json.RootElement); throw new Exception("Malformed weather was accepted."); }
            catch (JsonException) { }
        }
        check(true, "malformed and mismatched forecast arrays are rejected");
        using (var invalid = JsonDocument.Parse("{\"current\":{\"time\":0,\"sea_surface_temperature\":999}}"))
            check(WeatherClient.ParseSea(invalid.RootElement).Celsius is null, "invalid sea temperature is not displayed");

        foreach (var instant in new[]
        {
            new DateTimeOffset(2026, 3, 29, 6, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 25, 7, 0, 0, TimeSpan.Zero)
        })
        {
            using var json = JsonDocument.Parse(Forecast(instant));
            var data = WeatherClient.ParseWeather(json.RootElement);
            check(data.ForToday(8, instant)?.ValidAt == instant, "forecast respects Barcelona daylight-saving changes");
            check(data.ForToday(8, instant.AddDays(1)) is null, "yesterday's forecast is never shown for today");
        }
        var midnight = new DateTimeOffset(2026, 11, 26, 23, 30, 0, TimeSpan.Zero);
        using (var json = JsonDocument.Parse(Forecast(midnight.AddHours(7.5))))
            check(WeatherClient.ParseWeather(json.RootElement).ForToday(8, midnight) is not null, "forecast date uses Barcelona midnight, not UTC midnight");
        check(WeatherConditions.Describe(0, false).Icon == "🌙" && WeatherConditions.Describe(0, true).Icon == "☀️"
            && WeatherConditions.Describe(999, true).Icon == "—", "weather icons distinguish night and unknown conditions");

        var requests = 0;
        using (var client = new WeatherClient(new StubHandler(request =>
        {
            requests++;
            return Json("not JSON");
        })))
        {
            await client.RefreshAsync(now, CancellationToken.None);
            await client.RefreshAsync(now.AddMinutes(1), CancellationToken.None);
            check(requests == 2 && client.Weather.Failed && client.Sea.Failed, "bad responses back off at the configured intervals");
        }
        using (var client = new WeatherClient(new CancellableWeatherHandler()))
        using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            var cancelled = false;
            try { await client.RefreshAsync(now, stop.Token); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { cancelled = true; }
            check(cancelled, "weather requests cancel on shutdown");
        }
    }

    public static async Task LiveAsync()
    {
        using var client = new WeatherClient();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var now = DateTimeOffset.UtcNow;
        await client.RefreshAsync(now, stop.Token);
        if (client.Weather.Failed || client.Sea.Failed || client.Weather.Value?.Outside.Celsius is null || client.Sea.Value?.Celsius is null
            || new[] { 8, 12, 21 }.Any(hour => client.Weather.Value.ForToday(hour, now)?.Celsius is null))
            throw new Exception("Live Barcelona weather data is incomplete or unavailable.");
        Console.WriteLine("PASS: live Barcelona outside, sea surface, and all three forecast periods.");
    }
}

sealed class CancellableWeatherHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return new(HttpStatusCode.OK);
    }
}
