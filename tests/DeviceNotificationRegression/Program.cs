using EarTrumpet;
using EarTrumpet.DataModel.Audio;
using EarTrumpet.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using EarTrumpet.DataModel;
using EarTrumpet.Interop.MMDeviceAPI;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--audio-roles")
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            var roles = new Dictionary<string, string>();
            foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
            {
                try { roles[role.ToString()] = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, role).GetId(); }
                catch (System.Runtime.InteropServices.COMException ex) { roles[role.ToString()] = "unavailable:" + ex.HResult; }
            }
            Console.WriteLine(JsonSerializer.Serialize(roles));
            return 0;
        }
        // Real view models and their real event ordering; only the Core Audio boundary is fake.
        // No App startup, settings writes, media playback or host audio changes.
        var manager = new DeviceManager();
        var device = new AudioDevice(manager);
        manager.Devices.Add(device);
        var collection = new DeviceCollectionViewModel(manager, new AppSettings());
        var seen = new List<DeviceViewModel.DeviceIconKind>();
        collection.TrayPropertyChanged += () => seen.Add(collection.Default.IconKind);
        manager.Default = device;
        seen.Clear();
        device.IsMuted = true;
        if (seen.Count == 0 || seen[seen.Count - 1] != DeviceViewModel.DeviceIconKind.Mute)
        {
            Console.WriteLine("FAIL mute notification reached tray before IconKind updated, without a corrected notification");
            return 1;
        }
        seen.Clear();
        device.IsMuted = false;
        if (seen.Count == 0 || seen[seen.Count - 1] != DeviceViewModel.DeviceIconKind.Bar3)
        {
            Console.WriteLine("FAIL unmute notification left the tray with the old muted IconKind");
            return 1;
        }
        Console.WriteLine("PASS real device -> view model -> tray notifications preserve final mute/unmute state");
        if (args.Length > 0) VerifyUpdater(args[0]);
        return 0;
    }

    private static void VerifyUpdater(string scratch)
    {
        const string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        var parse = typeof(UpdateService).GetMethod("ParseSha256Digest", BindingFlags.NonPublic | BindingFlags.Static);
        var verify = typeof(UpdateService).GetMethod("VerifyDownload", BindingFlags.NonPublic | BindingFlags.Static);
        if (!Equals(parse.Invoke(null, new object[] { "SHA256:" + expected.ToUpperInvariant() }), expected))
            throw new Exception("Valid published digest was not normalized.");
        foreach (var invalid in new[] { null, "md5:" + expected, "sha256:abc", "sha256:" + new string('z', 64) })
            if (parse.Invoke(null, new object[] { invalid }) != null) throw new Exception("Invalid digest accepted.");
        Directory.CreateDirectory(scratch);
        var file = Path.Combine(scratch, "updater-digest-regression.bin");
        File.WriteAllText(file, "abc");
        verify.Invoke(null, new object[] { file, expected });
        ExpectRefusal(verify, file, null);
        ExpectRefusal(verify, file, new string('0', 64));
        File.WriteAllText(file, "abd");
        ExpectRefusal(verify, file, expected);
        var pipe = typeof(EarTrumpet.Interop.Helpers.PipeServer);
        var name = (string)pipe.GetField("PipeName").GetValue(null);
        var options = (System.IO.Pipes.PipeOptions)pipe.GetField("Options", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        if (name != "BetterTrumpet_IPC_v1_" + System.Diagnostics.Process.GetCurrentProcess().SessionId || options != System.IO.Pipes.PipeOptions.CurrentUserOnly)
            throw new Exception("CLI pipe lost its session/user boundary.");
        Console.WriteLine("PASS compiled updater accepts matching SHA-256, refuses missing/wrong/tampered digests; CLI pipe is user/session scoped");
    }

    private static void ExpectRefusal(MethodInfo method, string file, string digest)
    {
        try { method.Invoke(null, new object[] { file, digest }); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { return; }
        throw new Exception("Unsafe update download was accepted.");
    }

    private sealed class DeviceManager : IAudioDeviceManager
    {
        public event EventHandler<IAudioDevice> DefaultChanged;
        public event EventHandler Loaded { add { } remove { } }
        private IAudioDevice _default;
        public IAudioDevice Default { get => _default; set { _default = value; DefaultChanged?.Invoke(this, value); } }
        public ObservableCollection<IAudioDevice> Devices { get; } = new ObservableCollection<IAudioDevice>();
        public string Kind => "Playback";
        public void UpdatePeakValues() { }
        public void AddFilter(Func<ObservableCollection<IAudioDevice>, ObservableCollection<IAudioDevice>> filter) { }
    }

    private sealed class AudioDevice : IAudioDevice
    {
        public AudioDevice(IAudioDeviceManager manager) { Parent = manager; }
        public string Id => "regression-device";
        public string DisplayName => "Regression device";
        public string IconPath => null;
        public IAudioDeviceManager Parent { get; }
        public ObservableCollection<IAudioDeviceSession> Groups { get; } = new ObservableCollection<IAudioDeviceSession>();
        public event PropertyChangedEventHandler PropertyChanged;
        private bool _muted;
        public bool IsMuted { get => _muted; set { _muted = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMuted))); } }
        public float Volume { get; set; } = 1;
        public float PeakValue1 => 0;
        public float PeakValue2 => 0;
        public void AddFilter(Func<ObservableCollection<IAudioDeviceSession>, ObservableCollection<IAudioDeviceSession>> filter) { }
    }
}
