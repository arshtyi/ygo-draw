using System.Diagnostics;
using System.IO;

namespace ygo_draw.services;

public sealed class TinymistPreviewService(ProjectPaths paths) : IDisposable
{
    public const string PreviewUrl = "http://127.0.0.1:23625";
    private Process? _previewProcess;
    private string? _previewInputPath;
    private string? _previewInputSignature;
    private bool _previewProcessStarted;

    public async Task EnsurePreviewAsync(
        string typPath,
        IReadOnlyDictionary<string, string> inputs,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var inputSignature = BuildInputSignature(inputs);
        if (IsPreviewProcessRunning() &&
            string.Equals(_previewInputPath, typPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_previewInputSignature, inputSignature, StringComparison.Ordinal))
        {
            log("Tinymist preview 已更新。");
            return;
        }

        if (IsPreviewProcessRunning())
        {
            await StopPreviewProcessAsync(cancellationToken);
        }
        else
        {
            ClearPreviewProcess();
        }

        var startInfo = BuildStartInfo("preview", typPath, inputs);
        startInfo.ArgumentList.Add("--no-open");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) log(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) log(args.Data); };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("无法启动 tinymist preview。");
        }

        _previewProcess = process;
        _previewProcessStarted = true;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _previewInputPath = typPath;
        _previewInputSignature = inputSignature;
        log($"Tinymist preview server: {PreviewUrl}");
    }

    public ProcessStartInfo BuildCompileStartInfo(
        string typPath,
        string outputPath,
        IReadOnlyDictionary<string, string> inputs)
    {
        var startInfo = BuildStartInfo("compile", typPath, inputs);
        startInfo.ArgumentList.Add("--ppi");
        startInfo.ArgumentList.Add("600");
        startInfo.ArgumentList.Add(outputPath);
        return startInfo;
    }

    private ProcessStartInfo BuildStartInfo(
        string command,
        string typPath,
        IReadOnlyDictionary<string, string> inputs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "tinymist",
            WorkingDirectory = paths.ProjectRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add("--root");
        startInfo.ArgumentList.Add(paths.ProjectRoot);
        startInfo.ArgumentList.Add("--font-path");
        startInfo.ArgumentList.Add(Path.Combine(paths.TemplateRoot, "font"));
        foreach (var (key, value) in inputs)
        {
            startInfo.ArgumentList.Add("--input");
            startInfo.ArgumentList.Add($"{key}={value}");
        }
        startInfo.ArgumentList.Add(typPath);
        return startInfo;
    }

    private static string BuildInputSignature(IReadOnlyDictionary<string, string> inputs)
    {
        return string.Join(
            "\u001f",
            inputs.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item.Key + "=" + item.Value));
    }

    public void Dispose()
    {
        if (!IsPreviewProcessRunning())
        {
            ClearPreviewProcess();
            return;
        }

        try
        {
            _previewProcess?.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        finally
        {
            ClearPreviewProcess();
        }
    }

    private bool IsPreviewProcessRunning()
    {
        if (_previewProcess is null || !_previewProcessStarted)
        {
            return false;
        }

        try
        {
            return !_previewProcess.HasExited;
        }
        catch (InvalidOperationException)
        {
            ClearPreviewProcess();
            return false;
        }
    }

    private async Task StopPreviewProcessAsync(CancellationToken cancellationToken)
    {
        if (_previewProcess is null)
        {
            return;
        }

        try
        {
            if (_previewProcessStarted && !_previewProcess.HasExited)
            {
                _previewProcess.Kill(entireProcessTree: true);
                await _previewProcess.WaitForExitAsync(cancellationToken);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            ClearPreviewProcess();
        }
    }

    private void ClearPreviewProcess()
    {
        try
        {
            _previewProcess?.Dispose();
        }
        catch
        {
        }

        _previewProcess = null;
        _previewProcessStarted = false;
        _previewInputPath = null;
        _previewInputSignature = null;
    }
}
