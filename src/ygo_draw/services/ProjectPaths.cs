using System.IO;

namespace ygo_draw.services;

public sealed class ProjectPaths
{
    public ProjectPaths()
    {
        var appDir = AppContext.BaseDirectory;
        ProjectRoot = FindProjectRoot(appDir);
        CacheRoot = Path.Combine(ProjectRoot, ".cache");
        AssetsRoot = Path.Combine(ProjectRoot, "assets");
        CacheDownloads = Path.Combine(CacheRoot, "downloads");
        OtAssetsRoot = Path.Combine(AssetsRoot, "ot");
        RdAssetsRoot = Path.Combine(AssetsRoot, "rd");
        OtCardsJson = Path.Combine(OtAssetsRoot, "card", "ot.json");
        RdCardsJson = Path.Combine(RdAssetsRoot, "card", "rd.json");
        TypstLibRoot = Path.Combine(ProjectRoot, "lib");
        PreviewTemplatePath = Path.Combine(ProjectRoot, "src", "ygo_draw", "templates", "card-preview.typ");
        AppIconPath = Path.Combine(ProjectRoot, "public", "icons", "ygo_draw.png");
        AssetToolsDir = Path.Combine(ProjectRoot, "scripts", "asset_tools");
        DownloadScript = Path.Combine(AssetToolsDir, "download_assets.py");
        GuiStatePath = Path.Combine(ProjectRoot, ".cache", "gui_state.json");

        Directory.CreateDirectory(CacheDownloads);
        Directory.CreateDirectory(AssetsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(OtCardsJson)!);
        Directory.CreateDirectory(Path.GetDirectoryName(RdCardsJson)!);
    }

    public string ProjectRoot { get; }
    public string CacheRoot { get; }
    public string AssetsRoot { get; }
    public string CacheDownloads { get; }
    public string OtAssetsRoot { get; }
    public string RdAssetsRoot { get; }
    public string OtCardsJson { get; }
    public string RdCardsJson { get; }
    public string TypstLibRoot { get; }
    public string PreviewTemplatePath { get; }
    public string AppIconPath { get; }
    public string AssetToolsDir { get; }
    public string DownloadScript { get; }
    public string GuiStatePath { get; }

    public string[] TypstFontPaths =>
    [
        Path.Combine(OtAssetsRoot, "font"),
        Path.Combine(RdAssetsRoot, "font")
    ];

    public string[] RequiredAssetFiles =>
    [
        Path.Combine(OtAssetsRoot, "frame", "effect.png"),
        Path.Combine(OtAssetsRoot, "attribute", "light.png"),
        Path.Combine(OtAssetsRoot, "font", "YGO_Card_JP.ttf"),
        Path.Combine(RdAssetsRoot, "frame", "effect.png"),
        Path.Combine(RdAssetsRoot, "attribute", "light.png"),
        Path.Combine(RdAssetsRoot, "font", "YGO_Card_JP.ttf")
    ];

    public string[] RequiredTypstFiles =>
    [
        Path.Combine(TypstLibRoot, "mod.typ"),
        Path.Combine(TypstLibRoot, "ot", "data.typ"),
        Path.Combine(TypstLibRoot, "rd", "data.typ")
    ];

    private static string FindProjectRoot(string start)
    {
        var root = FindProjectRootFrom(start) ?? FindProjectRootFrom(Directory.GetCurrentDirectory());
        return root ?? Directory.GetCurrentDirectory();
    }

    private static string? FindProjectRootFrom(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            var hasAssetTools = File.Exists(Path.Combine(
                current.FullName,
                "scripts",
                "asset_tools",
                "download_assets.py"));
            var hasProjectMarker =
                File.Exists(Path.Combine(current.FullName, "ygo_draw.sln")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git"));

            if (hasAssetTools && hasProjectMarker)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
