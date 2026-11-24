using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Paperwalls.Core;

namespace Paperwalls;

public sealed partial class MainPage : Page
{
    private readonly PaperApiClient _api = new();
    private readonly CatalogStore _catalogStore = new(AppPaths.Catalog);
    private readonly FavoritesStore _favoritesStore = new(AppPaths.User);
    private readonly SettingsStore _settingsStore = new(AppPaths.User);
    private readonly WallpaperCache _cache = new(AppPaths.WallpaperCache);

    private readonly List<SidebarItem> _library =
    [
        new("All wallpapers", "\uE91B", null),
        new("Favorites", "\uEB51", null, isFavorites: true),
    ];
    private readonly List<SidebarItem> _themes = [];
    private readonly Dictionary<string, WallpaperItem> _items = [];
    private List<WallpaperItem> _visible = [];

    private PaperCatalog? _catalog;
    private HashSet<string> _favorites = [];
    private SidebarItem _selected;
    private RotationConfiguration _rotation;
    private DispatcherQueueTimer? _rotationTimer;
    private DispatcherQueueTimer? _statusTimer;
    private string? _currentWallpaperId;
    private bool _loading;
    private bool _suppressSettingsEvents;

    public MainPage()
    {
        InitializeComponent();
        _selected = _library[0];
        _selected.IsSelected = true;
        LibraryList.ItemsSource = _library;
        _rotation = _settingsStore.LoadRotation();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _favorites = _favoritesStore.All();
        try
        {
            if (_catalogStore.CachedCatalog() is { } cached) ShowCatalog(cached, []);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }

        await RefreshCatalogAsync();
    }

    private async Task RefreshCatalogAsync()
    {
        if (_loading) return;
        _loading = true;
        Busy.IsActive = true;
        try
        {
            var fresh = await _api.FetchCatalogAsync();
            var newIds = await Task.Run(() => _catalogStore.SaveCatalog(fresh));
            ShowCatalog(fresh, newIds);
            SetStatus(newIds.Count > 0 ? $"{newIds.Count} new" : null);
        }
        catch (Exception ex)
        {
            SetStatus(_catalog is null ? "Couldn't load wallpapers. Check your connection." : "Offline. Showing saved wallpapers.", persist: true);
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _loading = false;
            Busy.IsActive = false;
            LoadingRing.IsActive = false;
            UpdateEmptyState();
        }

        UpdateRotationTimer();
    }

    private void ShowCatalog(PaperCatalog catalog, HashSet<string> newIds)
    {
        _catalog = catalog;
        _items.Clear();
        foreach (var w in catalog.Wallpapers)
        {
            _items[w.Id] = new WallpaperItem(w) { IsFavorite = _favorites.Contains(w.Id), IsNew = newIds.Contains(w.Id) };
        }

        var selectedKey = _selected.ThemeKey;
        _themes.Clear();
        _themes.AddRange(catalog.Themes.Select(t => new SidebarItem(t.Label, null, t.Key)));
        if (selectedKey is not null)
        {
            var match = _themes.FirstOrDefault(t => t.ThemeKey == selectedKey);
            _selected = match ?? _library[0];
            _selected.IsSelected = true;
        }
        ThemeList.ItemsSource = null;
        ThemeList.ItemsSource = _themes;

        RandomButton.IsEnabled = true;
        LoadingRing.IsActive = false;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_catalog is null) return;

        var theme = _selected.ThemeKey is { } key ? _catalog.Themes.FirstOrDefault(t => t.Key == key) : null;
        TitleText.Text = _selected.IsFavorites ? "Favorites" : theme?.Label ?? "All wallpapers";
        SubtitleText.Text = theme?.Desc ?? "";
        SubtitleText.Visibility = theme is null ? Visibility.Collapsed : Visibility.Visible;

