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
        var service = (MediaSessionService)RuntimeHelpers.GetUninitializedObject(typeof(MediaSessionService));
        typeof(MediaSessionService).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, dispatcher);
        typeof(MediaSessionService).GetField("_thumbnailLock", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(service, new object());
        var pinned = typeof(MediaSessionService).GetField("_pinnedSession", BindingFlags.Instance | BindingFlags.NonPublic);
        // Controlled session identity only: no native factory, media player or audio.
        // Real compiled publication handlers must retain each queued notification.
        var session = (GlobalSystemMediaTransportControlsSession)RuntimeHelpers.GetUninitializedObject(typeof(GlobalSystemMediaTransportControlsSession));
        GC.SuppressFinalize(session);
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
        Console.WriteLine("PASS compiled pinned track/timeline notifications publish directly and reject stale queued sessions");
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
