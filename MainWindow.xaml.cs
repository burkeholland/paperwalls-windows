using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Paperwalls;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private TrayIcon? _tray;
    private bool _quitting;
    private bool _explainedTray;
    private MainPage Page => (MainPage)RootFrame.Content;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var scale = GetDpiForWindow(Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id)) / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1320 * scale), (int)(860 * scale)));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 720;
            presenter.PreferredMinimumHeight = 480;
        }

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
        Page.StatusChanged += message =>
        {
            if (!AppWindow.IsVisible) _tray?.ShowMessage(message);
        };
        try
        {
            var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
            _tray = new TrayIcon(hwnd, ShowGallery, async () => await Page.NextWallpaperAsync(), Quit);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            System.Diagnostics.Trace.TraceError("{0}", ex);
            Page.ReportStatus("Couldn't start the tray icon. Closing will quit Paper.");
        }
        AppWindow.Closing += (_, args) =>
        {
            if (_quitting || _tray?.IsAvailable != true) return;
            args.Cancel = true;
            HideToTray();
            if (!_explainedTray)
            {
                _tray.ShowMessage("Still running. Use the tray icon to open Paper or quit.");
                _explainedTray = true;
            }
        };
        Closed += (_, _) => _tray?.Dispose();
    }

    public void ShowGallery()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.Restore();
        Activate();
    }

    public void HideToTray()
    {
        if (_tray?.IsAvailable == true) AppWindow.Hide();
    }

    private void Quit()
    {
        _quitting = true;
        Close();
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}