        _visible = CatalogSearch.Filter(_catalog, SearchBox.Text ?? "", _selected.ThemeKey, _selected.IsFavorites, _favorites)
            .Select(w => _items[w.Id])
            .ToList();
        Gallery.ItemsSource = _visible;
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _catalog is not null && _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _selected.IsFavorites && string.IsNullOrWhiteSpace(SearchBox.Text)
            ? "No favorites yet"
            : "No wallpapers";
    }

    private void OnSidebarClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SidebarItem item) return;
        _selected.IsSelected = false;
        _selected = item;
        item.IsSelected = true;
        ApplyFilter();
        Gallery.ScrollIntoView(_visible.FirstOrDefault());
    }

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplyFilter();

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshCatalogAsync();

    private async void OnRandomClick(object sender, RoutedEventArgs e)
    {
        var pool = _visible.Count > 0 ? _visible.Select(i => i.Wallpaper).ToList() : _catalog?.Wallpapers;
        if (pool is null || pool.Count == 0) return;
        var candidates = pool.Count > 1 ? pool.Where(w => w.Id != _currentWallpaperId).ToList() : pool;
        await SetWallpaperAsync(candidates[Random.Shared.Next(candidates.Count)]);
    }

    private async void OnSetClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is WallpaperItem item) await SetWallpaperAsync(item.Wallpaper);
    }

    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is WallpaperItem item) ToggleFavorite(item);
    }

    private void ToggleFavorite(WallpaperItem item)
    {
        _favorites = _favoritesStore.Toggle(item.Wallpaper.Id);
        item.IsFavorite = _favorites.Contains(item.Wallpaper.Id);
        if (_selected.IsFavorites) ApplyFilter();
    }

    private async Task SetWallpaperAsync(PaperWallpaper wallpaper)
    {
        Busy.IsActive = true;
        SetStatus($"Downloading {wallpaper.Name}…", persist: true);
        try
        {
            var path = await _cache.GetFileAsync(wallpaper);
            await Task.Run(() => WallpaperSetter.Apply(path));
            _currentWallpaperId = wallpaper.Id;
            MarkSeen(wallpaper.Id);
            SetStatus($"Set {wallpaper.Name}");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't set {wallpaper.Name}", persist: true);
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            Busy.IsActive = false;
        }
    }

    private void MarkSeen(string id)
    {
        if (!_items.TryGetValue(id, out var item) || !item.IsNew) return;
        item.IsNew = false;
        try { _catalogStore.MarkSeen([id]); } catch (IOException) { }
    }

    private async void OnWallpaperClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WallpaperItem item) return;
        MarkSeen(item.Wallpaper.Id);

        var preview = new Grid { CornerRadius = new CornerRadius(8), MaxHeight = 560 };
        preview.Children.Add(new Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            Source = new BitmapImage(item.ThumbUri),
        });
        preview.Children.Add(new Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            Source = new BitmapImage(new Uri(item.Wallpaper.WallpaperUrl)) { DecodePixelWidth = 1800 },
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = item.Name,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    preview,
                    new TextBlock
                    {
                        Text = string.Join(" · ", item.Wallpaper.Tags),
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    },
                },
            },
            PrimaryButtonText = "Set wallpaper",
            SecondaryButtonText = item.IsFavorite ? "Unfavorite" : "Favorite",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 1100.0;
        dialog.Resources["ContentDialogMaxHeight"] = 900.0;
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            ToggleFavorite(item);
            dialog.SecondaryButtonText = item.IsFavorite ? "Unfavorite" : "Favorite";
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await SetWallpaperAsync(item.Wallpaper);
        }
    }

    private void OnGallerySizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Gallery.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        const double minCard = 260;
        var available = Math.Max(0, e.NewSize.Width - Gallery.Padding.Left - Gallery.Padding.Right - 4);
        var columns = Math.Max(1, (int)(available / minCard));
        var width = Math.Floor(available / columns);
        panel.ItemWidth = width;
        panel.ItemHeight = Math.Round((width - 12) * 9 / 16) + 68;
    }

    private void SetStatus(string? message, bool persist = false)
    {
        StatusText.Text = message ?? "";
        _statusTimer?.Stop();
        if (persist || message is null) return;
        if (_statusTimer is null)
        {
            _statusTimer = DispatcherQueue.CreateTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(4);
            _statusTimer.IsRepeating = false;
            _statusTimer.Tick += (_, _) => StatusText.Text = "";
        }
        _statusTimer.Start();
    }

    // Settings

    private void OnSettingsOpening(object sender, object e)
    {
        _suppressSettingsEvents = true;
        RotationToggle.IsOn = _rotation.IsEnabled;

        ScopeCombo.Items.Clear();
        ScopeCombo.Items.Add(new ComboBoxItem { Content = "All wallpapers", Tag = "" });
        foreach (var theme in _catalog?.Themes ?? [])
            ScopeCombo.Items.Add(new ComboBoxItem { Content = theme.Label, Tag = theme.Key });
        ScopeCombo.SelectedItem = ScopeCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => _rotation.ScopeKind == RotationScopeKind.Theme && (string)i.Tag == _rotation.ThemeKey)
            ?? ScopeCombo.Items[0];

        var preset = IntervalCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => double.TryParse(i.Tag as string, out var s) && s == _rotation.IntervalSeconds);
        IntervalCombo.SelectedItem = preset ?? IntervalCombo.Items.OfType<ComboBoxItem>().Last();
        CustomMinutes.Value = Math.Round(_rotation.IntervalSeconds / 60);
        CustomMinutes.Visibility = preset is null ? Visibility.Visible : Visibility.Collapsed;

        CacheSizeText.Text = FormatBytes(_cache.TotalBytes());
        _suppressSettingsEvents = false;
    }

    private void OnRotationChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsEvents) return;
        _rotation.IsEnabled = RotationToggle.IsOn;
        var key = (ScopeCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        _rotation.ScopeKind = string.IsNullOrEmpty(key) ? RotationScopeKind.All : RotationScopeKind.Theme;
        _rotation.ThemeKey = string.IsNullOrEmpty(key) ? null : key;
        SaveRotation();
    }

    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsEvents) return;
        var tag = (IntervalCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var isCustom = !double.TryParse(tag, out var seconds);
        CustomMinutes.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        if (isCustom) return;
        _rotation.IntervalSeconds = seconds;
        SaveRotation();
    }

    private void OnCustomMinutesChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressSettingsEvents || double.IsNaN(args.NewValue)) return;
        _rotation.IntervalSeconds = RotationEngine.Normalize(args.NewValue * 60);
        SaveRotation();
    }

    private void SaveRotation()
    {
        _rotation.IntervalSeconds = RotationEngine.Normalize(_rotation.IntervalSeconds);
        _settingsStore.SaveRotation(_rotation);
        UpdateRotationTimer();
    }

    private void UpdateRotationTimer()
    {
        _rotationTimer?.Stop();
        if (!_rotation.IsEnabled || _catalog is null) return;
        if (_rotationTimer is null)
        {
            _rotationTimer = DispatcherQueue.CreateTimer();
            _rotationTimer.Tick += async (_, _) =>
            {
                if (_catalog is not null && RotationEngine.Next(_catalog, _rotation, _currentWallpaperId) is { } next)
                    await SetWallpaperAsync(next);
            };
        }
        _rotationTimer.Interval = TimeSpan.FromSeconds(RotationEngine.Normalize(_rotation.IntervalSeconds));
        _rotationTimer.Start();
    }

    private void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        _cache.Clear(keepPath: WallpaperSetter.CurrentPath());
        CacheSizeText.Text = FormatBytes(_cache.TotalBytes());
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GB",
        >= 1024 * 1024 => $"{bytes / (1024d * 1024):0} MB",
        >= 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} bytes",
    };
}
