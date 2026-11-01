using EarTrumpet;
using EarTrumpet.DataModel.Storage;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

internal static class Program
{
    private static readonly string Root = AppContext.BaseDirectory;
    private static App _app;
    private static Window _window;
    private static WebView2 _web;
    private static bool _finished;
    private static bool _recording;
    private static int _phase;

    [STAThread]
    private static int Main()
    {
        if (Environment.GetEnvironmentVariable("BETTERTRUMPET_UI_TEST_GUEST") != "1" || !Directory.Exists(@"C:\Coordinator\Work"))
        {
            Console.Error.WriteLine("This native UI regression runs only in the owned isolated guest.");
            return 2;
        }
        File.WriteAllText(Path.Combine(Root, "portable.marker"), "Isolated UI verification");
        // A headless SYSTEM guest has no Explorer-created user personalization keys.
        // Seed the standard profile containers in this disposable guest only.
        using (var user = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry64))
        {
            using var personalize = user.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            personalize.SetValue("AppsUseLightTheme", 0);
            personalize.SetValue("SystemUsesLightTheme", 0);
            personalize.SetValue("EnableTransparency", 0);
            using var accessibility = user.CreateSubKey(@"Control Panel\Accessibility");
            using var dwm = user.CreateSubKey(@"Software\Microsoft\Windows\DWM");
        }
        var bag = StorageFactory.GetSettings();
        bag.Set("hasShownFirstRun", true);
        bag.Set("LastSeenVersion", System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(App).Assembly.Location).ProductVersion);
        bag.Set("AutoCheckForUpdates", false);
        bag.Set("AnnouncementsEnabled", false);
        bag.Set("IsTelemetryEnabled", false);
        _app = new App();
        _app.InitializeComponent();
        _app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Finish(1, e.Exception.ToString()); };
        _app.Startup += (_, __) => _app.Dispatcher.BeginInvoke(new Action(OpenSettings), DispatcherPriority.ContextIdle);
        var result = _app.Run();
        return _finished ? result : 1;
    }

    private static void OpenSettings()
    {
        try
        {
            _window = (Window)typeof(App).GetMethod("CreateSettingsExperience", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_app, null);
            _window.Width = 960;
            _window.Height = 680;
            _web = (WebView2)_window.FindName("SettingsWebView");
            _web.CoreWebView2InitializationCompleted += (_, e) =>
            {
                if (!e.IsSuccess) { Finish(1, e.InitializationException.ToString()); return; }
                _web.CoreWebView2.WebMessageReceived += OnMessage;
                _web.CoreWebView2.NavigationCompleted += async (_, navigation) =>
                {
                    if (!navigation.IsSuccess) { Finish(1, "Navigation failed: " + navigation.WebErrorStatus); return; }
                    try { await Probe(); } catch (Exception ex) { Finish(1, ex.ToString()); }
                };
            };
            _window.Show();
        }
        catch (Exception ex) { Finish(1, ex.ToString()); }
    }

    private static Task Probe() => _web.CoreWebView2.ExecuteScriptAsync(
        "window.__verificationMinimum=" + (_phase == 0 ? 900 : 650) + ";window.__verificationMaximum=" + (_phase == 0 ? 960 : 700) + ";" + File.ReadAllText(Path.Combine(Root, "Probe.js")));

    private static async void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var json = JsonDocument.Parse(args.WebMessageAsJson);
        if (!json.RootElement.TryGetProperty("type", out var type) || type.GetString() != "verificationSnapshot" || _recording) return;
        _recording = true;
        var snapshot = json.RootElement.GetRawText();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            var viewport = _phase == 0 ? "wide" : "minimum";
            File.WriteAllText(Path.Combine(Root, viewport + ".json"), snapshot);
            var data = json.RootElement;
            if (!data.GetProperty("url").GetString().StartsWith("https://bettertrumpet.settings/index.html")) throw new Exception("Wrong served settings URL.");
            if (data.GetProperty("buttons").GetArrayLength() != 2) throw new Exception("Caption controls are missing.");
            var width = data.GetProperty("width").GetDouble();
            double previousRight = -1;
            foreach (var button in data.GetProperty("buttons").EnumerateArray())
            {
                if (button.GetProperty("width").GetDouble() != 30 || button.GetProperty("height").GetDouble() != 30 || button.GetProperty("label").GetString().Length == 0) throw new Exception("Caption size/accessibility changed.");
                var left = button.GetProperty("left").GetDouble();
                var right = button.GetProperty("right").GetDouble();
                if (left < previousRight || left < 0 || right > width) throw new Exception("Caption controls overlap or clip.");
                previousRight = right;
            }
            if (!data.GetProperty("hasDeviceToggle").GetBoolean() || data.GetProperty("horizontalOverflow").GetBoolean()) throw new Exception("Device preference missing or settings overflow horizontally.");
            using (var stream = File.Create(Path.Combine(Root, viewport + ".png")))
                await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            if (_phase++ == 0)
            {
                _window.Width = 700; // The supported native minimum, not an artificial mobile viewport.
                _window.Height = 600;
                _recording = false;
                await Probe();
                return;
            }
            // Invoke the real DOM switch. The production web message bridge must update settings.
            await TogglePreference(true);
            await TogglePreference(false);
            Finish(0, "Native WebView settings rendered at supported wide/minimum sizes; real switch bridge passed.");
        }
        catch (Exception ex) { Finish(1, ex.ToString()); }
    }

    private static async Task TogglePreference(bool expected)
    {
        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action onChanged = () => { if (App.Settings.TrayIconShowsDeviceType == expected) changed.TrySetResult(true); };
        App.Settings.TrayIconShowsDeviceTypeChanged += onChanged;
        try
        {
            await _web.CoreWebView2.ExecuteScriptAsync("document.getElementById('setting-trayIconShowsDeviceType').click()");
            await changed.Task;
            var script = "new Promise(resolve => { const expected=" + expected.ToString().ToLowerInvariant() + "; let observer; const check=()=>{if(document.getElementById('setting-trayIconShowsDeviceType').checked===expected){observer?.disconnect();window.chrome.webview.removeEventListener('message',onMessage);resolve(true);}}; const onMessage=()=>requestAnimationFrame(check); observer=new MutationObserver(check);observer.observe(document.documentElement,{subtree:true,attributes:true,childList:true});window.chrome.webview.addEventListener('message',onMessage);check();requestAnimationFrame(check);})";
            await _web.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = script, awaitPromise = true, returnByValue = true }));
        }
        finally { App.Settings.TrayIconShowsDeviceTypeChanged -= onChanged; }
    }

    private static void Finish(int code, string message)
    {
        if (_finished) return;
        _finished = true;
        File.WriteAllText(Path.Combine(Root, "result.json"), JsonSerializer.Serialize(new { exitCode = code, message, version = App.PackageVersion?.ToString(), processId = Environment.ProcessId }));
        _window?.Close();
        _app.Shutdown(code);
    }
}
