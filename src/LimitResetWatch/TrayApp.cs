using Microsoft.Win32;

namespace LimitResetWatch;

/// <summary>常駐アプリ本体。サービスごとのトレイアイコンと浮かぶ窓を持ち、定期的に使用量を取り直す。</summary>
internal sealed class TrayApp : ApplicationContext
{
    static readonly int[] IntervalChoices = [1, 5, 15, 30];

    readonly AppSettings settings = AppSettings.Load();
    readonly UsagePoller poller;
    readonly ContextMenuStrip menu;
    readonly FloatingWindow floating;
    readonly Dictionary<ProviderId, TrayIconSlot> slots = [];
    readonly TrayIconSlot fallbackSlot;
    readonly System.Windows.Forms.Timer tickTimer = new() { Interval = 30_000 };
    readonly System.Windows.Forms.Timer pollTimer = new();
    ToolStripMenuItem floatingItem = null!;
    ToolStripMenuItem startupItem = null!;
    ToolStripMenuItem intervalItem = null!;

    public TrayApp()
    {
        poller = new UsagePoller(ProviderRegistry.CreateAll(), SnapshotStore.Load(), persist: true);
        menu = BuildMenu();

        // Windows はトレイアイコンの「常に表示」を、実行ファイルとアイコンの識別番号の組で覚える。
        // WinForms の NotifyIcon は作った順に 1, 2, 3… と番号を振るので、起動直後に全部のアイコンを
        // 決まった順（Claude 1、Codex 2、Gemini 3、Grok 4、アプリのアイコン 5）で作り、以後は作り直さずに
        // 表示・非表示だけを切り替える。ここより前に NotifyIcon を作らないこと。
        foreach (var id in ProviderStyle.Order.Where(ProviderStyle.InTray))
        {
            var slot = new TrayIconSlot(menu);
            slot.Clicked += ToggleFloating;
            slots[id] = slot;
        }
        fallbackSlot = new TrayIconSlot(menu);
        fallbackSlot.Clicked += ToggleFloating;

        floating = new FloatingWindow(menu);
        floating.PositionCommitted += (_, _) =>
        {
            settings.FloatingX = floating.Left;
            settings.FloatingY = floating.Top;
            settings.Save();
        };

        UpdateUi();
        if (settings.FloatingX is { } x && settings.FloatingY is { } y)
        {
            floating.Location = new Point(x, y);
            floating.ClampToScreen();
        }
        else
        {
            floating.PlaceDefault();
        }
        if (settings.FloatingVisible) floating.Show();

        poller.Changed += UpdateUi;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // 残り時間の表示は値を取り直さなくても進むので、短い間隔で描き直す。
        tickTimer.Tick += (_, _) => UpdateUi();
        tickTimer.Start();
        pollTimer.Interval = settings.PollMinutes * 60_000;
        pollTimer.Tick += (_, _) => _ = poller.PollAsync();
        pollTimer.Start();

        // メッセージループが回り始めてから最初の取得を始める。
        var firstPoll = new System.Windows.Forms.Timer { Interval = 300 };
        firstPoll.Tick += (_, _) =>
        {
            firstPoll.Stop();
            firstPoll.Dispose();
            _ = poller.PollAsync();
        };
        firstPoll.Start();
    }

    ContextMenuStrip BuildMenu()
    {
        var contextMenu = new ContextMenuStrip();
        floatingItem = new ToolStripMenuItem("浮かぶ窓を表示", null, (_, _) => ToggleFloating());
        var refreshItem = new ToolStripMenuItem("今すぐ更新", null, (_, _) => _ = poller.PollAsync());
        intervalItem = new ToolStripMenuItem("更新間隔");
        foreach (var minutes in IntervalChoices)
        {
            intervalItem.DropDownItems.Add(new ToolStripMenuItem($"{minutes} 分ごと", null, (_, _) => SetInterval(minutes))
            {
                Tag = minutes,
            });
        }
        startupItem = new ToolStripMenuItem("Windows の起動時に開始", null, (_, _) => Startup.Set(!Startup.IsEnabled()));
        var exitItem = new ToolStripMenuItem("終了", null, (_, _) => Exit());
        contextMenu.Items.AddRange(new ToolStripItem[]
        {
            floatingItem, refreshItem, intervalItem, startupItem, new ToolStripSeparator(), exitItem,
        });
        contextMenu.Opening += (_, _) =>
        {
            floatingItem.Checked = floating.Visible;
            startupItem.Checked = Startup.IsEnabled();
            refreshItem.Enabled = !poller.IsPolling;
            foreach (ToolStripMenuItem item in intervalItem.DropDownItems)
            {
                item.Checked = item.Tag is int minutes && minutes == settings.PollMinutes;
            }
        };
        return contextMenu;
    }

    void UpdateUi()
    {
        var now = DateTimeOffset.Now;
        var lightTaskbar = Theme.TaskbarLight;

        var shown = 0;
        foreach (var (id, slot) in slots)
        {
            var snapshot = poller.Get(id);
            if (snapshot is null || !UsageMath.IsVisible(snapshot))
            {
                slot.Hide();
                continue;
            }
            var (model, tooltip) = TrayPresenter.Build(snapshot, now, lightTaskbar);
            slot.Update(model, tooltip);
            shown++;
        }

        // サービスのアイコンが 1 つも無いときも、メニューを開けるようにアプリのアイコンを出しておく。
        if (shown == 0)
        {
            var tooltip = poller.IsPolling || poller.LastPollAt is null
                ? "LimitResetWatch（取得中…）"
                : "LimitResetWatch（使用量を取得できるサービスがない）";
            fallbackSlot.Update(TrayPresenter.Fallback(lightTaskbar), tooltip);
        }
        else
        {
            fallbackSlot.Hide();
        }

        var visible = poller.All.Where(UsageMath.IsVisible).ToList();
        floating.SetView(new FloatingView(visible, now, poller.LastPollAt, poller.IsPolling),
            Theme.AppsLight ? Palette.Light : Palette.Dark);
    }

    void ToggleFloating()
    {
        if (floating.Visible)
        {
            floating.Hide();
        }
        else
        {
            floating.Show();
            floating.ClampToScreen();
        }
        settings.FloatingVisible = floating.Visible;
        settings.Save();
    }

    void SetInterval(int minutes)
    {
        settings.PollMinutes = minutes;
        settings.Save();
        pollTimer.Interval = minutes * 60_000;
    }

    void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (floating.IsHandleCreated) floating.BeginInvoke(UpdateUi);
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (floating.IsHandleCreated) floating.BeginInvoke(floating.ClampToScreen);
    }

    void Exit()
    {
        tickTimer.Stop();
        pollTimer.Stop();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        foreach (var slot in slots.Values) slot.Dispose();
        slots.Clear();
        fallbackSlot.Dispose();
        floating.CloseForExit();
        ExitThread();
    }
}
