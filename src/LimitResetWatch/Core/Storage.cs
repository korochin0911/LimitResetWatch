using System.Text.Json;
using System.Text.Json.Serialization;

namespace LimitResetWatch;

/// <summary>前回取れた値を保存しておき、起動直後やログイン切れのときにもリセットまでの時間を出せるようにする。</summary>
internal static class SnapshotStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    static string FilePath => Path.Combine(Paths.AppDataDir, "snapshots.json");

    public static string Serialize(IEnumerable<ProviderSnapshot> snapshots) =>
        JsonSerializer.Serialize(snapshots.ToList(), Options);

    public static Dictionary<ProviderId, ProviderSnapshot> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var list = JsonSerializer.Deserialize<List<ProviderSnapshot>>(File.ReadAllText(FilePath), Options) ?? [];
            return list.Where(s => s.HasData).ToDictionary(s => s.Id, s => s with { IsStale = true });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<ProviderSnapshot> snapshots)
    {
        try
        {
            Directory.CreateDirectory(Paths.AppDataDir);
            File.WriteAllText(FilePath, Serialize(snapshots.Where(s => s.HasData)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class AppSettings
{
    public int PollMinutes { get; set; } = 5;
    public bool FloatingVisible { get; set; } = true;
    public int? FloatingX { get; set; }
    public int? FloatingY { get; set; }

    static string FilePath => Path.Combine(Paths.AppDataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) is { } settings)
            {
                settings.PollMinutes = Math.Clamp(settings.PollMinutes, 1, 120);
                return settings;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.AppDataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>「Windows の起動時に開始」。HKCU の Run キーに登録する。</summary>
internal static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "LimitResetWatch";

    public static bool IsEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string command
            && Environment.ProcessPath is { } exe
            && command.Contains(exe, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe) key.SetValue(ValueName, $"\"{exe}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
