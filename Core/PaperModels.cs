using System.Text.Json.Serialization;

namespace Paperwalls.Core;

public sealed record PaperCatalog(
    string Name,
    string Description,
    string BaseUrl,
    string WallpapersPath,
    string ThumbsPath,
    List<PaperTheme> Themes,
    List<PaperWallpaper> Wallpapers);

public sealed record PaperTheme(string Key, string Label, string Desc);

public sealed record PaperWallpaper(
    string Id,
    string Name,
    string Theme,
    List<string> Tags,
    string File,
    string WallpaperUrl,
    string ThumbUrl);

public enum RotationScopeKind { All, Theme }

public sealed class RotationConfiguration
{
    public bool IsEnabled { get; set; }
    public double IntervalSeconds { get; set; } = 3600;
    public RotationScopeKind ScopeKind { get; set; } = RotationScopeKind.All;
    public string? ThemeKey { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PaperCatalog))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(RotationConfiguration))]
internal sealed partial class PaperJsonContext : JsonSerializerContext;
