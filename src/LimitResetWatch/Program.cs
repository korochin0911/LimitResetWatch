namespace LimitResetWatch;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 画面を出さずに 1 回取得し、トレイアイコンと浮かぶ窓を画像に書き出す（見た目の確認用）。
        if (args is ["--dump", var directory]) return DumpMode.Run(directory);

        using var mutex = new Mutex(initiallyOwned: true, @"Local\LimitResetWatch", out var createdNew);
        if (!createdNew) return 0;
        Application.Run(new TrayApp());
        return 0;
    }
}
