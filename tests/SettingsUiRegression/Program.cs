using EarTrumpet;
using EarTrumpet.DataModel.Storage;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        bool guest = Environment.GetEnvironmentVariable("BETTERTRUMPET_UI_TEST_GUEST") == "1" && Directory.Exists(@"C:\Coordinator\Work");
        // This route is prepared for an explicit user exception to the Sandbox
        // restriction. The launcher must not set it before that consent exists.
        bool host = Environment.GetEnvironmentVariable("BETTERTRUMPET_UI_TEST_HOST") == "1" &&
            string.Equals(Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar), @"D:\Temp\bettertrumpet-performance-20261107\host-ui", StringComparison.OrdinalIgnoreCase);
        if (!guest && !host)
        {
            Console.Error.WriteLine("Use the owned guest or the exact, separately authorized portable host validation directory.");
            return 2;
        }
        File.WriteAllText(Path.Combine(Root, "portable.marker"), "Isolated UI verification");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(Root, "webview-profile"));
        // A headless SYSTEM guest has no Explorer-created user personalization keys.
        // Seed the standard profile containers in this disposable guest only.
        if (guest) using (var user = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry64))
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
        // Exercise the real native window factories without competing with the
        // installed application's tray, hotkeys, CLI pipe or single-instance lock.
        var startup = (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), _app, typeof(App).GetMethod("OnAppStartup", BindingFlags.Instance | BindingFlags.NonPublic));
        _app.Startup -= startup;
        typeof(App).GetProperty("Settings").SetValue(null, new AppSettings());
        App.Settings.SettingsWindowPlacement = null;
        App.Settings.TrayIconShowsDeviceType = false;
        typeof(App).GetProperty("PackageVersion").SetValue(null, Version.Parse(System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(App).Assembly.Location).ProductVersion.Trim()));
        typeof(App).GetProperty("PackageName").SetValue(null, "BetterTrumpet.UIRegression");
        _app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _app.DispatcherUnhandledException += (_, e) => { e.Handled = true; Finish(1, e.Exception.ToString()); };
        _app.Startup += (_, __) =>
        {
            ((EarTrumpet.UI.Themes.Manager)_app.Resources["ThemeManager"]).Load();
            _app.Dispatcher.BeginInvoke(new Action(OpenSettings), DispatcherPriority.ContextIdle);
        };
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
            await VerifyPopup();
            Finish(0, "Native settings wide/minimum rendering and real switch bridge passed; native media popup visible/hidden/reopen behavior passed.");
        }
        catch (Exception ex) { Finish(1, ex.ToString()); }
    }

    private static async Task VerifyPopup()
    {
        _window.Hide();
        // Control only the test process's media input. A live SMTC/legacy event
        // can otherwise race these synthetic playback notifications between
        // dispatcher turns and turn a renderer check into a timing-dependent test.
        var singleton = typeof(EarTrumpet.DataModel.MediaSessionService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        var previousService = singleton.GetValue(null);
        var mediaType = typeof(EarTrumpet.DataModel.MediaSessionService);
        var service = (EarTrumpet.DataModel.MediaSessionService)RuntimeHelpers.GetUninitializedObject(mediaType);
        mediaType.GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, Dispatcher.CurrentDispatcher);
        mediaType.GetField("_thumbnailLock", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, new object());
        singleton.SetValue(null, service);
        var popup = new EarTrumpet.UI.Views.MediaPopupWindow(App.Settings);
        _window = popup;
        var rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        popup.ContentRendered += (_, __) => rendered.TrySetResult(true);
        var area = SystemParameters.WorkArea;
        var icon = new Rect(area.Right - 48, area.Bottom + 1, 24, 24);
        var firstAnimation = ShowPopup(popup, icon);
        await rendered.Task;
        await firstAnimation;
        popup.CancelHide();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        var playing = typeof(EarTrumpet.DataModel.MediaSessionService).GetField("_isMediaPlaying", BindingFlags.Instance | BindingFlags.NonPublic);
        var published = typeof(EarTrumpet.DataModel.MediaSessionService).GetField("MediaPlaybackChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        var original = (bool)playing.GetValue(service);
        var path = (System.Windows.Shapes.Path)popup.FindName("PlayPauseIcon");
        try
        {
            // Controlled media boundary in this isolated test instance. These are
            // real service notifications, not claims of clicking playback buttons.
            playing.SetValue(service, false);
            ((Action<bool>)published.GetValue(service))?.Invoke(false);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            var pausedIcon = path.Data.ToString();
            playing.SetValue(service, true);
            ((Action<bool>)published.GetValue(service))?.Invoke(true);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            if (pausedIcon == path.Data.ToString()) throw new Exception($"Visible popup did not reflect playback notifications: showing={popup.IsShowing}, visible={popup.IsVisible}, playing={service.IsMediaPlaying}.");
            await HidePopup(popup);
            var hiddenIcon = path.Data.ToString();
            playing.SetValue(service, false);
            ((Action<bool>)published.GetValue(service))?.Invoke(false);
            typeof(EarTrumpet.UI.Views.MediaPopupWindow).GetMethod("OnMediaTrackChanged", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, null);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            if (hiddenIcon != path.Data.ToString()) throw new Exception("Hidden popup refreshed visual playback content.");

            var title = (TextBlock)popup.FindName("MarqueeText");
            const string stale = "__native_regression_stale_title__";
            title.Text = stale;
            var fresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            EventHandler changed = (_, __) => { if (title.Text != stale) fresh.TrySetResult(true); };
            descriptor.AddValueChanged(title, changed);
            try { await Task.WhenAll(ShowPopup(popup, icon), fresh.Task); }
            finally { descriptor.RemoveValueChanged(title, changed); }
            await Dispatcher.Yield(DispatcherPriority.Render);
            if (popup.Opacity != 1 || !popup.IsVisible) throw new Exception("Popup capture requested before native animation/visibility readiness.");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(popup.ActualWidth), (int)Math.Ceiling(popup.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(popup);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Root, "media-popup.png"))) png.Save(file);
            File.WriteAllText(Path.Combine(Root, "media-popup.json"), JsonSerializer.Serialize(new { width = popup.ActualWidth, height = popup.ActualHeight, visiblePlaybackNotifications = true, hiddenVisualsUnchanged = true, titleRefreshedOnReopen = title.Text != stale }));
            await HidePopup(popup);
        }
        finally
        {
            playing.SetValue(service, original);
            ((Action<bool>)published.GetValue(service))?.Invoke(original);
            popup.Close();
            singleton.SetValue(null, previousService);
        }
    }

    private static Task HidePopup(EarTrumpet.UI.Views.MediaPopupWindow popup)
    {
        var hidden = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = null;
        handler = (_, __) => { popup.PopupHidden -= handler; hidden.TrySetResult(true); };
        popup.PopupHidden += handler;
        popup.HidePopup();
        return hidden.Task;
    }

    private static Task ShowPopup(EarTrumpet.UI.Views.MediaPopupWindow popup, Rect icon)
    {
        var shown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var animation = (System.Windows.Media.Animation.Storyboard)typeof(EarTrumpet.UI.Views.MediaPopupWindow).GetField("_slideInStoryboard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(popup);
        EventHandler handler = null;
        handler = (_, __) => { animation.Completed -= handler; shown.TrySetResult(true); };
        animation.Completed += handler;
        popup.ShowPopup(icon);
        return shown.Task;
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
