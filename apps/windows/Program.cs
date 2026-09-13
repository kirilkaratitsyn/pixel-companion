using System.Diagnostics;
using System.Net;
using Microsoft.Win32;

namespace PixelCompanion;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var options = new Options(args);
        Directory.CreateDirectory(options.DataDirectory);
        using var mutex = new Mutex(true, "Local\\PixelCompanion-" + options.Port, out bool first);
        if (!first) { if (!options.Headless) MessageBox.Show("Pixel Companion уже работает. Откройте его значок в трее."); return; }
        try
        {
            if (args.Contains("--media-fixture")) { MediaFixture.Run(options.DataDirectory); return; }
            using var media = new MediaBridge();
            var pairing = new Pairing(options.DataDirectory);
            var server = new Server(pairing, media, options.Port, options.Loopback);
            try
            {
                using var context = options.Headless ? new ApplicationContext() : new ApplicationContext(new AgentForm(pairing, media, server, options));
                EventHandler? start = null;
                start = async (_, _) =>
                {
                    Application.Idle -= start;
                    try
                    {
                        File.AppendAllText(Path.Combine(options.DataDirectory, "startup.log"), "Initializing media\n");
                        await media.Initialize();
                        File.AppendAllText(Path.Combine(options.DataDirectory, "startup.log"), "Starting server\n");
                        await server.Start();
                        File.WriteAllText(Path.Combine(options.DataDirectory, "ready.json"), Wire.Serialize(new { port = options.Port, code = pairing.Code, pid = Environment.ProcessId, version = "0.2.0" }));
                    }
                    catch (Exception e) { File.WriteAllText(Path.Combine(options.DataDirectory, "error.log"), e.ToString()); context.ExitThread(); }
                };
                Application.Idle += start;
                Application.Run(context);
            }
            finally { server.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(options.DataDirectory, "error.log"), e.ToString());
            if (!options.Headless) MessageBox.Show("Не удалось запустить Pixel Companion.\n" + e.Message, "Pixel Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }
}
internal sealed class Options(string[] args)
{
    private string? Read(string key) { int index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    public int Port => int.TryParse(Read("--port"), out int port) && port > 1024 && port < 65536 ? port : 8765;
    public bool Headless => args.Contains("--headless");
    public bool Loopback => args.Contains("--loopback");
    public string DataDirectory => Read("--data-dir") ?? Path.Combine(AppContext.BaseDirectory, "data");
    public bool Tray => args.Contains("--tray");
}
internal sealed class AgentForm : Form
{
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Label status, code;
    private bool exiting;
    public AgentForm(Pairing pairing, MediaBridge media, Server server, Options options)
    {
        Text = "Pixel Companion"; ClientSize = new Size(560, 480); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        BackColor = Color.FromArgb(18, 22, 20); ForeColor = Color.Gainsboro; Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24), AutoScroll = true };
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Pixel Companion", Font = new Font("Segoe UI", 22), AutoSize = true });
        layout.Controls.Add(new Label { Text = "Музыка с Windows на вашем Pixel", AutoSize = true, Margin = new Padding(0, 0, 0, 14) });
        status = new Label { AutoSize = true, MaximumSize = new Size(490, 0), Text = "Ожидание телефона" }; layout.Controls.Add(status);
        string[] addresses = Server.Addresses();
        string url = "http://" + (addresses.FirstOrDefault() ?? "127.0.0.1") + ":" + options.Port;
        layout.Controls.Add(new Label { Text = "Адрес для телефона (та же локальная сеть)", AutoSize = true, Margin = new Padding(0, 16, 0, 4) });
        var addressBox = new TextBox { ReadOnly = true, Width = 490, Text = url }; layout.Controls.Add(addressBox);
        if (addresses.Length > 1) layout.Controls.Add(new Label { Text = "Другие IP: " + string.Join(", ", addresses.Skip(1)), AutoSize = true, MaximumSize = new Size(490, 0) });
        layout.Controls.Add(new Label { Text = "Одноразовый код · действует 5 минут", AutoSize = true, Margin = new Padding(0, 14, 0, 0) });
        code = new Label { Text = pairing.Code, AutoSize = true, Font = new Font("Consolas", 28) }; layout.Controls.Add(code);
        var buttons = new FlowLayoutPanel { AutoSize = true, Width = 490 };
        Button Button(string label, Action action) { var b = new Button { Text = label, AutoSize = true, Height = 36, BackColor = Color.FromArgb(40, 50, 44), FlatStyle = FlatStyle.Flat }; b.Click += (_, _) => action(); buttons.Controls.Add(b); return b; }
        Button("Новый код", pairing.Renew);
        Button("Открыть пульт", () => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        Button("Копировать адрес", () => Clipboard.SetText(url));
        layout.Controls.Add(buttons);
        var autorun = new CheckBox { Text = "Запускать вместе с Windows", AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) autorun.Checked = key?.GetValue("PixelCompanion") != null;
        autorun.CheckedChanged += (_, _) =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (autorun.Checked) key.SetValue("PixelCompanion", "\"" + Environment.ProcessPath + "\" --tray"); else key.DeleteValue("PixelCompanion", false);
        };
        layout.Controls.Add(autorun);
        layout.Controls.Add(new Label { Text = "v0.2 · Для доверенной домашней сети. Данные идут по HTTP/WS.\nЗакрытие окна оставляет программу в трее.", AutoSize = true, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 12, 0, 0) });
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Pixel Companion", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => Restore());
        menu.Items.Add("Новый код", null, (_, _) => { pairing.Renew(); Restore(); });
        menu.Items.Add("Отвязать все устройства", null, (_, _) => { pairing.Revoke(); Restore(); });
        menu.Items.Add("Выход", null, (_, _) => { exiting = true; Close(); });
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Restore();
        timer.Tick += (_, _) => { code.Text = pairing.Code; status.Text = $"Подключено: {server.ClientCount} · Сохранённых устройств: {pairing.DeviceCount}\n" + (media.Latest.Media.SessionId == null ? "Запустите музыку на компьютере" : media.Latest.Media.Source + " · " + media.Latest.Media.Title); };
        timer.Start();
        Shown += (_, _) => { if (options.Tray) Hide(); };
        FormClosing += (_, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }
    private void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); tray.Dispose(); } base.Dispose(disposing); }
}
