namespace LimitResetWatch;

/// <summary>常に最前面に出る小さな窓。フォーカスを奪わず、Alt+Tab にも出ない。ドラッグで動かせる。</summary>
internal sealed class FloatingWindow : Form
{
    FloatingView view = new([], DateTimeOffset.Now, null, false);
    Palette palette = Palette.Dark;
    bool roundedByDwm;
    bool allowClose;

    /// <summary>ドラッグで動かし終えたとき。</summary>
    public event EventHandler? PositionCommitted;

    public FloatingWindow(ContextMenuStrip menu)
    {
        Text = "LimitResetWatch";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ContextMenuStrip = menu;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    float UiScale => DeviceDpi / 96f;

    public void SetView(FloatingView newView, Palette newPalette)
    {
        var paletteChanged = newPalette != palette;
        view = newView;
        palette = newPalette;
        if (paletteChanged && IsHandleCreated) roundedByDwm = Native.TryRoundCorners(Handle, palette.Border);
        Relayout();
    }

    void Relayout()
    {
        using var g = CreateGraphics();
        var size = FloatingRenderer.Render(g, UiScale, palette, view, draw: false);
        if (ClientSize != size)
        {
            ClientSize = size;
            ClampToScreen();
        }
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        roundedByDwm = Native.TryRoundCorners(Handle, palette.Border);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(palette.Background);
        FloatingRenderer.Render(e.Graphics, UiScale, palette, view, draw: true);
        if (!roundedByDwm)
        {
            using var pen = new Pen(palette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == Native.WM_EXITSIZEMOVE)
        {
            ClampToScreen();
            PositionCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 終了はメニューから。Alt+F4 などでは隠すだけにする。
        if (!allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    public void CloseForExit()
    {
        allowClose = true;
        Close();
    }

    /// <summary>画面の外にはみ出さないように動かす（画面構成が変わったときや、中身が増えたとき）。</summary>
    public void ClampToScreen()
    {
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        var x = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        var y = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        if (x != Left || y != Top) Location = new Point(x, y);
    }

    /// <summary>既定の位置（主画面の右下、タスクバーの上）。</summary>
    public void PlaceDefault()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        var margin = (int)Math.Round(12 * UiScale);
        Location = new Point(area.Right - Width - margin, area.Bottom - Height - margin);
    }
}
