using System.Drawing.Drawing2D;

namespace LimitResetWatch;

internal sealed record FloatingView(
    IReadOnlyList<ProviderSnapshot> Providers, DateTimeOffset Now, DateTimeOffset? LastPollAt, bool Polling);

/// <summary>浮かぶ窓の中身。大きさを測るだけ（draw: false）と、描く（draw: true）を同じ手順で行う。</summary>
internal static class FloatingRenderer
{
    const TextFormatFlags LineFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top;
    const TextFormatFlags WrapFlags = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

    sealed record Row(string Label, double? Used, double? Pace, string Countdown, string Date, bool Faded, string? Value);

    sealed record Block(string Name, string Sub, Color Accent, List<Row> Rows, string? Error, string? Warning);

    public static Size Render(Graphics g, float scale, Palette palette, FloatingView view, bool draw)
    {
        int R(float value) => (int)MathF.Round(value * scale);
        using var body = new Font(UiFont.Name, 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bold = new Font(UiFont.Name, 12.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font(UiFont.Name, 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        Size Measure(string text, Font font) =>
            text.Length == 0 ? Size.Empty : TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue), LineFlags);

        int pad = R(12), indent = R(16), gap = R(8), barWidth = R(84), barHeight = R(6), rowGap = R(4), blockGap = R(10);
        var bodyHeight = Measure("あ0", body).Height;
        var boldHeight = Measure("あ0", bold).Height;
        var smallHeight = Measure("あ0", small).Height;
        var rowHeight = Math.Max(bodyHeight, barHeight);

        var blocks = view.Providers.Select(s => BuildBlock(s, view.Now, palette)).ToList();

        // 列の幅は全サービスで共通にして、バーと残り時間の位置をそろえる。
        int labelWidth = 0, detailWidth = 0, valueWidth = 0, headerWidth = 0;
        var percentWidth = Measure("100%", body).Width;
        foreach (var block in blocks)
        {
            headerWidth = Math.Max(headerWidth,
                Measure(block.Name, bold).Width + (block.Sub.Length > 0 ? gap + Measure(block.Sub, small).Width : 0));
            foreach (var row in block.Rows)
            {
                labelWidth = Math.Max(labelWidth, Measure(row.Label, body).Width);
                if (row.Value is not null) valueWidth = Math.Max(valueWidth, Measure(row.Value, body).Width);
                else detailWidth = Math.Max(detailWidth, Measure(row.Countdown, body).Width + Measure(row.Date, small).Width);
            }
        }
        var spanWidth = barWidth + gap + percentWidth + gap + detailWidth;
        if (valueWidth > spanWidth) detailWidth += valueWidth - spanWidth;
        var contentWidth = Math.Max(
            indent + labelWidth + gap + barWidth + gap + percentWidth + gap + detailWidth,
            Math.Max(indent + headerWidth, R(260)));
        var width = pad * 2 + contentWidth;

        var y = pad;
        if (blocks.Count == 0)
        {
            var message = view.Polling || view.LastPollAt is null ? "取得中…" : "使用量を取得できるサービスがない";
            if (draw) TextRenderer.DrawText(g, message, body, new Point(pad, y), palette.Muted, palette.Background, LineFlags);
            y += bodyHeight;
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (i > 0) y += blockGap;

            if (draw)
            {
                var dot = R(8);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var dotBrush = new SolidBrush(block.Accent);
                g.FillEllipse(dotBrush, pad, y + (boldHeight - dot) / 2f, dot, dot);
                TextRenderer.DrawText(g, block.Name, bold, new Point(pad + indent, y), palette.Text, palette.Background, LineFlags);
                if (block.Sub.Length > 0)
                {
                    var x = pad + indent + Measure(block.Name, bold).Width + gap;
                    TextRenderer.DrawText(g, block.Sub, small, new Point(x, y + boldHeight - smallHeight), palette.Muted, palette.Background, LineFlags);
                }
            }
            y += boldHeight + rowGap;

            var x0 = pad + indent;
            foreach (var row in block.Rows)
            {
                if (draw)
                {
                    var textY = y + (rowHeight - bodyHeight) / 2;
                    var color = row.Faded ? palette.Muted : palette.Text;
                    TextRenderer.DrawText(g, row.Label, body, new Point(x0, textY), palette.Muted, palette.Background, LineFlags);
                    var barX = x0 + labelWidth + gap;
                    if (row.Value is not null)
                    {
                        TextRenderer.DrawText(g, row.Value, body, new Point(barX, textY), color, palette.Background, LineFlags);
                    }
                    else
                    {
                        if (row.Used is { } used)
                        {
                            var fill = row.Faded ? ProviderStyle.Blend(block.Accent, palette.Track, 0.5) : block.Accent;
                            DrawBar(g, new Rectangle(barX, y + (rowHeight - barHeight) / 2, barWidth, barHeight),
                                used, row.Pace, fill, palette, Math.Max(1, R(2)));
                            var percent = $"{Math.Round(used)}%";
                            TextRenderer.DrawText(g, percent, body,
                                new Rectangle(barX + barWidth + gap, textY, percentWidth, bodyHeight), color, palette.Background, LineFlags | TextFormatFlags.Right);
                        }
                        var detailX = barX + barWidth + gap + percentWidth + gap;
                        TextRenderer.DrawText(g, row.Countdown, body, new Point(detailX, textY), color, palette.Background, LineFlags);
                        if (row.Date.Length > 0)
                        {
                            TextRenderer.DrawText(g, row.Date, small,
                                new Point(detailX + Measure(row.Countdown, body).Width, textY + bodyHeight - smallHeight), palette.Muted, palette.Background, LineFlags);
                        }
                    }
                }
                y += rowHeight + rowGap;
            }

            foreach (var (message, color) in new[] { (block.Error, palette.Warn), (block.Warning, palette.Muted) })
            {
                if (message is null) continue;
                var messageWidth = contentWidth - indent;
                var measured = TextRenderer.MeasureText(g, message, small, new Size(messageWidth, int.MaxValue), WrapFlags);
                var height = Math.Min(measured.Height, smallHeight * 3);
                if (draw)
                {
                    TextRenderer.DrawText(g, message, small, new Rectangle(x0, y, messageWidth, height), color,
                        palette.Background, WrapFlags | TextFormatFlags.EndEllipsis);
                }
                y += height + rowGap;
            }
            y -= rowGap;
        }

        y += blockGap;
        var footer = view.Polling ? "更新中…" : view.LastPollAt is { } at ? $"{TimeText.Clock(at)} に更新" : "未取得";
        if (draw)
        {
            TextRenderer.DrawText(g, footer, small, new Point(pad, y), palette.Muted, palette.Background, LineFlags);
            const string Hint = "右クリックでメニュー";
            TextRenderer.DrawText(g, Hint, small, new Point(width - pad - Measure(Hint, small).Width, y), palette.Muted, palette.Background, LineFlags);
        }
        y += smallHeight + pad;
        return new Size(width, y);
    }

    static Block BuildBlock(ProviderSnapshot snapshot, DateTimeOffset now, Palette palette)
    {
        var sub = new List<string>();
        if (!string.IsNullOrEmpty(snapshot.Plan)) sub.Add(snapshot.Plan);
        if (snapshot.IsStale && snapshot.FetchedAt is { } at) sub.Add($"{TimeText.At(at)} 時点");

        var rows = new List<Row>();
        foreach (var window in snapshot.Windows)
        {
            var passed = window.ResetsAt is { } r && r <= now;
            var countdown = window.ResetsAt is { } reset ? passed ? "リセット済み" : TimeText.Until(reset, now) : "";
            var date = window.ResetsAt is { } resetAt && !passed ? $" · {TimeText.At(resetAt)}" : "";
            rows.Add(new Row(window.Label, window.UsedPercent, passed ? null : UsageMath.Elapsed(window, now),
                countdown, date, snapshot.IsStale || passed, null));
        }
        foreach (var value in snapshot.Values)
        {
            rows.Add(new Row(value.Label, null, null, "", "", snapshot.IsStale, value.Value));
        }

        var error = snapshot.Error is null ? null : snapshot.HasData ? $"最新の値を取得できない: {snapshot.Error}" : snapshot.Error;
        return new Block(ProviderStyle.Name(snapshot.Id), string.Join("  ", sub),
            ProviderStyle.Accent(snapshot.Id, palette.IsLight), rows, error, snapshot.Warning);
    }

    static void DrawBar(Graphics g, Rectangle rect, double used, double? pace, Color fill, Palette palette, int tickWidth)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(rect, rect.Height / 2f);
        using (var track = new SolidBrush(palette.Track)) g.FillPath(track, path);

        var fillWidth = (float)(rect.Width * Math.Clamp(used / 100, 0, 1));
        if (fillWidth > 0)
        {
            var state = g.Save();
            g.SetClip(path);
            using var brush = new SolidBrush(fill);
            g.FillRectangle(brush, rect.X, rect.Y, fillWidth, rect.Height);
            g.Restore(state);
        }

        if (pace is { } elapsed)
        {
            // 期間の経過位置。使用率のバーがこれより左なら、まだ使い切るペースに届いていない。
            var x = rect.X + (float)(rect.Width * elapsed);
            using var tick = new SolidBrush(Color.FromArgb(220, palette.Text));
            g.FillRectangle(tick, x - tickWidth / 2f, rect.Y - tickWidth, tickWidth, rect.Height + tickWidth * 2);
        }
    }

    static GraphicsPath RoundedRect(Rectangle rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
