using System.Diagnostics;
using System.Text;

namespace IosInspector.Services;

public sealed record ToolResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;
}

public sealed class ToolRunner
{
    public async Task<ToolResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ToolResult(process.ExitCode, await outputTask, await errorTask);
    }

    public Process StartStreaming(string executable, IEnumerable<string> arguments, Action<string> onLine, Action<int> onExit)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.Exited += (_, _) => onExit(process.ExitCode);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }
}
