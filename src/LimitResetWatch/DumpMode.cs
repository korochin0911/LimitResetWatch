using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LimitResetWatch;

/// <summary>
/// LimitResetWatch.exe --dump &lt;フォルダー&gt;
/// 使用量を 1 回取り、取得結果（snapshots.json）・トレイアイコンの拡大図・浮かぶ窓を画像に書き出して終わる。
/// ログイン情報は書き出さない。保存済みの前回の値（%APPDATA%\LimitResetWatch）も読み書きしない。
/// </summary>
internal static class DumpMode
{
    static readonly int[] IconSizes = [16, 20, 24, 32];
    const int Zoom = 4;

    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var poller = new UsagePoller(ProviderRegistry.CreateAll(), [], persist: false);
        poller.PollAsync().GetAwaiter().GetResult();
        var now = DateTimeOffset.Now;
        var snapshots = poller.All;

        File.WriteAllText(Path.Combine(directory, "snapshots.json"), SnapshotStore.Serialize(snapshots));

        var view = new FloatingView(snapshots.Where(UsageMath.IsVisible).ToList(), now, poller.LastPollAt, false);
        SaveFloating(Path.Combine(directory, "floating-dark.png"), Palette.Dark, view, 1f);
        SaveFloating(Path.Combine(directory, "floating-light.png"), Palette.Light, view, 1f);
        SaveFloating(Path.Combine(directory, "floating-dark-150.png"), Palette.Dark, view, 1.5f);
        SaveIconSheet(Path.Combine(directory, "tray-icons.png"), snapshots, now);
        return 0;
    }

    static void SaveFloating(string path, Palette palette, FloatingView view, float scale)
    {
        Size size;
        using (var probe = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(probe))
        {
            size = FloatingRenderer.Render(g, scale, palette, view, draw: false);
        }
        // GDI の文字描画はアルファを書かないので、透過の無い形式にする（実際の窓と同じ見え方になる）。
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(palette.Background);
            FloatingRenderer.Render(g, scale, palette, view, draw: true);
        }
        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>行がサービス、列が「暗いタスクバー × 大きさ」「明るいタスクバー × 大きさ」。4 倍に拡大して並べる。</summary>
    static void SaveIconSheet(string path, IReadOnlyList<ProviderSnapshot> snapshots, DateTimeOffset now)
    {
        var rows = new List<Func<bool, TrayIconModel>>();
        foreach (var snapshot in snapshots.Where(s => ProviderStyle.InTray(s.Id) && UsageMath.IsVisible(s)))
        {
            rows.Add(light => TrayPresenter.Build(snapshot, now, light).Model);
        }
        rows.Add(TrayPresenter.Fallback);

        var cell = IconSizes.Max() * Zoom + 16;
        var columns = IconSizes.Length * 2;
        using var sheet = new Bitmap(cell * columns, cell * rows.Count, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        for (var column = 0; column < columns; column++)
        {
            var light = column >= IconSizes.Length;
            using var background = new SolidBrush(light ? Color.FromArgb(0xEE, 0xEE, 0xEE) : Color.FromArgb(0x1C, 0x1C, 0x1C));
            g.FillRectangle(background, column * cell, 0, cell, cell * rows.Count);
            var size = IconSizes[column % IconSizes.Length];
            for (var row = 0; row < rows.Count; row++)
            {
                using var icon = TrayIconRenderer.Render(size, rows[row](light));
                var drawn = size * Zoom;
                g.DrawImage(icon, new Rectangle(column * cell + (cell - drawn) / 2, row * cell + (cell - drawn) / 2, drawn, drawn));
            }
        }
        sheet.Save(path, ImageFormat.Png);
    }
}
