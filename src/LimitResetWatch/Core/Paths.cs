namespace LimitResetWatch;

internal static class Paths
{
    public static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LimitResetWatch");

    /// <summary>環境変数で場所を変えられるフォルダー（CODEX_HOME など）。無ければホーム直下の既定名。</summary>
    public static string EnvDir(string variable, string defaultName) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } dir ? dir : Path.Combine(UserHome, defaultName);

    /// <summary>ほかのアプリが書き込み中でも読めるように共有モードで開く。</summary>
    public static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
