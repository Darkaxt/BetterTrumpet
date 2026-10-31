// Controlled Windows/audio boundaries only; tray state and generated icons are production code.
using System;
using System.Drawing;

namespace EarTrumpet
{
    public class AppSettings
    {
        public event EventHandler<bool> UseLegacyIconChanged;
        private bool _legacy;
        public bool UseLegacyIcon
        {
            get => _legacy;
            set { _legacy = value; UseLegacyIconChanged?.Invoke(this, value); }
        }
    }
}
namespace EarTrumpet.DataModel
{
    public class MediaSessionService
    {
        public static MediaSessionService Instance { get; set; } = new MediaSessionService();
        public bool IsMediaPlaying { get; private set; }
        public event Action<bool> MediaPlaybackChanged;
        public void SetPlaying(bool playing)
        {
            IsMediaPlaying = playing;
            MediaPlaybackChanged?.Invoke(playing);
        }
    }
}
namespace EarTrumpet.UI.ViewModels
{
    public class DeviceViewModel
    {
        public enum DeviceIconKind { Mute, Bar0, Bar1, Bar2, Bar3 }
        public DeviceIconKind IconKind { get; set; }
    }
    public class DeviceCollectionViewModel
    {
        public DeviceViewModel Default { get; set; }
        public event Action TrayPropertyChanged;
        public void Notify() => TrayPropertyChanged?.Invoke();
    }
}
namespace EarTrumpet.Interop
{
    public interface IShellNotifyIconSource
    {
        Icon Current { get; }
        event Action<IShellNotifyIconSource> Changed;
    }
    public static class User32
    {
        public enum SystemMetrics { SM_CXSMICON }
        public static int GetSystemMetricsForDpi(SystemMetrics metric, uint dpi) => 24;
    }
    public static class SndVolSSO
    {
        public enum IconId { Muted, NoDevice, SpeakerZeroBars, SpeakerOneBar, SpeakerTwoBars, SpeakerThreeBars }
        public static string GetPath(IconId kind) => kind.ToString();
    }
}
namespace EarTrumpet.Interop.Helpers
{
    public static class WindowsTaskbar { public static uint Dpi => 96; }
    public static class SystemSettings { public static bool IsSystemLightTheme => false; }
    public static class IconHelper
    {
        public static Icon LoadIconForTaskbar(string path, uint dpi) => (Icon)SystemIcons.Application.Clone();
        public static Icon ColorIcon(Icon icon, double fill, System.Windows.Media.Color color) => (Icon)icon.Clone();
    }
}
