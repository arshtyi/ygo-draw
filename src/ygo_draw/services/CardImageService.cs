using System.IO;

namespace ygo_draw.services;

public sealed class CardImageService(ProjectPaths paths, ProcessService processService)
{
    public async Task EnsureCenterImageAsync(
        string cardImage,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var imageDirectory = Path.Combine(paths.TemplateRoot, "images");
        Directory.CreateDirectory(imageDirectory);

        var pngPath = Path.Combine(imageDirectory, $"{cardImage}.png");
        if (File.Exists(pngPath))
        {
            return;
        }

        log($"准备中心图: {cardImage}");
        var command = await BuildPythonAssetCommandAsync(
            "prepare_card_image.py",
            [cardImage, "--project-root", paths.ProjectRoot],
            cancellationToken);
        var code = await processService.RunAsync(
            command.FileName,
            command.Arguments,
            paths.AssetToolsDir,
            log,
            cancellationToken);

        if (code != 0 || !File.Exists(pngPath))
        {
            throw new InvalidOperationException($"中心图准备失败，退出码: {code}");
        }
    }

    private async Task<AssetCommand> BuildPythonAssetCommandAsync(
        string scriptName,
        IReadOnlyList<string> scriptArguments,
        CancellationToken cancellationToken)
    {
        if (await processService.CanStartAsync("uv", "--version", paths.AssetToolsDir, cancellationToken))
        {
            return new AssetCommand("uv", ["run", "python", scriptName, ..scriptArguments]);
        }

        return new AssetCommand("python", [scriptName, ..scriptArguments]);
    }

    private sealed record AssetCommand(string FileName, IReadOnlyList<string> Arguments);
}
