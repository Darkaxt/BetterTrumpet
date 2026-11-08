using EarTrumpet.DataModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Interop;
using Windows.Media;
using Windows.Media.Control;

[SupportedOSPlatform("windows10.0.17763")]
internal static class Program
{
    private static readonly Dictionary<IntPtr, GlobalSystemMediaTransportControlsSession> Observed = new();
    private static readonly Dictionary<string, int> NativeCounts = new();
    private static readonly object CountLock = new();
    private static GlobalSystemMediaTransportControlsSessionManager _manager;
    private static MediaSessionService _service;
    private static int _repeats;
    private static readonly CounterTrace Counter = new();
    private static int _exitCode;
    private static Dispatcher _dispatcher;
    private static HwndSource _window;
    private static SystemMediaTransportControls _provider;
    private static bool _published;
    private static bool _collect;
    private static bool _observe;
    private const string ProviderId = "BetterTrumpet.MediaSessionRegression";
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string id);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "hidden") return HiddenPopupRegression();
        if (args.Length < 2 || !int.TryParse(args[0], out _repeats) || !int.TryParse(args[1], out var seconds) || _repeats < 1 || seconds < 1)
        {
            Console.Error.WriteLine("Arguments: subscription refresh count, diagnostic observation seconds. No App startup, host settings writes, audio or playback controls.");
            return 2;
        }
        _collect = args.Length > 2 && args[2] == "collect";
        _observe = args.Length < 3;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _dispatcher = dispatcher;
        dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _service = (MediaSessionService)RuntimeHelpers.GetUninitializedObject(typeof(MediaSessionService));
                Set("_sessionManager", _manager);
                Set("_dispatcher", dispatcher);
                Set("_thumbnailLock", new object());
                Trace.Listeners.Add(Counter);
                Refresh();
                _manager.SessionsChanged += SessionsChanged;
                // Own paused metadata-only session: no audio player/source and no
                // control requests to other applications. Its HWND is never shown.
                Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(ProviderId));
                _window = new HwndSource(new HwndSourceParameters("BetterTrumpet metadata regression") { WindowStyle = unchecked((int)0x80000000), Width = 1, Height = 1 });
                _provider = SystemMediaTransportControlsInterop.GetForWindow(_window.Handle);
                _provider.IsPlayEnabled = true;
                _provider.IsEnabled = true;
                _provider.PlaybackStatus = MediaPlaybackStatus.Paused;
                _provider.DisplayUpdater.Type = MediaPlaybackType.Music;
                _provider.DisplayUpdater.MusicProperties.Title = "BetterTrumpet regression seed";
                _provider.DisplayUpdater.Update();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                timer.Tick += (_, __) =>
                {
                    timer.Stop();
                    _manager.SessionsChanged -= SessionsChanged;
                    lock (CountLock)
                        Console.WriteLine(JsonSerializer.Serialize(new { serviceAssembly = typeof(MediaSessionService).Assembly.Location, refreshesPerSessionList = _repeats, collectTemporaryWrappers = _collect, publishedControlledChange = _published, diagnosticSeconds = seconds, nativeCallbacks = NativeCounts, productionCallbacks = Counter.Count }));
                    foreach (var session in Observed.Values) session.MediaPropertiesChanged -= NativeChanged;
                    _provider.IsEnabled = false;
                    _provider.DisplayUpdater.ClearAll();
                    _window.Dispose();
                    Trace.Listeners.Remove(Counter);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                };
                timer.Start();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); _exitCode = 1; dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        }));
        Dispatcher.Run();
        return _exitCode;
    }

    private static int HiddenPopupRegression()
    {
        // Execute the real compiled hidden-state handlers; visual fields are
        // intentionally absent so an unintended visual refresh is observable.
        // This is a controlled UI boundary, not rendered-window acceptance.
        var dispatcher = Dispatcher.CurrentDispatcher;
        var service = (MediaSessionService)RuntimeHelpers.GetUninitializedObject(typeof(MediaSessionService));
        typeof(MediaSessionService).GetField("_thumbnailLock", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, new object());
        typeof(MediaSessionService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, service);
        var popupType = typeof(MediaSessionService).Assembly.GetType("EarTrumpet.UI.Views.MediaPopupWindow");
        var popup = RuntimeHelpers.GetUninitializedObject(popupType);
        typeof(DispatcherObject).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(popup, dispatcher);
        int failures = 0;
        dispatcher.UnhandledException += (_, error) => { failures++; Console.WriteLine("FAIL hidden handler touched visual content: " + error.Exception.GetType().Name); error.Handled = true; };
        foreach (var name in new[] { "OnMediaTrackChanged", "OnMediaPlaybackChanged", "OnTimelineChanged" })
        {
            var method = popupType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            var args = name == "OnMediaPlaybackChanged" ? new object[] { true } : null;
            method.Invoke(popup, args);
        }
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.ContextIdle);
        Dispatcher.PushFrame(frame);
        var thumbnail = popupType.GetField("_thumbnailCts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(popup) as CancellationTokenSource;
        if (thumbnail != null)
        {
            failures++;
            Console.WriteLine("FAIL hidden track change started thumbnail work");
            thumbnail.Cancel();
            thumbnail.Dispose();
        }
        if (failures == 0) Console.WriteLine("PASS compiled hidden popup handlers acquire no thumbnail job and touch no visual content");
        return failures == 0 ? NotificationRegression() : 1;
    }

    private static int NotificationRegression()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        int result = 1;
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(new Action(async () =>
        {
            try { result = await NativeNotificationRegression(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { frame.Continue = false; }
        }));
        Dispatcher.PushFrame(frame);
        return result;
    }

    private static async Task<int> NativeNotificationRegression()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var ready = new TaskCompletionSource<GlobalSystemMediaTransportControlsSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        void FindSession()
        {
            try
            {
                foreach (var candidate in manager.GetSessions())
                    if (candidate.SourceAppUserModelId == ProviderId) ready.TrySetResult(candidate);
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }
        Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs> changed = (_, __) => dispatcher.BeginInvoke(new Action(FindSession));
        manager.SessionsChanged += changed;
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(ProviderId));
        using var window = new HwndSource(new HwndSourceParameters("BetterTrumpet notification regression") { WindowStyle = unchecked((int)0x80000000), Width = 1, Height = 1 });
        var provider = SystemMediaTransportControlsInterop.GetForWindow(window.Handle);
        try
        {
            // Metadata-only native session, never shown and never given an audio source.
            provider.IsPlayEnabled = true;
            provider.IsEnabled = true;
            provider.PlaybackStatus = MediaPlaybackStatus.Paused;
            provider.DisplayUpdater.Type = MediaPlaybackType.Music;
            provider.DisplayUpdater.MusicProperties.Title = "BetterTrumpet notification regression";
            provider.DisplayUpdater.Update();
            FindSession();
            Console.WriteLine("Waiting for owned paused metadata session registration.");
            var session = await ready.Task;
            return await VerifyNotificationDispatch(dispatcher, session, provider);
        }
        finally
        {
            manager.SessionsChanged -= changed;
            provider.IsEnabled = false;
            provider.DisplayUpdater.ClearAll();
        }
    }

    private static async Task<int> VerifyNotificationDispatch(Dispatcher dispatcher, GlobalSystemMediaTransportControlsSession session, SystemMediaTransportControls provider)
    {
        var service = (MediaSessionService)RuntimeHelpers.GetUninitializedObject(typeof(MediaSessionService));
        typeof(MediaSessionService).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, dispatcher);
        typeof(MediaSessionService).GetField("_thumbnailLock", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, new object());
        var pinned = typeof(MediaSessionService).GetField("_pinnedSession", BindingFlags.Instance | BindingFlags.NonPublic);
        // The service does not subscribe to native callbacks in this fixture:
        // invoke its real handlers with our real native session identity only.
        pinned.SetValue(service, session);
        int tracks = 0, timelines = 0;
        service.MediaTrackChanged += () => tracks++;
        service.TimelineChanged += () => timelines++;
        var track = typeof(MediaSessionService).GetMethod("OnMediaPropertiesChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        var timeline = typeof(MediaSessionService).GetMethod("OnTimelinePropertiesChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int i = 0; i < 3; i++)
        {
            track.Invoke(service, new object[] { session, null });
            timeline.Invoke(service, new object[] { session, null });
        }
        Drain(dispatcher);
        if (tracks != 3 || timelines != 3) throw new Exception($"Pinned notifications were delayed or dropped: tracks={tracks}, timelines={timelines}");
        tracks = timelines = 0;
        track.Invoke(service, new object[] { session, null });
        timeline.Invoke(service, new object[] { session, null });
        pinned.SetValue(service, null);
        Drain(dispatcher);
        if (tracks != 0 || timelines != 0) throw new Exception("Queued notifications survived a pinned-session change.");
        Console.WriteLine("PASS compiled pinned track/timeline handlers publish directly and reject stale queued sessions using an owned native session");
        pinned.SetValue(service, session);
        const string expected = "BetterTrumpet completed metadata regression";
        var metadataReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async void CheckMetadata(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (props.Title == expected && props.Thumbnail != null) metadataReady.TrySetResult(true);
            }
            catch (Exception ex) { metadataReady.TrySetException(ex); }
        }
        // Own a one-pixel PNG in this registered test-output directory. A native
        // StorageFile supplies the image content type; no audio source is created.
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[] { 255, 0, 0, 255 }, 4)));
        using var png = new System.IO.MemoryStream();
        encoder.Save(png);
        var fixturePath = System.IO.Path.Combine(AppContext.BaseDirectory, "metadata-art-" + Guid.NewGuid().ToString("N") + ".png");
        System.IO.File.WriteAllBytes(fixturePath, png.ToArray());
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(fixturePath);
        session.MediaPropertiesChanged += CheckMetadata;
        try
        {
            provider.DisplayUpdater.MusicProperties.Title = expected;
            provider.DisplayUpdater.MusicProperties.Artist = string.Empty;
            provider.DisplayUpdater.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file);
            provider.DisplayUpdater.Update();
            CheckMetadata(session, null);
            await metadataReady.Task;
            var title = await Task.Run(service.GetCurrentMediaInfo);
            if (title != expected) throw new Exception("Compiled metadata reader did not return completed native title.");
            var thumbnail = await Task.Run(service.GetCurrentThumbnail);
            if (thumbnail == null || thumbnail.PixelWidth < 1 || thumbnail.PixelWidth > 300 || thumbnail.PixelHeight < 1) throw new Exception("Compiled thumbnail reader did not return valid bounded native art.");
            Console.WriteLine($"PASS compiled metadata and thumbnail readers observe actual native completion without timing thresholds ({thumbnail.PixelWidth}x{thumbnail.PixelHeight})");
        }
        finally { session.MediaPropertiesChanged -= CheckMetadata; }
        return 0;
    }

    private static void Drain(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.ContextIdle);
        Dispatcher.PushFrame(frame);
    }

    private static void Set(string name, object value) => typeof(MediaSessionService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_service, value);
    private static void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => _dispatcher.BeginInvoke(new Action(Refresh));
    private static void Refresh()
    {
        if (_observe) Observe();
        bool providerPresent = HasControlledSession();
        foreach (var session in _manager.GetSessions())
            if (session.SourceAppUserModelId == ProviderId) Set("_pinnedSession", session);
        var subscribe = typeof(MediaSessionService).GetMethod("SubscribeToAllSessions", BindingFlags.Instance | BindingFlags.NonPublic);
        for (var index = 0; index < _repeats; index++)
        {
            subscribe.Invoke(_service, null);
            if (_collect) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        }
        if (!_published && _provider != null && providerPresent)
        {
            _published = true;
            _provider.DisplayUpdater.MusicProperties.Title = "BetterTrumpet regression changed";
            _provider.DisplayUpdater.Update();
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasControlledSession()
    {
        foreach (var session in _manager.GetSessions()) if (session.SourceAppUserModelId == ProviderId) return true;
        return false;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Observe()
    {
        foreach (var session in _manager.GetSessions())
        {
            var identity = ((WinRT.IWinRTObject)(object)session).NativeObject.ThisPtr;
            if (!Observed.ContainsKey(identity))
            {
                Observed.Add(identity, session);
                session.MediaPropertiesChanged += NativeChanged;
            }
        }
    }
    private static void NativeChanged(GlobalSystemMediaTransportControlsSession session, MediaPropertiesChangedEventArgs args)
    {
        lock (CountLock)
        {
            NativeCounts.TryGetValue(session.SourceAppUserModelId, out var count);
            NativeCounts[session.SourceAppUserModelId] = count + 1;
        }
    }
    private sealed class CounterTrace : TraceListener
    {
        public int Count;
        public override void Write(string message) { }
        public override void WriteLine(string message)
        {
            if (message == "MediaSessionService: Media properties changed (track change)") Interlocked.Increment(ref Count);
        }
    }
}
