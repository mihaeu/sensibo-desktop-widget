using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SensiboWidget.Core;
using Forms = System.Windows.Forms;

namespace SensiboWidget;

public partial class MainWindow : Window
{
    private readonly StateStore store;
    private readonly bool demo;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer clock;
    private readonly SemaphoreSlim monitorGate = new(1, 1);
    private readonly WeatherClient weather = new();
    private readonly CancellationTokenSource weatherLifetime = new();
    private Task weatherTask = Task.CompletedTask;
    private WidgetSettings settings;
    private CancellationTokenSource? monitoring;
    private Task? monitorTask;
    private MonitorUpdate latest = new(null, false, "Open Settings to connect your device.");
    private bool settingsOpen;
    private bool exiting;

    public MainWindow(StateStore store, bool demo)
    {
        this.store = store;
        this.demo = demo;
        settings = demo ? new() : store.Load();
        InitializeComponent();
        ApplyAppearance();
        tray = new Forms.NotifyIcon { Text = demo ? "Sensibo widget (demo)" : "Sensibo widget",
            Icon = System.Drawing.SystemIcons.Application, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show widget", null, (_, _) => Dispatcher.Invoke(Show));
        menu.Items.Add("Settings…", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Exit));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Show);
        clock = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) =>
        {
            RefreshReadings();
            if (!demo && !exiting && weatherTask.IsCompleted) weatherTask = RefreshWeatherAsync();
        }, Dispatcher);
        SystemEvents.UserPreferenceChanged += ThemeChanged;
        Loaded += async (_, _) =>
        {
            MonitorPosition.Restore(this, settings.Position);
            if (!demo) weatherTask = RefreshWeatherAsync();
            if (demo)
            {
                latest = new(new("demo", "Demo preview", "airq", new(20.3, 69.3, 519, 967, DateTimeOffset.UtcNow), true), true, "Sample readings — demo mode");
                RefreshReadings();
            }
            else if (settings.DeviceId is null || store.LoadKey() is null) ShowSettings();
            else await StartMonitoringAsync();
        };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) SurfaceBorder.ContextMenu.IsOpen = true;
            if (e.Key == Key.Escape) Hide();
        };
    }

    private async Task StartMonitoringAsync()
    {
        if (demo) return;
        await monitorGate.WaitAsync();
        try
        {
        if (exiting) return;
        monitoring?.Cancel();
        if (monitorTask is not null)
        {
            try { await monitorTask; } catch (OperationCanceledException) { }
        }
        monitoring?.Dispose();
        var key = store.LoadKey();
        if (key is null || settings.DeviceId is null) return;
        monitoring = new CancellationTokenSource();
        var token = monitoring.Token;
        var id = settings.DeviceId;
        latest = new(null, false, "Connecting…");
        RefreshReadings();
        monitorTask = Task.Run(async () =>
        {
            using var client = new SensiboClient(key);
            try
            {
                await client.MonitorAsync(id, update => Dispatcher.BeginInvoke(() =>
                {
                    if (token.IsCancellationRequested || exiting) return;
                    latest = update;
                    RefreshReadings();
                }), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }, token);
        }
        finally { monitorGate.Release(); }
    }

    private void RefreshReadings()
    {
        var readings = latest.Device?.Readings;
        TemperatureText.Text = Format(readings?.Temperature, "0.0");
        HumidityText.Text = Format(readings?.Humidity, "0.#");
        TvocText.Text = Format(readings?.Tvoc, "0");
        Co2Text.Text = Format(readings?.Co2, "0");
        var old = readings?.MeasuredAt is not { } stamp || DateTimeOffset.UtcNow - stamp > TimeSpan.FromMinutes(15);
        var weatherWarning = RefreshWeatherReadings();
        var warning = !latest.Connected || latest.Device?.Online == false || old || weatherWarning;
        StatusDot.Visibility = warning || demo ? Visibility.Visible : Visibility.Collapsed;
        var age = readings?.MeasuredAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "not received";
        var status = latest.Device?.Online == false ? "Device offline" : latest.Message;
        if (old && readings?.MeasuredAt is not null) status += "\nMeasurement is over 15 minutes old.";
        if (weatherWarning) status += "\nSome weather data is unavailable or outdated. Hover each value for details.";
        SurfaceBorder.ToolTip = $"{latest.Device?.Name ?? "Sensibo"}\n{status}\nLast measurement: {age}\neCO₂ is estimated from TVOC.\nWeather: Open-Meteo (CC BY 4.0).\nDrag to move · Right-click for settings";
    }

    private async Task RefreshWeatherAsync()
    {
        try
        {
            await weather.RefreshAsync(DateTimeOffset.UtcNow, weatherLifetime.Token);
            if (!exiting) RefreshReadings();
        }
        catch (OperationCanceledException) when (weatherLifetime.IsCancellationRequested) { }
    }

    private bool RefreshWeatherReadings()
    {
        var now = DateTimeOffset.UtcNow;
        var conditions = weather.Weather.Value;
        var sea = weather.Sea.Value;
        if (demo)
        {
            var date = TimeZoneInfo.ConvertTime(now, WeatherConditions.BarcelonaZone).Date;
            ForecastPoint Point(int hour, double temperature, int code, bool day)
            {
                var local = date.AddHours(hour);
                return new(new DateTimeOffset(local, WeatherConditions.BarcelonaZone.GetUtcOffset(local)), temperature, code, day);
            }
            conditions = new(new(15.8, now), [Point(8, 14, 1, true), Point(12, 18, 0, true), Point(21, 13, 0, false)]);
            sea = new(18.1, now);
        }
        var outsideOld = !demo && (weather.Weather.IsStale(now, WeatherClient.OutsideInterval)
            || conditions is null || now - conditions.Outside.ValidAt > WeatherClient.OutsideInterval * 2);
        var seaOld = !demo && (weather.Sea.IsStale(now, WeatherClient.SeaInterval)
            || sea is null || now - sea.ValidAt > WeatherClient.SeaInterval * 2);
        OutsideText.Text = Format(conditions?.Outside.Celsius, "0.0");
        SeaText.Text = Format(sea?.Celsius, "0.0");
        OutsideText.Opacity = outsideOld ? 0.6 : 1;
        SeaText.Opacity = seaOld ? 0.6 : 1;
        OutsidePanel.ToolTip = WeatherTip("Barcelona outside air · model estimate", conditions?.Outside, weather.Weather.FetchedAt, outsideOld, "15 minutes");
        SeaPanel.ToolTip = WeatherTip("Barcelona coast · sea surface model estimate", sea, weather.Sea.FetchedAt, seaOld, "1 hour");
        var morningMissing = ShowForecast(MorningPanel, MorningIcon, MorningText, conditions?.ForToday(8, now), "Morning", 8, outsideOld, now);
        var noonMissing = ShowForecast(NoonPanel, NoonIcon, NoonText, conditions?.ForToday(12, now), "Noon", 12, outsideOld, now);
        var nightMissing = ShowForecast(NightPanel, NightIcon, NightText, conditions?.ForToday(21, now), "Night", 21, outsideOld, now);
        return outsideOld || seaOld || conditions?.Outside.Celsius is null || sea?.Celsius is null || morningMissing || noonMissing || nightMissing;
    }

    private string WeatherTip(string label, TemperatureSample? sample, DateTimeOffset? fetched, bool old, string interval)
    {
        static string Time(DateTimeOffset? time) => time is { } value
            ? TimeZoneInfo.ConvertTime(value, WeatherConditions.BarcelonaZone).ToString("dd MMM HH:mm", CultureInfo.InvariantCulture) : "not received";
        return $"{label}\n{(demo ? "Sample data — demo mode" : old ? "Unavailable or outdated; last value retained." : "Open-Meteo · CC BY 4.0")}\nValid at: {Time(sample?.ValidAt)} · Barcelona time\nFetched: {Time(demo ? DateTimeOffset.UtcNow : fetched)}\nRefresh: every {interval}{(sample?.Celsius is null ? "\nTemperature unavailable." : "")}";
    }

    private bool ShowForecast(StackPanel panel, TextBlock icon, TextBlock temperature, ForecastPoint? point,
        string label, int hour, bool old, DateTimeOffset now)
    {
        var description = WeatherConditions.Describe(point?.Code, point?.IsDay);
        icon.Text = description.Icon;
        temperature.Text = Format(point?.Celsius, "0");
        panel.Opacity = old ? 0.6 : 1;
        var date = TimeZoneInfo.ConvertTime(now, WeatherConditions.BarcelonaZone).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        var text = $"{label} · {date}, {hour:00}:00 Barcelona time\n{description.Description} · {Format(point?.Celsius, "0")} °C\n{(demo ? "Sample forecast — demo mode" : old ? "Forecast may be outdated." : "Weather forecast by Open-Meteo · CC BY 4.0")}";
        panel.ToolTip = text;
        AutomationProperties.SetName(panel, text);
        return point?.Celsius is null || point.Code is null;
    }

    private static string Format(double? value, string format) => value?.ToString(format, CultureInfo.InvariantCulture) ?? "—";

    private void DragWidget(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || settingsOpen) return;
        try { DragMove(); }
        catch (InvalidOperationException) { return; }
        SavePosition();
    }

    private void SavePosition()
    {
        settings = settings with { Position = MonitorPosition.SnapAndCapture(this) };
        if (!demo) SaveSettings();
    }

    private void SaveSettings()
    {
        try { store.Save(settings); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, "The position could not be saved. Check access to the local settings folder.", "Sensibo widget"); }
    }

    private async void ShowSettings()
    {
        if (settingsOpen || exiting) return;
        settingsOpen = true;
        try
        {
            var dialog = new SettingsWindow(store, settings, demo) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                settings = dialog.Result;
                ApplyAppearance();
                MonitorPosition.Restore(this, settings.Position);
                await StartMonitoringAsync();
            }
        }
        finally { settingsOpen = false; }
    }

    private void ApplyAppearance()
    {
        Width = Height = settings.Size;
        using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var light = settings.Theme == "Light" || (settings.Theme == "System" && (personalize?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0);
        SetBrush("Surface", light ? "#F2F6F8" : "#20272B");
        SetBrush("Ink", light ? "#222B29" : "#EDF2EF");
        SetBrush("MutedInk", light ? "#5C6964" : "#A8B5AE");
        SetBrush("Line", light ? "#D7E0E5" : "#39464D");
    }

    private void SetBrush(string name, string color) => Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(ApplyAppearance);

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x007E) Dispatcher.BeginInvoke(() => MonitorPosition.Restore(this, settings.Position));
        return IntPtr.Zero;
    }

    private void OpenSettings(object sender, RoutedEventArgs e) => ShowSettings();
    private async void Reconnect(object sender, RoutedEventArgs e) => await StartMonitoringAsync();
    private void OpenWeatherSource(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://open-meteo.com/") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception)
        { MessageBox.Show(this, "Weather data: https://open-meteo.com/ (CC BY 4.0).", "Weather source"); }
    }
    private void ResetPosition(object sender, RoutedEventArgs e)
    {
        settings = settings with { Position = null };
        MonitorPosition.Restore(this, null);
        if (!demo) SaveSettings();
    }
    private void HideWidget(object sender, RoutedEventArgs e) => Hide();
    private void Quit(object sender, RoutedEventArgs e) => Exit();

    private async void Exit()
    {
        if (exiting) return;
        exiting = true;
        monitoring?.Cancel();
        weatherLifetime.Cancel();
        clock.Stop();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        SystemEvents.UserPreferenceChanged -= ThemeChanged;
        if (monitorTask is not null)
        {
            try { await monitorTask; } catch (OperationCanceledException) { }
        }
        monitoring?.Dispose();
        await weatherTask;
        weather.Dispose();
        weatherLifetime.Dispose();
        Application.Current.Shutdown();
    }
}
