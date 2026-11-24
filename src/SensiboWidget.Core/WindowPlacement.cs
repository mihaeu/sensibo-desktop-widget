namespace SensiboWidget.Core;

// Positions use monitor-relative fractions; work areas and window sizes use physical pixels.
public sealed record SavedPosition(string Monitor, double X, double Y);
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
public readonly record struct PixelPoint(int X, int Y);

public static class WindowPlacement
{
    public static PixelPoint Restore(PixelRect area, int width, int height, SavedPosition? saved)
    {
        var x = ValidFraction(saved?.X, 1);
        var y = ValidFraction(saved?.Y, 0);
        return new(area.X + (int)Math.Round(x * Math.Max(0, area.Width - width)),
            area.Y + (int)Math.Round(y * Math.Max(0, area.Height - height)));
    }

    public static PixelPoint Snap(PixelRect area, PixelRect window, int threshold)
    {
        var maxX = area.X + Math.Max(0, area.Width - window.Width);
        var maxY = area.Y + Math.Max(0, area.Height - window.Height);
        var x = Math.Clamp(window.X, area.X, maxX);
        var y = Math.Clamp(window.Y, area.Y, maxY);
        if (x - area.X <= threshold) x = area.X;
        else if (maxX - x <= threshold) x = maxX;
        if (y - area.Y <= threshold) y = area.Y;
        else if (maxY - y <= threshold) y = maxY;
        return new(x, y);
    }

    public static SavedPosition Capture(string monitor, PixelRect area, PixelRect window) => new(
        monitor, Fraction(window.X - area.X, area.Width - window.Width),
        Fraction(window.Y - area.Y, area.Height - window.Height));

    private static double Fraction(int offset, int space) => space > 0 ? Math.Clamp((double)offset / space, 0, 1) : 0;
    private static double ValidFraction(double? value, double fallback) =>
        value is { } number && double.IsFinite(number) ? Math.Clamp(number, 0, 1) : fallback;
}
