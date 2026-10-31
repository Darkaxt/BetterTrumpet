using EarTrumpet;
using EarTrumpet.DataModel.Audio;
using EarTrumpet.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
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
        return 0;
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
