using System.Text.Json;

namespace SensiboWidget.Core;

public sealed record Readings(double? Temperature = null, double? Humidity = null,
    double? Tvoc = null, double? Co2 = null, DateTimeOffset? MeasuredAt = null)
{
    public Readings Merge(JsonElement value) => new(
        Number(value, "temperature", Temperature), Number(value, "humidity", Humidity),
        Number(value, "tvoc", Tvoc), Number(value, "co2", Co2), Timestamp(value, MeasuredAt));

    private static double? Number(JsonElement source, string name, double? previous)
    {
        if (!source.TryGetProperty(name, out var field)) return previous;
        return field.TryGetDoubleSafe(out var number) && double.IsFinite(number) ? number : null;
    }

    private static DateTimeOffset? Timestamp(JsonElement source, DateTimeOffset? previous)
    {
        if (!source.TryGetProperty("time", out var time)) return previous;
        return time.ValueKind == JsonValueKind.Object && time.TryGetProperty("time", out var text)
            && text.ValueKind == JsonValueKind.String && text.TryGetDateTimeOffset(out var stamp)
            ? stamp : null;
    }
}

internal static class JsonNumbers
{
    public static bool TryGetDoubleSafe(this JsonElement value, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number);
    }
}

public sealed record Device(string Id, string Name, string Model, Readings Readings, bool? Online);
public sealed record MonitorUpdate(Device? Device, bool Connected, string Message);
