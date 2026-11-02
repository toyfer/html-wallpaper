using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HtmlWallpaper;

internal static class Native
{
    public const int WmSpawnWorker = 0x052C;
    public const int SmtoNormal = 0x0000;
    public const int SmtoAbortIfHung = 0x0002;
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
            new IntPtr(0xD),
            IntPtr.Zero,
            Native.SmtoNormal | Native.SmtoAbortIfHung,
            1000,
            out _);

        IntPtr worker = IntPtr.Zero;
        Native.EnumWindows((top, _) =>
        {
            var def = Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (def != IntPtr.Zero)
            {
                var next = Native.FindWindowEx(top, IntPtr.Zero, "WorkerW", null);
                if (next != IntPtr.Zero)
                {
                    worker = next;
                }
            }

            return true;
        }, IntPtr.Zero);

        if (worker == IntPtr.Zero)
        {
            worker = Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        }

        if (worker == IntPtr.Zero)
        {
            throw new InvalidOperationException("WorkerW not found.");
        }

        return worker;
    }

    public static void Attach(IntPtr hwnd, IntPtr worker)
    {
        Native.SetParent(hwnd, worker);
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Native.SetWindowPos(hwnd, Native.HwndBottom, 0, 0, bounds.Width, bounds.Height, 0x0040);
        Native.ShowWindow(hwnd, 5);
    }

    public static void Detach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Native.SetParent(hwnd, IntPtr.Zero);
    }
}

internal sealed class WallpaperForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly string _root;
    private readonly string _htmlPath;
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
        Controls.Add(_web);
    }

    public async Task StartAsync()
    {
        var data = Path.Combine(_root, "webview2-data");
        Directory.CreateDirectory(data);
        var env = await CoreWebView2Environment.CreateAsync(null, data);
        await _web.EnsureCoreWebView2Async(env);
        _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _web.CoreWebView2.Navigate(new Uri(_htmlPath).AbsoluteUri);
        Attach();
    }

    public void Attach()
    {
        if (IsDisposed)
        {
            return;
        }

        Show();
        DesktopHost.Attach(Handle, DesktopHost.FindWorkerW());
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
