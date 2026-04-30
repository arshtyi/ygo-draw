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
        AssetsCardsJson = Path.Combine(AssetsRoot, "cards", "cards.json");
        TemplateArchive = Path.Combine(CacheDownloads, "yugioh-card-template.tar.xz");
        TemplateSha256 = Path.Combine(CacheDownloads, "yugioh-card-template.tar.xz.sha256");
        CardsCacheJson = Path.Combine(CacheDownloads, "cards.json");
        CardsSha256 = Path.Combine(CacheDownloads, "cards.json.sha256");
        TemplateRoot = Path.Combine(AssetsRoot, "card_templates");
        TypstYgoRoot = Path.Combine(AssetsRoot, "typst-ygo");
        PreviewTemplatePath = Path.Combine(ProjectRoot, "src", "ygo_draw", "templates", "card-preview.typ");
        AppIconPath = Path.Combine(ProjectRoot, "public", "icons", "ygo_draw.png");
        TemplateMarker = Path.Combine(TemplateRoot, ".template_archive.sha256");
        AssetToolsDir = Path.Combine(ProjectRoot, "scripts", "asset_tools");
        DownloadScript = Path.Combine(AssetToolsDir, "download_assets.py");
        GuiStatePath = Path.Combine(ProjectRoot, ".cache", "gui_state.json");

        Directory.CreateDirectory(CacheDownloads);
        Directory.CreateDirectory(AssetsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(AssetsCardsJson)!);
    }

    public string ProjectRoot { get; }
    public string CacheRoot { get; }
    public string AssetsRoot { get; }
    public string CacheDownloads { get; }
    public string AssetsCardsJson { get; }
    public string TemplateArchive { get; }
    public string TemplateSha256 { get; }
    public string CardsCacheJson { get; }
    public string CardsSha256 { get; }
    public string TemplateRoot { get; }
    public string TypstYgoRoot { get; }
    public string PreviewTemplatePath { get; }
    public string AppIconPath { get; }
    public string TemplateMarker { get; }
    public string AssetToolsDir { get; }
    public string DownloadScript { get; }
    public string GuiStatePath { get; }

    public string[] RequiredTemplateFiles =>
    [
        Path.Combine(TemplateRoot, "figure", "cards", "card-effect.png"),
        Path.Combine(TemplateRoot, "figure", "attributes", "attribute-light.png"),
        Path.Combine(TemplateRoot, "font", "sc", "Yu-Gi-Oh! DFKaiW5-A（简体中文）.ttf")
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
