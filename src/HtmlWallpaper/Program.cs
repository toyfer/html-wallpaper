using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HtmlWallpaper;

internal static class Native
{
    public const uint WmSpawnWorker = 0x052C;
    public const uint SmtoNormal = 0x0000;
    public const uint SmtoAbortIfHung = 0x0002;
    public const int GwlStyle = -16;
    public const int GwlExStyle = -20;
    public const long WsChild = 0x40000000L;
    public const long WsVisible = 0x10000000L;
    public const long WsCaption = 0x00C00000L;
    public const long WsSysMenu = 0x00080000L;
    public const long WsThickFrame = 0x00040000L;
    public const long WsMinimizeBox = 0x00020000L;
    public const long WsMaximizeBox = 0x00010000L;
    public const long WsPopup = unchecked((long)0x80000000L);
    public const long WsExNoActivate = 0x08000000L;
    public const long WsExToolWindow = 0x00000080L;
    public const long WsExTransparent = 0x00000020L;
    public const long WsExLayered = 0x00080000L;
    public const long WsExNoRedirectionBitmap = 0x00200000L;
    public const uint LwaAlpha = 0x00000002;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpFrameChanged = 0x0020;
    public const uint SwpShowWindow = 0x0040;
    public const uint SpiSetDeskWallpaper = 0x0014;
    public const uint SpifUpdateIniFile = 0x01;
    public const uint SpifSendChange = 0x02;
    public const int SwShowNoActivate = 4;
    public const int SwHide = 0;
    public static readonly IntPtr HwndBottom = new(1);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string? pvParam, uint fWinIni);
}

internal sealed class Config
{
    public string Html { get; set; } = "wallpaper.html";
}

internal static class DesktopHost
{
    public static IntPtr FindWorkerW()
    {
        var progman = Native.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            throw new InvalidOperationException("Progman not found.");
        }

        Native.SendMessageTimeout(
            progman,
            Native.WmSpawnWorker,
            IntPtr.Zero,
            IntPtr.Zero,
            Native.SmtoNormal | Native.SmtoAbortIfHung,
            1000,
            out _);
        Native.SendMessageTimeout(
            progman,
            Native.WmSpawnWorker,
            new IntPtr(0xD),
            new IntPtr(0x1),
            Native.SmtoNormal | Native.SmtoAbortIfHung,
            1000,
            out _);

        var child = Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (child != IntPtr.Zero)
        {
            return child;
        }

        IntPtr sibling = IntPtr.Zero;
        Native.EnumWindows((top, _) =>
        {
            var def = Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (def != IntPtr.Zero)
            {
                var next = Native.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                if (next != IntPtr.Zero)
                {
                    sibling = next;
                    return false;
                }
            }

            return true;
        }, IntPtr.Zero);

        if (sibling != IntPtr.Zero)
        {
            return sibling;
        }

        throw new InvalidOperationException("WorkerW not found.");
    }

    public static bool IsRaisedDesktop()
    {
        var progman = Native.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return false;
        }

        var ex = Native.GetWindowLongPtr(progman, Native.GwlExStyle).ToInt64();
        if ((ex & Native.WsExNoRedirectionBitmap) != 0)
        {
            return true;
        }

        return Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
    }

    public static void Attach(IntPtr hwnd)
    {
        if (!Native.IsWindow(hwnd))
        {
            throw new InvalidOperationException("Wallpaper window handle is gone.");
        }

        var worker = FindWorkerW();
        var progman = Native.FindWindow("Progman", null);
        var raised = IsRaisedDesktop();
        var parent = raised && progman != IntPtr.Zero ? progman : worker;

        var style = Native.GetWindowLongPtr(hwnd, Native.GwlStyle).ToInt64();
        style &= ~(Native.WsCaption | Native.WsThickFrame | Native.WsSysMenu | Native.WsMinimizeBox | Native.WsMaximizeBox | Native.WsPopup);
        style |= Native.WsChild | Native.WsVisible;
        Native.SetWindowLongPtr(hwnd, Native.GwlStyle, new IntPtr(style));

        var ex = Native.GetWindowLongPtr(hwnd, Native.GwlExStyle).ToInt64();
        ex |= Native.WsExNoActivate | Native.WsExToolWindow | Native.WsExTransparent | Native.WsExLayered;
        Native.SetWindowLongPtr(hwnd, Native.GwlExStyle, new IntPtr(ex));
        Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LwaAlpha);

        Native.SetParent(hwnd, parent);

        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        var defView = Native.FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null);
        var insertAfter = raised && defView != IntPtr.Zero ? defView : Native.HwndBottom;
        Native.SetWindowPos(
            hwnd,
            insertAfter,
            0,
            0,
            bounds.Width,
            bounds.Height,
            Native.SwpNoActivate | Native.SwpShowWindow | Native.SwpFrameChanged);
        Native.ShowWindow(hwnd, Native.SwShowNoActivate);
    }

    public static void Detach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd))
        {
            return;
        }

        Native.ShowWindow(hwnd, Native.SwHide);
        Native.SetParent(hwnd, IntPtr.Zero);
        Native.SystemParametersInfo(
            Native.SpiSetDeskWallpaper,
            0,
            null,
            Native.SpifUpdateIniFile | Native.SpifSendChange);
    }
}

