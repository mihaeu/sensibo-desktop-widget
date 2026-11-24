using System.Globalization;
using System.Windows;
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
        clock = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => RefreshReadings(), Dispatcher);
        SystemEvents.UserPreferenceChanged += ThemeChanged;
        Loaded += async (_, _) =>
        {
            MonitorPosition.Restore(this, settings.Position);
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
        var warning = !latest.Connected || latest.Device?.Online == false || old;
        StatusDot.Visibility = warning || demo ? Visibility.Visible : Visibility.Collapsed;
        var age = readings?.MeasuredAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "not received";
        var status = latest.Device?.Online == false ? "Device offline" : latest.Message;
        if (old && readings?.MeasuredAt is not null) status += "\nMeasurement is over 15 minutes old.";
        SurfaceBorder.ToolTip = $"{latest.Device?.Name ?? "Sensibo"}\n{status}\nLast measurement: {age}\neCO₂ is estimated from TVOC.\nDrag to move · Right-click for settings";
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
        Application.Current.Shutdown();
    }
}
