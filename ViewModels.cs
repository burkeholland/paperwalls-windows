using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Paperwalls.Core;

namespace Paperwalls;

public abstract partial class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class WallpaperItem(PaperWallpaper wallpaper) : Observable
{
    private bool _isFavorite;
    private bool _isNew;

    public PaperWallpaper Wallpaper { get; } = wallpaper;
    public string Name => Wallpaper.Name;
    public string SetLabel => $"Set {Wallpaper.Name} as wallpaper";
    public string Tags => string.Join(" · ", Wallpaper.Tags.Take(3));
    public Uri ThumbUri { get; } = new(wallpaper.ThumbUrl);

    public bool IsFavorite
    {
        get => _isFavorite;
        set { Set(ref _isFavorite, value); Raise(nameof(FavoriteGlyph)); Raise(nameof(FavoriteLabel)); }
    }

    public bool IsNew
    {
        get => _isNew;
        set { Set(ref _isNew, value); Raise(nameof(NewVisibility)); }
    }

    public string FavoriteGlyph => IsFavorite ? "\uEB52" : "\uEB51";
    public string FavoriteLabel => IsFavorite ? "Remove from favorites" : "Add to favorites";
    public Visibility NewVisibility => IsNew ? Visibility.Visible : Visibility.Collapsed;
}

public sealed partial class SidebarItem(string title, string? glyph, string? themeKey, bool isFavorites = false) : Observable
{
    private bool _isSelected;

    public string Title { get; } = title;
    public string? ThemeKey { get; } = themeKey;
    public bool IsFavorites { get; } = isFavorites;
    public string Glyph { get; } = glyph ?? "";
    public Visibility GlyphVisibility => glyph is null ? Visibility.Collapsed : Visibility.Visible;

    public bool IsSelected
    {
        get => _isSelected;
        set { Set(ref _isSelected, value); Raise(nameof(Background)); Raise(nameof(Weight)); }
    }

    public Microsoft.UI.Xaml.Media.Brush Background => IsSelected
        ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"]
        : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorTransparentBrush"];

    public Windows.UI.Text.FontWeight Weight => IsSelected
        ? Microsoft.UI.Text.FontWeights.SemiBold
        : Microsoft.UI.Text.FontWeights.Normal;
}
