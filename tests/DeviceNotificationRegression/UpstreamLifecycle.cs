using EarTrumpet;
using EarTrumpet.DataModel;
using EarTrumpet.DataModel.AppInformation;
using EarTrumpet.DataModel.Audio;
using EarTrumpet.Interop;
using EarTrumpet.Interop.MMDeviceAPI;
using EarTrumpet.UI.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;

internal static partial class Program
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
    private static void DrainUi()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void VerifyUpstreamLifecycle()
    {
        var assembly = typeof(App).Assembly;
        var sessionType = assembly.GetType("EarTrumpet.DataModel.WindowsAudio.Internal.AudioDeviceSession");
        var session = RuntimeHelpers.GetUninitializedObject(sessionType);
        var info = new AppInfo();
        int staleCalls = 0;
        Action<IAppInfo> stopped = _ => staleCalls++;
        info.Stopped += stopped;
        Set(session, "_appInfo", info);
        Set(session, "_onAppStopped", stopped);
        sessionType.GetMethod("Release", Hidden).Invoke(session, null);
        info.Stop();
        if (info.HandlerCount != 0 || staleCalls != 0) throw new Exception("Released session remains owned by the process-stopped event.");

        var type = assembly.GetType("EarTrumpet.DataModel.WindowsAudio.Internal.AudioDeviceManager");
        var manager = RuntimeHelpers.GetUninitializedObject(type);
        GC.SuppressFinalize(manager); // This fixture never registers native callbacks.
        var endpoints = new Endpoints { Console = "late", Multimedia = "old" };
        var map = new ConcurrentDictionary<string, IAudioDevice>();
        Set(manager, "_enumerator", endpoints);
        Set(manager, "_deviceMap", map);
        Set(manager, "_devices", new ObservableCollection<IAudioDevice>());
        Set(manager, "_kind", Enum.Parse(type.GetField("_kind", Hidden).FieldType, "Playback"));
        var owner = new DeviceManager();
        var old = new AudioDevice(owner, "old");
        var late = new AudioDevice(owner, "late");
        map.TryAdd(old.Id, old);
        Set(manager, "_default", old);
        Set(manager, "_isLoaded", true);
        var contract = (IAudioDeviceManager)manager;
        int changes = 0;
        contract.DefaultChanged += (_, __) => changes++;
        var query = type.GetMethod("QueryDefaultDevice", Hidden);
        query.Invoke(manager, null);
        if (contract.Default != null) throw new Exception("A not-yet-added Console endpoint incorrectly fell back to Multimedia.");
        type.GetMethod("Add", Hidden).Invoke(manager, new object[] { late });
        if (!ReferenceEquals(contract.Default, late)) throw new Exception("Endpoint addition did not resolve the pending Console default.");
        type.GetMethod("Remove", Hidden).Invoke(manager, new object[] { late });
        if (contract.Default != null) throw new Exception("Removed endpoint remains default.");
        Set(manager, "_default", late);
        changes = 0;
        var replacement = new AudioDevice(owner, "late");
        type.GetMethod("Add", Hidden).Invoke(manager, new object[] { replacement });
        if (!ReferenceEquals(contract.Default, replacement) || changes != 1) throw new Exception("Same-ID replacement did not notify a new default instance.");
        endpoints.Console = null;
        query.Invoke(manager, null);
        if (!ReferenceEquals(contract.Default, old)) throw new Exception("Missing Console role did not fall back to the available Multimedia role.");
        Console.WriteLine("PASS compiled session release and default endpoint add/remove/same-ID ordering");

        var dispatcher = Dispatcher.CurrentDispatcher;
        var peakOwner = new DeviceManager();
        var collection = new DeviceCollectionViewModel(peakOwner, new AppSettings());
        var tick = typeof(DeviceCollectionViewModel).GetMethod("PeakMeterTimer_Elapsed", Hidden);
        int queued = 0;
        DispatcherHookEventHandler posted = (_, e) => { if (e.Operation.Priority == DispatcherPriority.Input) queued++; };
        dispatcher.Hooks.OperationPosted += posted;
        try
        {
            Task.Run(() => { for (int i = 0; i < 100; i++) tick.Invoke(collection, new object[] { null, null }); }).GetAwaiter().GetResult();
            if (queued != 1 || peakOwner.PeakUpdates != 100) throw new Exception("Meter ticks accumulated multiple pending UI updates or skipped native sampling.");
            DrainUi();
            Task.Run(() => tick.Invoke(collection, new object[] { null, null })).GetAwaiter().GetResult();
            if (queued != 2) throw new Exception("Completed meter update did not permit the next dispatch.");
            DrainUi();
        }
        finally { dispatcher.Hooks.OperationPosted -= posted; }
        Console.WriteLine("PASS compiled meter dispatch queues one Input operation while the UI is occupied and resumes after completion");
    }

    private static int InstallerFixture(string pipeName, int exitCode)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.In);
        client.Connect();
        if (client.ReadByte() != 1) return 91;
        return exitCode;
    }
    private static void VerifyInstallerCompletion()
    {
        var run = typeof(UpdateService).GetMethod("RunInstallerAsync", Hidden);
        foreach (int exitCode in new[] { 0, 7 })
        {
            var service = (UpdateService)RuntimeHelpers.GetUninitializedObject(typeof(UpdateService));
            var viewModel = (FlyoutViewModel)RuntimeHelpers.GetUninitializedObject(typeof(FlyoutViewModel));
            Set(viewModel, "_currentDispatcher", Dispatcher.CurrentDispatcher);
            viewModel.SetUpdateService(service);
            int notifications = 0;
            viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlyoutViewModel.IsUpdateBusy)) notifications++; };
            var name = "BetterTrumpet-installer-regression-" + Guid.NewGuid().ToString("N");
            using var pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var start = new ProcessStartInfo(Environment.ProcessPath, $"--installer-fixture {name} {exitCode}") { UseShellExecute = false, CreateNoWindow = true };
            var task = Task.Run(() => (Task)run.Invoke(service, new object[] { start }));
            pipe.WaitForConnectionAsync().GetAwaiter().GetResult();
            DrainUi();
            if (!service.IsInstalling || !service.IsBusy || !viewModel.IsUpdateBusy || notifications == 0 || task.IsCompleted) throw new Exception("Installer busy state did not span the actual running process.");
            // Reentry must return at its real busy guard, before network/settings/UI work.
            service.DownloadAndInstallAsync();
            pipe.WriteByte(1);
            pipe.Flush();
            bool failed = false;
            try { task.GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { failed = true; }
            DrainUi();
            if (failed != (exitCode != 0) || service.IsInstalling || service.IsBusy || viewModel.IsUpdateBusy || notifications < 2) throw new Exception("Installer exit result did not release busy state and notify the flyout.");
        }
        var missing = (UpdateService)RuntimeHelpers.GetUninitializedObject(typeof(UpdateService));
        try
        {
            ((Task)run.Invoke(missing, new object[] { new ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory, "nonexistent-installer-" + Guid.NewGuid().ToString("N") + ".exe")) { UseShellExecute = false, CreateNoWindow = true } })).GetAwaiter().GetResult();
            throw new Exception("Missing installer unexpectedly started.");
        }
        catch (System.ComponentModel.Win32Exception) { }
        if (missing.IsInstalling || missing.IsBusy) throw new Exception("Failed installer launch retained busy state.");
        Console.WriteLine("PASS compiled installer lifecycle/flyout busy notifications follow actual process completion, failure and launch refusal");
    }

    private sealed class AppInfo : IAppInfo
    {
        public event Action<IAppInfo> Stopped;
        public int HandlerCount => Stopped?.GetInvocationList().Length ?? 0;
        public void Stop() => Stopped?.Invoke(this);
        public string DisplayName => "Fixture";
        public string ExeName => "Fixture";
        public string PackageInstallPath => null;
        public string SmallLogoPath => null;
        public bool IsDesktopApp => true;
    }
    private sealed class Endpoints : IMMDeviceEnumerator
    {
        public string Console, Multimedia;
        public IMMDevice GetDefaultAudioEndpoint(EDataFlow flow, ERole role)
        {
            var id = role == ERole.eConsole ? Console : Multimedia;
            if (id == null) throw new COMException("No endpoint", unchecked((int)0x80070490));
            return new RawDevice(id);
        }
        public IMMDeviceCollection EnumAudioEndpoints(EDataFlow flow, DeviceState mask) => throw new NotSupportedException();
        public IMMDevice GetDevice(string id) => new RawDevice(id);
        public void RegisterEndpointNotificationCallback(IMMNotificationClient client) { }
        public void UnregisterEndpointNotificationCallback(IMMNotificationClient client) { }
    }
    private sealed class RawDevice : IMMDevice
    {
        private readonly string _id;
        public RawDevice(string id) { _id = id; }
        public string GetId() => _id;
        public DeviceState GetState() => DeviceState.ACTIVE;
        public void Activate(ref Guid iid, uint context, IntPtr parameters, out object value) => throw new NotSupportedException();
        public IPropertyStore OpenPropertyStore(STGM access) => throw new NotSupportedException();
    }
}
