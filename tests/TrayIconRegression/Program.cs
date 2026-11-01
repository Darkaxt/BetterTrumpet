using EarTrumpet;
using EarTrumpet.DataModel;
using EarTrumpet.DataModel.WindowsAudio;
using EarTrumpet.UI.Helpers;
using EarTrumpet.UI.ViewModels;
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Threading;

internal static class Program
{
    private static int _failures;
    private static AudioEndpointFormFactor _formFactor;

    [STAThread]
    private static int Main()
    {
        foreach (var formFactor in new[] { AudioEndpointFormFactor.Speakers, AudioEndpointFormFactor.Headphones, AudioEndpointFormFactor.DigitalAudioDisplayDevice })
        {
            _formFactor = formFactor;
            RunCases();
        }
        return _failures == 0 ? 0 : 1;
    }

    private static void RunCases()
    {
        Run("startup muted during playback", muted: true, playing: true, test: (source, devices, settings) =>
        {
            ExpectStatic(source, "startup");
            ExpectMutedIcon(source);
        });
        Run("playback starts muted", muted: true, playing: false, test: (source, devices, settings) =>
        {
            MediaSessionService.Instance.SetPlaying(true);
            Tick(source);
            ExpectStatic(source, "playback started muted");
            ExpectMutedIcon(source);
        });
        Run("mute stops animation and unmute resumes it", muted: false, playing: true, test: (source, devices, settings) =>
        {
            ExpectAnimating(source, "playing");
            Tick(source);
            devices.Default.IconKind = DeviceViewModel.DeviceIconKind.Mute;
            devices.Notify();
            ExpectStatic(source, "muted");
            Tick(source); // A pending tick must not overwrite the crossed-out icon.
            ExpectMutedIcon(source);
            devices.Default.IconKind = DeviceViewModel.DeviceIconKind.Bar2;
            devices.Notify();
            ExpectAnimating(source, "unmuted while playing");
            Tick(source);
        });
        Run("pause then unmute stays static", muted: false, playing: true, test: (source, devices, settings) =>
        {
            devices.Default.IconKind = DeviceViewModel.DeviceIconKind.Mute;
            devices.Notify();
            MediaSessionService.Instance.SetPlaying(false);
            devices.Default.IconKind = DeviceViewModel.DeviceIconKind.Bar2;
            devices.Notify();
            ExpectStatic(source, "unmuted while paused");
        });
        Run("default device switches and disconnect", muted: false, playing: true, test: (source, devices, settings) =>
        {
            devices.Default = new DeviceViewModel { IconKind = DeviceViewModel.DeviceIconKind.Mute, FormFactor = _formFactor };
            devices.Notify();
            ExpectStatic(source, "muted new default");
            ExpectMutedIcon(source);
            devices.Default = null;
            devices.Notify();
            MediaSessionService.Instance.SetPlaying(true);
            ExpectStatic(source, "no output");
            devices.Default = new DeviceViewModel { IconKind = DeviceViewModel.DeviceIconKind.Bar2, FormFactor = _formFactor };
            devices.Notify();
            ExpectAnimating(source, "unmuted new default");
        });
        Run("legacy toggle respects mute and playback", muted: true, playing: true, test: (source, devices, settings) =>
        {
            settings.UseLegacyIcon = true;
            ExpectStatic(source, "legacy muted");
            settings.UseLegacyIcon = false;
            ExpectStatic(source, "modern muted");
            ExpectMutedIcon(source);
            devices.Default.IconKind = DeviceViewModel.DeviceIconKind.Bar2;
            devices.Notify();
            ExpectAnimating(source, "modern playing");
            settings.UseLegacyIcon = true;
            ExpectStatic(source, "legacy playing");
            settings.UseLegacyIcon = false;
            ExpectAnimating(source, "modern playing restored");
            MediaSessionService.Instance.SetPlaying(false);
            ExpectStatic(source, "paused");
        });
    }

    private static void Run(string name, bool muted, bool playing,
        Action<TaskbarIconSource, DeviceCollectionViewModel, AppSettings> test)
    {
        MediaSessionService.Instance = new MediaSessionService();
        MediaSessionService.Instance.SetPlaying(playing);
        var devices = new DeviceCollectionViewModel
        {
            Default = new DeviceViewModel { IconKind = muted ? DeviceViewModel.DeviceIconKind.Mute : DeviceViewModel.DeviceIconKind.Bar2, FormFactor = _formFactor }
        };
        var settings = new AppSettings();
        var source = new TaskbarIconSource(devices, settings);
        try { test(source, devices, settings); Console.WriteLine("PASS " + _formFactor + ": " + name); }
        catch (Exception ex) { _failures++; Console.WriteLine("FAIL " + _formFactor + ": " + name + ": " + ex.Message); }
        finally
        {
            MediaSessionService.Instance.SetPlaying(false);
            Field<DispatcherTimer>(source, "_animationTimer")?.Stop();
            source.Current?.Dispose();
            (Field<VolumeIconGenerator>(source, "_volumeIconGenerator") as IDisposable)?.Dispose();
        }
    }

    private static T Field<T>(object source, string name) =>
        (T)source.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(source);
    private static void Tick(TaskbarIconSource source) => typeof(TaskbarIconSource)
        .GetMethod("OnAnimationTick", BindingFlags.NonPublic | BindingFlags.Instance)
        .Invoke(source, new object[] { null, EventArgs.Empty });
    private static void ExpectStatic(TaskbarIconSource source, string context)
    {
        if (Field<bool>(source, "_isAnimating") || Field<DispatcherTimer>(source, "_animationTimer").IsEnabled)
            throw new Exception(context + ": animation must be stopped");
    }
    private static void ExpectAnimating(TaskbarIconSource source, string context)
    {
        if (!Field<bool>(source, "_isAnimating") || !Field<DispatcherTimer>(source, "_animationTimer").IsEnabled)
            throw new Exception(context + ": animation must be running");
    }
    private static void ExpectMutedIcon(TaskbarIconSource source)
    {
        var glyph = Field<TrayDeviceGlyph>(source, "_glyph");
        var generator = Field<VolumeIconGenerator>(source, "_volumeIconGenerator");
        using var expected = glyph == TrayDeviceGlyph.Speaker ? generator.GetMutedIcon(false) : generator.GetDeviceIcon(glyph, true, false);
        using var left = source.Current.ToBitmap();
        using var right = expected.ToBitmap();
        for (int y = 0; y < left.Height; y++)
            for (int x = 0; x < left.Width; x++)
                if (left.GetPixel(x, y) != right.GetPixel(x, y))
                    throw new Exception("crossed-out icon was overwritten");
    }
}
