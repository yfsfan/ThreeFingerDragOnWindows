using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.settings;
using ThreeFingerDragOnWindows.threefingerdrag;
using ThreeFingerDragOnWindows.touchpad;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows;
internal static class Program {
    [STAThread]
    private static void Main(string[] args) {
        using var mutex = new Mutex(true, "ThreeFingerDrag-ThinkPadRepair-2071", out var first);
        if(!first) { if(!args.Contains("--tray")) MessageBox.Show("修正版已经运行，请双击托盘图标打开。升级时请先退出旧版本。"); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new RepairWindow(args.Contains("--tray")));
    }
}
internal sealed class RepairWindow : Form {
    private readonly CheckBox enable = new() { Text = "启用三指拖动", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "开机自启动（登录后进入托盘）", AutoSize = true };
    private readonly NotifyIcon tray = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly ToolStripMenuItem trayEnable = new("启用三指拖动");
    private readonly DesktopPreferences preferences = DesktopPreferences.Load(AppContext.BaseDirectory);
    private readonly Label status = new() { AutoSize = true, Text = "正在检测触摸板…" };
    private readonly Label fingers = new() { AutoSize = true, Text = "完整帧手指数：等待输入", Font = new Font("Microsoft YaHei UI", 14) };
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Dictionary<IntPtr, DeviceState> devices = new();
    private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 100 };
    private long lastFrame;
    private string latest = "";
    private int latestCount;
    private bool disposed;
    private bool exiting;
    private bool updatingStartup;
    private bool initialized;
    private readonly bool startInTray;

    public RepairWindow(bool startInTray = false) {
        this.startInTray = startInTray;
        Text = "ThreeFingerDrag — ThinkPad 修正版 2.0.7.3";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        ClientSize = new Size(780, 540);
        Font = new Font("Microsoft YaHei UI", 10);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "最小化或关闭窗口会收进托盘，三指拖动继续运行。\r\n双击托盘图标重新打开；右键菜单可暂停拖动或退出程序。", AutoSize = false }, 0, 0);
        layout.Controls.Add(status, 0, 1);
        var toggles = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        toggles.Controls.Add(enable);
        toggles.Controls.Add(startup);
        layout.Controls.Add(toggles, 0, 2);
        layout.Controls.Add(fingers, 0, 3);
        layout.Controls.Add(details, 0, 4);
        var save = new Button { Text = "保存诊断日志", AutoSize = true };
        save.Click += (_, _) => {
            using var dialog = new SaveFileDialog { FileName = "ThinkPadRepair-log.txt", Filter = "文本日志|*.txt", InitialDirectory = AppContext.BaseDirectory };
            if(dialog.ShowDialog(this) != DialogResult.OK) return;
            try { Logger.Export(dialog.FileName); MessageBox.Show(this, "日志已保存。"); }
            catch(Exception ex) { MessageBox.Show(this, "保存失败：" + ex.Message); }
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(save);
        var hide = new Button { Text = "收进托盘", AutoSize = true };
        hide.Click += (_, _) => HideToTray();
        var quit = new Button { Text = "退出程序", AutoSize = true };
        quit.Click += (_, _) => ExitApplication();
        actions.Controls.Add(hide);
        actions.Controls.Add(quit);
        layout.Controls.Add(actions, 0, 5);
        layout.Controls.Add(new TextBox { Multiline = true, Dock = DockStyle.Fill, Text = "三指选择测试区\r\n把指针放在这段文字上，三指同时放下并移动，尝试选择文字。\r\n抬起后稍等半秒，拖动应释放。退出程序请使用按钮或托盘右键菜单。" }, 0, 6);
        Controls.Add(layout);
        trayMenu.Items.Add("打开窗口", null, (_, _) => RestoreWindow());
        trayMenu.Items.Add(trayEnable);
        trayEnable.Click += (_, _) => { if(enable.Enabled) enable.Checked = !enable.Checked; };
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出程序", null, (_, _) => ExitApplication());
        tray.Icon = Icon;
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += (_, _) => RestoreWindow();
        enable.Checked = preferences.DragEnabled;
        startup.Checked = StartupRegistration.IsEnabled();
        UpdateTrayState();
        enable.CheckedChanged += (_, _) => {
            ResetEngines();
            preferences.DragEnabled = enable.Checked;
            try { preferences.Save(AppContext.BaseDirectory); }
            catch(Exception ex) { status.Text = "拖动状态已切换，保存设置失败：" + ex.Message; Logger.Log(ex.ToString()); }
            UpdateTrayState();
        };
        startup.CheckedChanged += (_, _) => {
            if(updatingStartup) return;
            try { StartupRegistration.SetEnabled(startup.Checked); }
            catch(Exception ex) {
                updatingStartup = true;
                startup.Checked = StartupRegistration.IsEnabled();
                updatingStartup = false;
                MessageBox.Show(this, "更改自启动失败：" + ex.Message);
            }
        };
        displayTimer.Tick += (_, _) => {
            var active = Environment.TickCount64 - lastFrame < 300;
            fingers.Text = active ? $"完整帧手指数：{latestCount}" : "完整帧手指数：等待输入";
            if(details.Text != latest) details.Text = latest;
        };
        Load += (_, _) => {
            var found = TouchpadHelper.Exists();
            var registered = TouchpadHelper.RegisterInput(Handle);
            status.Text = found && registered ? "触摸板已识别，输入监听已启用。" : "触摸板检测或输入监听失败，请保存日志。";
            Logger.Log($"ThinkPadRepair 2.0.7.3; found={found}; registered={registered}; process64={Environment.Is64BitProcess}; hwnd={Handle}");
            enable.Enabled = found && registered;
            UpdateTrayState();
            tray.Visible = true;
            initialized = true;
            displayTimer.Start();
            if(this.startInTray) BeginInvoke((Action)HideToTray);
        };
        Resize += (_, _) => { if(initialized && WindowState == FormWindowState.Minimized) HideToTray(); };
        FormClosing += (_, e) => {
            if(!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); }
        };
        FormClosed += (_, _) => {
            disposed = true;
            displayTimer.Stop(); displayTimer.Dispose();
            foreach(var state in devices.Values) state.Engine.Dispose();
            tray.Visible = false; tray.Dispose(); trayMenu.Dispose();
        };
    }

    private void UpdateTrayState() {
        trayEnable.Checked = enable.Checked;
        trayEnable.Enabled = enable.Enabled;
        tray.Text = enable.Checked ? "ThinkPad 三指拖动 — 已启用" : "ThinkPad 三指拖动 — 已暂停";
    }
    private void HideToTray() {
        if(disposed) return;
        // Hide already removes the taskbar button. Changing ShowInTaskbar would
        // recreate the HWND and invalidate its Raw Input registration.
        Hide();
        Logger.Log($"Hidden to tray; hwnd={Handle}; dragging={enable.Checked}");
    }
    private void RestoreWindow() {
        if(disposed) return;
        Show(); WindowState = FormWindowState.Normal; Activate();
        Logger.Log($"Restored from tray; hwnd={Handle}; dragging={enable.Checked}");
        updatingStartup = true;
        startup.Checked = StartupRegistration.IsEnabled();
        updatingStartup = false;
    }
    private void ExitApplication() { exiting = true; Close(); }

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        if(initialized && !disposed) {
            var registered = TouchpadHelper.RegisterInput(Handle);
            Logger.Log($"Window recreated; input re-registered={registered}; hwnd={Handle}");
            if(!registered) status.Text = "重新绑定触摸板输入失败，请保存日志。";
        }
    }

    private void ResetEngines() {
        foreach(var state in devices.Values) { state.Engine.Dispose(); state.Engine = new ThreeFingerDrag(); state.Previous = Array.Empty<TouchpadContact>(); state.Last = 0; }
        Logger.Log("Dragging enabled=" + enable.Checked);
    }

    protected override void WndProc(ref Message message) {
        if(!disposed && message.Msg == TouchpadHelper.WM_INPUT) {
            try {
                foreach(var (device, contacts, count) in TouchpadHelper.ParseInput(message.LParam)) {
                    var info = TouchpadHelper.GetDeivceInfo(device);
                    if(info == null && !TouchpadHelper.Exists(device)) continue;
                    info = TouchpadHelper.GetDeivceInfo(device);
                    if(!enable.Enabled) {
                        enable.Enabled = true;
                        status.Text = "触摸板已识别，输入监听已启用。";
                        UpdateTrayState();
                    }
                    if(!devices.TryGetValue(device, out var state)) {
                        devices[device] = state = new DeviceState();
                        App.SettingsData.ThreeFingerDeviceDragCursorConfigs[info.deviceId ?? "default"] = new SettingsData.ThreeFingerDragConfig();
                    }
                    var complete = state.Frames.Accept(count, contacts);
                    if(complete == null) continue;
                    var now = Environment.TickCount64;
                    var frame = complete.ToArray();
                    lastFrame = now;
                    latestCount = frame.Length;
                    latest = $"原始报告计数：{count}　完整帧：{frame.Length}\r\n" + string.Join("\r\n", complete);
                    Logger.Log("Complete real contact frame: " + string.Join(", ", complete));
                    if(enable.Checked) state.Engine.OnTouchpadContact(device, state.Previous, frame, Math.Max(1, now - state.Last));
                    state.Previous = frame;
                    state.Last = now;
                }
            } catch(Exception ex) {
                enable.Checked = false;
                latest = "输入处理异常，已停止拖动。\r\n" + ex.Message;
                Logger.Log(ex.ToString());
            }
        } else if(message.Msg == TouchpadHelper.WM_INPUT_DEVICE_CHANGE && devices != null) {
            if(devices.Remove(message.LParam, out var state)) state.Engine.Dispose();
        }
        base.WndProc(ref message);
    }

    private sealed class DeviceState {
        public readonly ContactFrameAssembler Frames = new();
        public ThreeFingerDrag Engine = new();
        public TouchpadContact[] Previous = Array.Empty<TouchpadContact>();
        public long Last;
    }
}
