using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SensiboWidget.Core;

namespace SensiboWidget;

public partial class SettingsWindow : Window
{
    private readonly StateStore store;
    private readonly bool demo;
    private readonly CancellationTokenSource lifetime = new();
    private string? validatedKey;
    public WidgetSettings Result { get; private set; }

    public SettingsWindow(StateStore store, WidgetSettings settings, bool demo)
    {
        this.store = store;
        this.demo = demo;
        Result = settings;
        InitializeComponent();
        ThemeBox.SelectedIndex = settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        SizeBox.SelectedIndex = settings.Size switch { 156 => 0, 208 => 2, 240 => 3, _ => 1 };
        StartupBox.IsChecked = StartupRegistration.Enabled;
        KeyBox.Password = demo ? "" : store.LoadKey() ?? "";
        StatusText.Text = store.LoadWarning ?? "Your key is sent only to Sensibo. No readings are stored on disk.";
        if (demo)
        {
            KeyBox.IsEnabled = ConnectButton.IsEnabled = DeviceBox.IsEnabled = StartupBox.IsEnabled = false;
            SaveButton.IsEnabled = true;
            StatusText.Text = "Demo preview. Appearance changes apply only to this session.";
        }
        Closed += (_, _) => lifetime.Cancel();
        Loaded += async (_, _) => { if (!demo && KeyBox.Password.Length > 0) await ConnectAsync(); };
    }

    private void KeyChanged(object sender, RoutedEventArgs e)
    {
        validatedKey = null;
        if (SaveButton is not null) SaveButton.IsEnabled = false;
        if (DeviceBox is not null) DeviceBox.ItemsSource = null;
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SaveButton is not null) SaveButton.IsEnabled = validatedKey is not null && DeviceBox.SelectedItem is Device;
    }

    private async void Connect(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async Task ConnectAsync()
    {
        var key = KeyBox.Password.Trim();
        if (key.Length == 0) { StatusText.Text = "Paste your API key first."; return; }
        ConnectButton.IsEnabled = KeyBox.IsEnabled = SaveButton.IsEnabled = false;
        StatusText.Text = "Connecting…";
        try
        {
            using var client = new SensiboClient(key);
            var devices = await client.GetDevicesAsync(lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            validatedKey = key;
            DeviceBox.ItemsSource = devices;
            DeviceBox.SelectedItem = devices.FirstOrDefault(d => d.Id == Result.DeviceId) ?? devices.FirstOrDefault();
            StatusText.Text = devices.Count > 0 ? "Connected. Choose your device and save." : "No devices found. Use the account shown in your phone app.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (SensiboApiException exception)
        {
            StatusText.Text = exception.IsAuthenticationFailure ? "Key rejected. Check the key or create a new one."
                : exception.StatusCode == 429 ? "Rate limit reached. Wait a few minutes, then retry."
                : "Sensibo is temporarily unavailable. Try again later.";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or OperationCanceledException or ArgumentException or InvalidOperationException)
        { StatusText.Text = "Could not connect. Check your internet connection and API key."; }
        finally
        {
            ConnectButton.IsEnabled = KeyBox.IsEnabled = true;
            SaveButton.IsEnabled = validatedKey is not null && DeviceBox.SelectedItem is Device;
        }
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        if (!demo && (validatedKey is null || DeviceBox.SelectedItem is not Device)) return;
        var updated = Result with
        {
            DeviceId = (DeviceBox.SelectedItem as Device)?.Id ?? Result.DeviceId,
            Theme = ((ComboBoxItem)ThemeBox.SelectedItem).Content.ToString()!,
            Size = int.Parse(((ComboBoxItem)SizeBox.SelectedItem).Tag.ToString()!, System.Globalization.CultureInfo.InvariantCulture)
        };
        try
        {
            if (!demo)
            {
                store.SaveKey(validatedKey!);
                store.Save(updated);
                StartupRegistration.Enabled = StartupBox.IsChecked == true;
            }
            Result = updated;
            DialogResult = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or System.Security.SecurityException)
        { StatusText.Text = "Could not save settings or startup registration. Check your Windows account permissions."; }
    }

    private void OpenKeyPage(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://home.sensibo.com/me/api") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { StatusText.Text = "Open https://home.sensibo.com/me/api in your browser."; }
    }
}