internal sealed class WallpaperForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly string _root;
    private readonly string _htmlPath;
    private bool _ready;
    private bool _attached;

    public WallpaperForm(string root, string htmlPath)
    {
        _root = root;
        _htmlPath = htmlPath;
        Text = "HtmlWallpaper";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        BackColor = Color.Black;
        ShowIcon = false;
        Controls.Add(_web);
    }

    public async Task StartAsync()
    {
        if (IsDisposed)
        {
            return;
        }

        if (!_ready)
        {
            var data = Path.Combine(_root, "webview2-data");
            Directory.CreateDirectory(data);
            var env = await CoreWebView2Environment.CreateAsync(null, data);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _web.CoreWebView2.Navigate(new Uri(_htmlPath).AbsoluteUri);
            _ready = true;
        }

        Attach();
    }

    public void Attach()
    {
        if (IsDisposed)
        {
            return;
        }

        Show();
        DesktopHost.Attach(Handle);
        _attached = true;
    }

    public void StopWallpaper()
    {
        if (!_attached)
        {
            return;
        }

        DesktopHost.Detach(Handle);
        _attached = false;
        Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopWallpaper();
        _web.Dispose();
        base.OnFormClosing(e);
    }
}

internal static class SingleInstance
{
    public const string MutexName = "Local\\HtmlWallpaper.toyfer.v1";
    public const string PipeName = "HtmlWallpaper.toyfer.v1";

    public static void SendQuit()
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        client.Connect(800);
        using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
        writer.WriteLine("quit");
    }

    public static void Listen(Action onQuit, CancellationToken token)
    {
        Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var line = reader.ReadLine();
                    if (string.Equals(line, "quit", StringComparison.OrdinalIgnoreCase))
                    {
                        onQuit();
                    }
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }, token);
    }
}

internal static class App
{
    [STAThread]
    private static void Main(string[] args)
    {
        var quitRequested = args.Any(a => string.Equals(a, "--quit", StringComparison.OrdinalIgnoreCase));
        using var mutex = new Mutex(true, SingleInstance.MutexName, out var created);
        if (!created)
        {
            if (quitRequested)
            {
                try { SingleInstance.SendQuit(); }
                catch (TimeoutException) { }
                catch (IOException) { }
            }

            return;
        }

        if (quitRequested)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        var root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPath = Path.Combine(root, "config.json");
        var config = new Config();
        if (File.Exists(configPath))
        {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath)) ?? config;
        }

        var html = Path.GetFullPath(Path.Combine(root, config.Html));
        if (!html.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(html))
        {
            html = Path.Combine(root, "wallpaper.html");
        }

        var form = new WallpaperForm(root, html);
        var cts = new CancellationTokenSource();
        NotifyIcon? tray = null;
        var quitting = false;

        void Shutdown()
        {
            if (quitting)
            {
                return;
            }

            quitting = true;
            cts.Cancel();
            form.StopWallpaper();
            if (tray is not null)
            {
                tray.Visible = false;
                tray.Dispose();
            }

            form.Close();
            Application.Exit();
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Start", null, async (_, _) =>
        {
            try { await form.StartAsync(); }
            catch (Exception ex) { ShowError(ex); }
        });
        menu.Items.Add("Stop", null, (_, _) => form.StopWallpaper());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());

        tray = new NotifyIcon
        {
            Text = "HtmlWallpaper",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };

        tray.DoubleClick += async (_, _) =>
        {
            try { await form.StartAsync(); }
            catch (Exception ex) { ShowError(ex); }
        };

        SingleInstance.Listen(() =>
        {
            if (form.IsHandleCreated)
            {
                form.BeginInvoke(Shutdown);
            }
            else
            {
                Shutdown();
            }
        }, cts.Token);

        form.Load += async (_, _) =>
        {
            form.Hide();
            try { await form.StartAsync(); }
            catch (Exception ex) { ShowError(ex); }
        };

        Application.ApplicationExit += (_, _) =>
        {
            if (!quitting)
            {
                Shutdown();
            }
        };

        Application.Run(form);
    }

    private static void ShowError(Exception ex)
    {
        MessageBox.Show(ex.Message, "HtmlWallpaper", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
