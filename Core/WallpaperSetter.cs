using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Paperwalls.Core;

/// <summary>
/// Sets the desktop wallpaper through Explorer's IDesktopWallpaper COM API.
/// Unlike writing Control Panel registry values, this works from inside an MSIX package
/// (packaged apps get a virtualized HKCU) and applies to every monitor.
/// </summary>
public static partial class WallpaperSetter
{
    private const int DWPOS_FILL = 4;
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private static readonly Guid CLSID_DesktopWallpaper = new("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD");

    public static void Apply(string path)
    {
        var wallpaper = Create();
        wallpaper.SetPosition(DWPOS_FILL);
        wallpaper.SetWallpaper(null, path);
    }

    /// <summary>Returns the wallpaper path when every monitor shows the same image.</summary>
    public static string? CurrentPath()
    {
        try
        {
            Create().GetWallpaper(null, out var path);
            return path;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static unsafe IDesktopWallpaper Create()
    {
        var iid = typeof(IDesktopWallpaper).GUID;
        var clsid = CLSID_DesktopWallpaper;
        Marshal.ThrowExceptionForHR(CoCreateInstance(&clsid, null, CLSCTX_LOCAL_SERVER, &iid, out var ptr));
        try
        {
            return (IDesktopWallpaper)ComWrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    private static readonly StrategyBasedComWrappers ComWrappers = new();

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoCreateInstance(Guid* clsid, void* outer, uint context, Guid* iid, out nint ppv);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
internal partial interface IDesktopWallpaper
{
    void SetWallpaper(string? monitorId, string wallpaper);
    void GetWallpaper(string? monitorId, out string wallpaper);
    void GetMonitorDevicePathAt(uint index, out string monitorId);
    void GetMonitorDevicePathCount(out uint count);
    void GetMonitorRECT(string monitorId, out Rect rect);
    void SetBackgroundColor(uint color);
    void GetBackgroundColor(out uint color);
    void SetPosition(int position);
    void GetPosition(out int position);
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left, Top, Right, Bottom;
}
