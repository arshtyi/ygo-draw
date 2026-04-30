using System.Diagnostics;

namespace ygo_draw.services;

public sealed class ProcessService
{
    public async Task<bool> CanStartAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) log(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) log(args.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    public async Task<int> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) log(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) log(args.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    public async Task<int> RunAsync(
        ProcessStartInfo startInfo,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => { if (args.Data is not null) log(args.Data); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is not null) log(args.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
