using System.IO;

namespace ygo_draw.services;

public sealed record ResourceStatus(bool IsReady, string Message);

public sealed class ResourceIntegrityService(ProjectPaths paths)
{
    public ResourceStatus Check()
    {
        var failures = new List<string>();

        if (!File.Exists(paths.OtCardsJson))
        {
            failures.Add("assets/ot/card/ot.json 不存在");
        }

        if (!File.Exists(paths.RdCardsJson))
        {
            failures.Add("assets/rd/card/rd.json 不存在");
        }

        foreach (var requiredFile in paths.RequiredTypstFiles)
        {
            if (!File.Exists(requiredFile))
            {
                failures.Add($"缺少 Typst 文件: {Path.GetRelativePath(paths.ProjectRoot, requiredFile)}");
            }
        }

        foreach (var requiredFile in paths.RequiredAssetFiles)
        {
            if (!File.Exists(requiredFile))
            {
                failures.Add($"缺少资源文件: {Path.GetRelativePath(paths.ProjectRoot, requiredFile)}");
            }
        }

        return failures.Count == 0
            ? new ResourceStatus(true, "资源已就绪")
            : new ResourceStatus(false, "资源不完整，建议点击“下载”更新。\n" + string.Join("\n", failures));
    }
}
