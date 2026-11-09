using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace LimitResetWatch;

/// <summary>トレイアイコン 1 個分の表示内容。</summary>
/// <param name="Number">リセットまでの時間の数字。null ならサービスの略称（Initial）を出す。</param>
/// <param name="Used">下のバーに出す使用率（%）。</param>
/// <param name="Pace">枠の期間のうち経過した割合（0〜1）。バーの上に目盛りとして出す。</param>
internal sealed record TrayIconModel(
    string? Number, string? Unit, string Initial, double? Used, double? Pace, Color Accent, bool Muted, bool LightTaskbar,
    bool IsFallback = false);

internal static class TrayPresenter
{
    public static (TrayIconModel Model, string Tooltip) Build(ProviderSnapshot snapshot, DateTimeOffset now, bool lightTaskbar)
    {
        var accent = ProviderStyle.Accent(snapshot.Id, lightTaskbar);
        var initial = ProviderStyle.Initial(snapshot.Id);
        var primary = UsageMath.PickPrimary(snapshot.Windows, now);
        var muted = snapshot.IsStale || !snapshot.HasData;

        TrayIconModel model;
        if (primary?.ResetsAt is { } reset)
        {
            var (number, unit) = TimeText.Compact(reset, now);
            model = new TrayIconModel(number, unit, initial, primary.UsedPercent, UsageMath.Elapsed(primary, now), accent,
                muted || reset <= now, lightTaskbar);
        }
        else
        {
            model = new TrayIconModel(null, null, initial, null, null, accent, true, lightTaskbar);
        }

        var name = ProviderStyle.Name(snapshot.Id);
        var lines = new List<string>
        {
            snapshot.IsStale && snapshot.FetchedAt is { } at ? $"{name}（{TimeText.At(at)} 時点）" : name,
        };
        var ordered = snapshot.Windows.OrderBy(w => ReferenceEquals(w, primary) ? 0 : 1);
        foreach (var window in ordered)
        {
            if (window.ResetsAt is not { } resetsAt) continue;
            lines.Add($"{window.Label} {Math.Round(window.UsedPercent)}% {TimeText.Until(resetsAt, now)}");
        }
        if (!snapshot.HasData && snapshot.Error is not null) lines.Add(snapshot.Error);
        return (model, Truncate(string.Join("\n", lines)));
    }

    public static TrayIconModel Fallback(bool lightTaskbar) =>
        new(null, null, "", null, null, lightTaskbar ? Color.FromArgb(0x1F, 0x1F, 0x1F) : Color.FromArgb(0xF2, 0xF2, 0xF2),
            false, lightTaskbar, IsFallback: true);

    /// <summary>NotifyIcon.Text は 127 文字まで。</summary>
    public static string Truncate(string text) => text.Length <= 127 ? text : text[..126] + "…";
}

internal static class TrayIconRenderer
{
    public static Bitmap Render(int size, TrayIconModel model)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var gray = model.LightTaskbar ? Color.FromArgb(0x8A, 0x8A, 0x8A) : Color.FromArgb(0x80, 0x80, 0x80);
        var foreground = model.Muted ? ProviderStyle.Blend(model.Accent, gray, 0.6) : model.Accent;

        if (model.IsFallback)
        {
            DrawClock(g, size, foreground);
            return bitmap;
        }

        var barHeight = model.Used is null ? 0 : Math.Max(2, (int)MathF.Round(size / 8f));
        var gap = barHeight > 0 ? Math.Max(1, size / 16) : 0;
        var textBottom = size - barHeight - gap;
        DrawText(g, size, textBottom, model.Number ?? model.Initial, model.Number is null ? "" : model.Unit ?? "", foreground);

