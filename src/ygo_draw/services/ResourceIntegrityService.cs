using System.IO;
using System.Security.Cryptography;

namespace ygo_draw.services;

public sealed record ResourceStatus(bool IsReady, string Message);

public sealed class ResourceIntegrityService(ProjectPaths paths)
{
    public ResourceStatus Check()
    {
        var failures = new List<string>();

        CheckHashPair(paths.TemplateArchive, paths.TemplateSha256, "模板压缩包", failures);
        CheckHashPair(paths.CardsCacheJson, paths.CardsSha256, "卡片数据压缩缓存", failures);

        if (!File.Exists(paths.AssetsCardsJson))
        {
            failures.Add("assets/cards/cards.json 不存在");
        }

        if (!File.Exists(Path.Combine(paths.TypstYgoRoot, "lib", "mod.typ")) ||
            !File.Exists(Path.Combine(paths.TypstYgoRoot, "lib", "card", "types.typ")))
        {
            failures.Add("assets/typst-ygo 不存在或不完整");
        }

        if (!File.Exists(paths.TemplateMarker))
        {
            failures.Add("模板解压标记不存在");
        }
        else if (File.Exists(paths.TemplateSha256))
        {
            var expected = ReadSha256(paths.TemplateSha256);
            var marker = File.ReadAllText(paths.TemplateMarker).Trim().ToLowerInvariant();
            if (!string.Equals(expected, marker, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("模板解压标记与缓存 hash 不一致");
            }
        }

        foreach (var requiredFile in paths.RequiredTemplateFiles)
        {
            if (!File.Exists(requiredFile))
            {
                failures.Add($"缺少模板文件: {Path.GetFileName(requiredFile)}");
            }
        }

        return failures.Count == 0
            ? new ResourceStatus(true, "资源已就绪")
            : new ResourceStatus(false, "资源不完整，建议点击“下载”更新。\n" + string.Join("\n", failures));
    }

    private static void CheckHashPair(string contentPath, string sha256Path, string label, List<string> failures)
    {
        if (!File.Exists(contentPath))
        {
            failures.Add($"{label}不存在");
            return;
        }

        if (!File.Exists(sha256Path))
        {
            failures.Add($"{label} hash 文件不存在");
            return;
        }

        var expected = ReadSha256(sha256Path);
        var actual = ComputeSha256(contentPath);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{label} hash 校验失败");
        }
    }

    private static string ReadSha256(string path)
    {
        var token = File.ReadAllText(path).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token?.ToLowerInvariant() ?? string.Empty;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
