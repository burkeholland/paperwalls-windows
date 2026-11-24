using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Paperwalls;

internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private readonly nint _window;
    private readonly SubclassProc _callback;
    private readonly uint _taskbarCreated;
    private nint _icon;
    private bool _added;
    private readonly Action _open;
    private readonly Action _next;
    private readonly Action _quit;

    public bool IsAvailable => _added;

    public TrayIcon(nint window, Action open, Action next, Action quit)
    {
        _window = window;
        _open = open;
        _next = next;
        _quit = quit;
        _callback = WindowProc;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), 1, 32, 32, 0x10);
        if (_icon == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!SetWindowSubclass(_window, _callback, 1, 0))
        {
            DestroyIcon(_icon);
            _icon = 0;
            throw new Win32Exception("Couldn't attach the Paperwalls tray icon.");
        }
        try { Add(); }
        catch
        {
            Dispose();
            throw;
        }
    }

    private NotifyIconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = 1,
        Flags = 1 | 2 | 4,
        Callback = CallbackMessage,
        Icon = _icon,
        Tip = "Paperwalls",
        Info = "",
        InfoTitle = "",
    };

    private void Add()
    {
        var data = Data();
        if (!ShellNotifyIcon(0, ref data)) throw new Win32Exception("Couldn't add the Paperwalls tray icon.");
        data.Version = 4;
        if (!ShellNotifyIcon(4, ref data))
        {
            ShellNotifyIcon(2, ref data);
            throw new Win32Exception("Couldn't configure the Paperwalls tray icon.");
        }
        _added = true;
    }

    public void ShowMessage(string message)
    {
        var data = Data();
        data.Flags = 0x10;
        data.InfoTitle = "Paperwalls";
        data.Info = message;
        data.InfoFlags = 1;
        if (!ShellNotifyIcon(1, ref data))
            System.Diagnostics.Trace.TraceError("Couldn't show tray notification: {0}", message);
    }

    private nint WindowProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == _taskbarCreated)
        {
            _added = false;
            try { Add(); }
            catch (Win32Exception ex)
            {
                System.Diagnostics.Trace.TraceError("{0}", ex);
                _open();
            }
        }
        else if (message == CallbackMessage)
        {
            var notification = (uint)((long)lParam & 0xFFFF);
            if (notification is 0x400 or 0x401) _open();
            else if (notification == 0x7B) ShowMenu();
            return 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            System.Diagnostics.Trace.TraceError("Couldn't create tray menu.");
            _open();
            return;
        }
        try
        {
            AppendMenu(menu, 0, 1, "Next wallpaper");
            AppendMenu(menu, 0, 2, "Open");
            AppendMenu(menu, 0x800, 0, null);
            AppendMenu(menu, 0, 3, "Quit");
            SetMenuDefaultItem(menu, 2, false);
            GetCursorPos(out var point);
            SetForegroundWindow(_window);
            var command = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, _window, 0);
            PostMessage(_window, 0, 0, 0);
            if (command == 1) _next();
            else if (command == 2) _open();
            else if (command == 3) _quit();
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data();
            ShellNotifyIcon(2, ref data);
            _added = false;
        }
        RemoveWindowSubclass(_window, _callback, 1);
        if (_icon != 0) DestroyIcon(_icon);
        _icon = 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);

    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint operation, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuDefaultItem(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