        if (model.Used is { } used)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.None;
            var barY = size - barHeight;
            using var track = new SolidBrush(model.LightTaskbar ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(90, 255, 255, 255));
            g.FillRectangle(track, 0, barY, size, barHeight);
            var fill = (int)Math.Round(size * Math.Clamp(used / 100, 0, 1));
            using var fillBrush = new SolidBrush(foreground);
            if (fill > 0) g.FillRectangle(fillBrush, 0, barY, fill, barHeight);
            if (model.Pace is { } pace)
            {
                // 期間の経過位置。使用率のバーがこれより左なら、まだ使い切るペースに届いていない。
                var tickWidth = Math.Max(1, size / 16);
                var x = Math.Clamp((int)Math.Round(size * pace) - tickWidth / 2, 0, size - tickWidth);
                using var tick = new SolidBrush(model.LightTaskbar ? Color.Black : Color.White);
                g.FillRectangle(tick, x, barY - gap, tickWidth, barHeight + gap);
            }
        }
        return bitmap;
    }

    /// <summary>数字を大きく、単位を小さく、下端をそろえて描く。幅が足りなければ横に詰め、それでも足りなければ縮める。</summary>
    static void DrawText(Graphics g, int size, int textBottom, string main, string unit, Color color)
    {
        const float DigitHeight = 0.70f;   // Segoe UI の数字の高さ（em に対する比）
        const float MinCondense = 0.72f;
        using var family = new FontFamily("Segoe UI");
        var style = FontStyle.Bold;
        var mainEm = textBottom / DigitHeight;
        var unitEm = mainEm * 0.6f;

        using var format = StringFormat.GenericTypographic;
        float Width(string text, float em)
        {
            if (text.Length == 0) return 0;
            using var font = new Font(family, em, style, GraphicsUnit.Pixel);
            return g.MeasureString(text, font, PointF.Empty, format).Width;
        }

        var total = Width(main, mainEm) + Width(unit, unitEm);
        var condense = Math.Min(1f, size / total);
        var shrink = 1f;
        if (condense < MinCondense)
        {
            shrink = condense / MinCondense;
            condense = MinCondense;
        }
        mainEm *= shrink;
        unitEm *= shrink;
        var mainWidth = Width(main, mainEm);
        var unitWidth = Width(unit, unitEm);
        var drawnWidth = (mainWidth + unitWidth) * condense;

        // 縮めたぶんは縦の中央に寄せる。
        var digitHeight = mainEm * DigitHeight;
        var baseline = textBottom - (textBottom - digitHeight) / 2f;
        var ascentRatio = family.GetCellAscent(style) / (float)family.GetEmHeight(style);

        var state = g.Save();
        g.TranslateTransform((size - drawnWidth) / 2f, 0);
        g.ScaleTransform(condense, 1f);
        using var brush = new SolidBrush(color);
        using (var font = new Font(family, mainEm, style, GraphicsUnit.Pixel))
        {
            g.DrawString(main, font, brush, 0, baseline - mainEm * ascentRatio, format);
        }
        if (unit.Length > 0)
        {
            using var font = new Font(family, unitEm, style, GraphicsUnit.Pixel);
            g.DrawString(unit, font, brush, mainWidth, baseline - unitEm * ascentRatio, format);
        }
        g.Restore(state);
    }

    static void DrawClock(Graphics g, int size, Color color)
    {
        var stroke = Math.Max(1.5f, size / 9f);
        var radius = size / 2f - stroke;
        var center = size / 2f;
        using var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawEllipse(pen, center - radius, center - radius, radius * 2, radius * 2);
        g.DrawLine(pen, center, center, center, center - radius * 0.6f);
        g.DrawLine(pen, center, center, center + radius * 0.45f, center + radius * 0.2f);
    }
}

/// <summary>NotifyIcon 1 個と、そのアイコンのハンドルを持つ。</summary>
internal sealed class TrayIconSlot : IDisposable
{
    readonly NotifyIcon icon;
    IntPtr iconHandle;
    string? renderedKey;

    public event Action? Clicked;

    public TrayIconSlot(ContextMenuStrip menu)
    {
        icon = new NotifyIcon { ContextMenuStrip = menu };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Clicked?.Invoke();
        };
    }

    public void Update(TrayIconModel model, string tooltip)
    {
        var size = SystemInformation.SmallIconSize.Width;
        var key = $"{size}|{model}";
        if (key != renderedKey)
        {
            using var bitmap = TrayIconRenderer.Render(size, model);
            var handle = bitmap.GetHicon();
            icon.Icon = Icon.FromHandle(handle);
            if (iconHandle != IntPtr.Zero) Native.DestroyIcon(iconHandle);
            iconHandle = handle;
            renderedKey = key;
        }
        icon.Text = TrayPresenter.Truncate(tooltip);
        icon.Visible = true;
    }

    /// <summary>トレイから外す。NotifyIcon は作り直さないので、識別番号は変わらない。</summary>
    public void Hide() => icon.Visible = false;

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        if (iconHandle != IntPtr.Zero) Native.DestroyIcon(iconHandle);
        iconHandle = IntPtr.Zero;
    }
}
