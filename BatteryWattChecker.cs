using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Management;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Battery Watt Checker")]
[assembly: AssemblyProduct("Battery Watt Checker")]
[assembly: AssemblyCompany("LiveTrack")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyDescription("배터리 충전 전력을 작업 표시줄에 표시합니다.")]

internal sealed class BatteryWattChecker : ApplicationContext
{
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private readonly ToolStripMenuItem reading = new ToolStripMenuItem();
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private Icon currentIcon;
    private string currentBadge;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    private BatteryWattChecker()
    {
        menu.Items.Add("Battery Watt Checker  v" + Updates.Current.ToString(3)).Enabled = false;
        menu.Items.Add("Created by LiveTrack").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        reading.Enabled = false;
        menu.Items.Add(reading);
        menu.Items.Add(new ToolStripSeparator());
        var update = new ToolStripMenuItem("업데이트 확인…");
        menu.Items.Add(update);
        update.Click += async delegate {
            update.Enabled = false;
            try {
                var release = await Updates.Latest();
                if (release.Version <= Updates.Current) {
                    MessageBox.Show("현재 최신 버전입니다.  v" + Updates.Current.ToString(3), "Battery Watt Checker");
                    return;
                }
                if (MessageBox.Show("v" + release.Version.ToString(3) + " 버전을 설치하고 다시 시작할까요?",
                    "Battery Watt Checker", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                await Updates.Stage(release);
                Process.Start(Updates.PendingPath, "--install-update " + Process.GetCurrentProcess().Id);
                ExitThread();
            } catch (Exception ex) {
                MessageBox.Show(ex.Message, "업데이트를 완료하지 못했습니다");
            } finally { if (!update.IsDisposed) update.Enabled = true; }
        };
        var startup = new ToolStripMenuItem("로그인할 때 자동 시작");
        menu.Items.Add(startup);
        menu.Opening += delegate {
            startup.Checked = String.Equals(Registry.GetValue(@"HKEY_CURRENT_USER\" + RunKey,
                "BatteryWattChecker", null) as string, QuotedPath, StringComparison.OrdinalIgnoreCase);
        };
        startup.Click += delegate {
            try {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) {
                    if (startup.Checked) key.DeleteValue("BatteryWattChecker", false);
                    else key.SetValue("BatteryWattChecker", QuotedPath);
                }
            } catch (Exception ex) { MessageBox.Show(ex.Message, "자동 시작 설정"); }
        };
        menu.Items.Add("종료", null, delegate { ExitThread(); });
        tray.ContextMenuStrip = menu;
        RefreshReading();
        tray.Visible = true;
        timer.Interval = 5000;
        timer.Tick += delegate { RefreshReading(); };
        timer.Start();
    }

    private static string QuotedPath { get { return "\"" + Application.ExecutablePath + "\""; } }

    private void RefreshReading()
    {
        try {
            using (var query = new ManagementObjectSearcher(@"root\wmi",
                "SELECT Active, PowerOnline, Charging, Discharging, ChargeRate, DischargeRate FROM BatteryStatus"))
            using (var rows = query.Get()) {
                foreach (ManagementObject row in rows) using (row) {
                    if (!Convert.ToBoolean(row["Active"])) continue;
                    bool discharging = Convert.ToBoolean(row["Discharging"]);
                    bool charging = Convert.ToBoolean(row["Charging"]);
                    bool online = Convert.ToBoolean(row["PowerOnline"]);
                    string state = discharging ? "배터리 사용" : charging ? "배터리 충전" :
                        online ? "전원 연결 · 충전 대기" : "배터리 대기";
                    object raw = row[discharging ? "DischargeRate" : "ChargeRate"];
                    long mw = raw == null ? -1 : Convert.ToInt64(raw);
                    if (!charging && !discharging) mw = 0;
                    if (mw < 0 || mw >= Int32.MaxValue) break;
                    double watts = mw / 1000.0;
                    string sign = discharging && mw > 0 ? "−" : "";
                    string badge = sign + Math.Round(watts, MidpointRounding.AwayFromZero)
                        .ToString(CultureInfo.InvariantCulture);
                    ShowReading(badge, state + " " + watts.ToString("0.0") + " W",
                        charging ? Color.FromArgb(15, 104, 70) : Color.FromArgb(55, 65, 82));
                    return;
                }
            }
        } catch (ManagementException) {
        } catch (COMException) {
        } catch (UnauthorizedAccessException) {
        }
        ShowReading("?", "배터리 전력 측정값 없음", Color.DimGray);
    }

    private void ShowReading(string badge, string description, Color color)
    {
        tray.Text = description;
        reading.Text = description;
        string key = badge + color.ToArgb();
        if (key == currentBadge) return;
        using (var bitmap = new Bitmap(64, 64))
        using (var g = Graphics.FromImage(bitmap))
        using (var format = new StringFormat(StringFormat.GenericTypographic))
        using (var font = new Font("Segoe UI", badge.Length > 2 ? 31 : 45, FontStyle.Bold, GraphicsUnit.Pixel)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(color);
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            format.FormatFlags |= StringFormatFlags.NoWrap;
            g.DrawString(badge, font, Brushes.White, new RectangleF(0, 0, 64, 64), format);
            IntPtr handle = bitmap.GetHicon();
            try {
                using (Icon icon = Icon.FromHandle(handle)) {
                    Icon previous = currentIcon;
                    currentIcon = (Icon)icon.Clone();
                    tray.Icon = currentIcon;
                    if (previous != null) previous.Dispose();
                }
            } finally { DestroyIcon(handle); }
        }
        currentBadge = key;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            timer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            if (currentIcon != null) currentIcon.Dispose();
            menu.Dispose();
        }
        base.Dispose(disposing);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--install-update") {
            Updates.Install(args[1]);
            return;
        }
        if (args.Length == 2 && args[0] == "--updated") Updates.Cleanup(args[1]);
        bool first;
        using (var mutex = new Mutex(true, @"Local\LiveTrack.BatteryWattChecker", out first)) {
            if (!first) return;
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            using (var context = new BatteryWattChecker()) Application.Run(context);
        }
    }
}
