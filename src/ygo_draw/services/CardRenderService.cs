using System.Diagnostics;
using System.IO;
using System.Text;
using ygo_draw.models;

namespace ygo_draw.services;

public sealed class CardRenderService(
    ProjectPaths paths,
    ProcessService processService,
    CardImageService imageService,
    TinymistPreviewService previewService) : IDisposable
{
    public async Task<string> RenderPreviewAsync(
        CardSummary card,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var inputs = TypstInputMapper.BuildInputs(card);
        await imageService.EnsureCenterImageAsync(
            TypstInputMapper.CardImage(card),
            log,
            cancellationToken);
        await previewService.EnsurePreviewAsync(
            paths.PreviewTemplatePath,
            inputs,
            log,
            cancellationToken);
        return paths.PreviewTemplatePath;
    }

    public async Task<string> ExportAsync(
        CardSummary card,
        string outputDirectory,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var inputs = TypstInputMapper.BuildInputs(card);
        await imageService.EnsureCenterImageAsync(
            TypstInputMapper.CardImage(card),
            log,
            cancellationToken);

        var outputPath = Path.Combine(outputDirectory, $"{BuildExportFileName(card)}.png");
        var startInfo = previewService.BuildCompileStartInfo(
            paths.PreviewTemplatePath,
            outputPath,
            inputs);
        var code = await processService.RunAsync(startInfo, log, cancellationToken);

        if (code != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException($"导出失败: {card.Name} ({card.Id}), exit code: {code}");
        }

        return outputPath;
    }

    public void Dispose()
    {
        previewService.Dispose();
    }

    private static string BuildExportFileName(CardSummary card)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var raw = $"{card.Id}-{card.Name}";
        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.ToString().Trim();
    }
}
