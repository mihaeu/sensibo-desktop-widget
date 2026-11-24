using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using SensiboWidget.Core;

namespace SensiboWidget;

public sealed record WidgetSettings(string? DeviceId = null, SavedPosition? Position = null,
    string Theme = "System", int Size = 176);

public sealed class StateStore
{
    public string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SensiboDesktopWidget");
    public string? LoadWarning { get; private set; }

    public WidgetSettings Load()
    {
        var file = Path.Combine(Folder, "settings.json");
        if (!File.Exists(file)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(file)) ?? new();
            return settings with { Size = Math.Clamp(settings.Size, 156, 240),
                Theme = settings.Theme is "Light" or "Dark" ? settings.Theme : "System" };
        }
        catch (JsonException)
        {
            File.Copy(file, file + ".invalid", true);
            LoadWarning = "Invalid settings were backed up. Choose your device again in Settings.";
            return new();
        }
    }

    public void Save(WidgetSettings settings) => WriteAtomic("settings.json",
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })));

    public string? LoadKey()
    {
        var path = Path.Combine(Folder, "api-key.bin");
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (CryptographicException)
        {
            LoadWarning = "The saved API key cannot be decrypted. Enter it again in Settings.";
            return null;
        }
    }

    public void SaveKey(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        try { WriteAtomic("api-key.bin", ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private void WriteAtomic(string name, byte[] bytes)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, name);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
    }
}

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "SensiboDesktopWidget";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(Name, false);
        }
    }
}
