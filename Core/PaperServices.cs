using System.Text.Json;

namespace Paperwalls.Core;

public static class AppPaths
{
    public static string Root { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        try
        {
            // Packaged: LocalState folder, removed cleanly on uninstall.
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paperwalls");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string Catalog => Path.Combine(Root, "Catalog");
    public static string User => Path.Combine(Root, "User");
    public static string WallpaperCache => Path.Combine(Root, "WallpaperCache");
}

public sealed class PaperApiClient
{
    public static readonly Uri CatalogUri = new("https://burkeholland.github.io/paper/api.json");
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<PaperCatalog> FetchCatalogAsync()
    {
        await using var stream = await Http.GetStreamAsync(CatalogUri);
        var catalog = await JsonSerializer.DeserializeAsync(stream, PaperJsonContext.Default.PaperCatalog)
            ?? throw new InvalidDataException("Empty catalog");
        if (catalog.Themes.Count == 0 || catalog.Wallpapers.Count == 0)
            throw new InvalidDataException("Empty catalog");
        return catalog;
    }
}

public sealed class CatalogStore(string directory)
{
    private string CatalogPath => Path.Combine(directory, "catalog.json");
    private string SeenPath => Path.Combine(directory, "seen-wallpapers.json");

    public PaperCatalog? CachedCatalog()
    {
        if (!File.Exists(CatalogPath)) return null;
        return JsonSerializer.Deserialize(File.ReadAllText(CatalogPath), PaperJsonContext.Default.PaperCatalog);
    }

    /// <summary>Saves the catalog and returns IDs added since the user first launched the app.</summary>
    public HashSet<string> SaveCatalog(PaperCatalog catalog)
    {
        Directory.CreateDirectory(directory);
        var seen = LoadSeen();
        var current = catalog.Wallpapers.Select(w => w.Id).ToHashSet();
        AtomicWrite(CatalogPath, JsonSerializer.Serialize(catalog, PaperJsonContext.Default.PaperCatalog));

        if (seen is null)
        {
            SaveSeen(current);
            return [];
        }

        current.ExceptWith(seen);
        return current;
    }

    public void MarkSeen(IEnumerable<string> ids)
    {
        var seen = LoadSeen() ?? [];
        seen.UnionWith(ids);
        SaveSeen(seen);
    }

    private HashSet<string>? LoadSeen()
    {
        if (!File.Exists(SeenPath)) return null;
        return JsonSerializer.Deserialize(File.ReadAllText(SeenPath), PaperJsonContext.Default.ListString)?.ToHashSet() ?? [];
    }

    private void SaveSeen(HashSet<string> ids)
    {
        Directory.CreateDirectory(directory);
        AtomicWrite(SeenPath, JsonSerializer.Serialize(ids.Order().ToList(), PaperJsonContext.Default.ListString));
    }

    internal static void AtomicWrite(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}

public sealed class FavoritesStore(string directory)
{
    private string FilePath => Path.Combine(directory, "favorites.json");

    public HashSet<string> All()
    {
        if (!File.Exists(FilePath)) return [];
        return JsonSerializer.Deserialize(File.ReadAllText(FilePath), PaperJsonContext.Default.ListString)?.ToHashSet() ?? [];
    }

    public HashSet<string> Toggle(string id)
    {
        var favorites = All();
        if (!favorites.Remove(id)) favorites.Add(id);
        Directory.CreateDirectory(directory);
        CatalogStore.AtomicWrite(FilePath, JsonSerializer.Serialize(favorites.Order().ToList(), PaperJsonContext.Default.ListString));
        return favorites;
    }
}

public sealed class SettingsStore(string directory)
{
    private string FilePath => Path.Combine(directory, "rotation.json");

    public RotationConfiguration LoadRotation()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), PaperJsonContext.Default.RotationConfiguration) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        return new();
    }

    public void SaveRotation(RotationConfiguration config)
    {
        Directory.CreateDirectory(directory);
        CatalogStore.AtomicWrite(FilePath, JsonSerializer.Serialize(config, PaperJsonContext.Default.RotationConfiguration));
    }
}

public sealed class WallpaperCache(string directory, long maxBytes = 750L * 1024 * 1024)
{
    public string LocalPath(PaperWallpaper wallpaper)
    {
        var ext = Path.GetExtension(new Uri(wallpaper.WallpaperUrl).AbsolutePath);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        return Path.Combine(directory, wallpaper.Id + ext);
    }

    public async Task<string> GetFileAsync(PaperWallpaper wallpaper)
    {
        Directory.CreateDirectory(directory);
        var path = LocalPath(wallpaper);
        if (File.Exists(path))
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            return path;
        }

        var temp = path + ".download";
        using (var response = await PaperApiClient.Http.GetAsync(wallpaper.WallpaperUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(temp);
            await response.Content.CopyToAsync(file);
        }
        File.Move(temp, path, overwrite: true);
        EnforceSizeLimit(path);
        return path;
    }

    public long TotalBytes() =>
        Directory.Exists(directory) ? new DirectoryInfo(directory).EnumerateFiles().Sum(f => f.Length) : 0;

    public void Clear(string? keepPath = null)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles())
        {
            // Keep the file that is currently the desktop wallpaper.
            if (keepPath is not null && string.Equals(file.FullName, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
            try { file.Delete(); } catch (IOException) { }
        }
    }

    private void EnforceSizeLimit(string justAdded)
    {
        var files = new DirectoryInfo(directory).EnumerateFiles().OrderBy(f => f.LastAccessTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= maxBytes) break;
            if (string.Equals(file.FullName, justAdded, StringComparison.OrdinalIgnoreCase)) continue;
            total -= file.Length;
            try { file.Delete(); } catch (IOException) { }
        }
    }
}

public static class CatalogSearch
{
    public static IEnumerable<PaperWallpaper> Filter(PaperCatalog catalog, string search, string? themeKey, bool favoritesOnly, ISet<string> favorites)
    {
        var query = search.Trim();
        var labels = catalog.Themes.ToDictionary(t => t.Key, t => t.Label);
        return catalog.Wallpapers.Where(w =>
        {
            if (themeKey is not null && w.Theme != themeKey) return false;
            if (favoritesOnly && !favorites.Contains(w.Id)) return false;
            if (query.Length == 0) return true;
            return w.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || w.Theme.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (labels.TryGetValue(w.Theme, out var label) && label.Contains(query, StringComparison.OrdinalIgnoreCase))
                || w.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
        });
    }
}

public static class RotationEngine
{
    public const double MinimumIntervalSeconds = 60;

    public static List<PaperWallpaper> Eligible(PaperCatalog catalog, RotationConfiguration config) =>
        config.ScopeKind == RotationScopeKind.Theme && config.ThemeKey is { } key
            ? catalog.Wallpapers.Where(w => w.Theme == key).ToList()
            : catalog.Wallpapers;

    public static PaperWallpaper? Next(PaperCatalog catalog, RotationConfiguration config, string? currentId)
    {
        var eligible = Eligible(catalog, config);
        if (eligible.Count == 0) return null;
        var index = eligible.FindIndex(w => w.Id == currentId);
        return index < 0 ? eligible[0] : eligible[(index + 1) % eligible.Count];
    }

    public static double Normalize(double seconds) => Math.Max(MinimumIntervalSeconds, seconds);
}
