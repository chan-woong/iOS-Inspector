using System.Diagnostics;
using IosInspector.Models;

namespace IosInspector.Services;

public sealed class IosDeviceService
{
    private readonly ToolRunner _runner = new();

    public string ToolDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "tools");
    private string Tool(string name) => File.Exists(Path.Combine(ToolDirectory, name + ".exe"))
        ? Path.Combine(ToolDirectory, name + ".exe")
        : name;

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken token = default)
    {
        var ids = await _runner.RunAsync(Tool("idevice_id"), ["-l"], token);
        if (!ids.Success) throw FriendlyError("단말 검색", ids);

        var devices = new List<DeviceInfo>();
        foreach (var udid in ids.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var info = await _runner.RunAsync(Tool("ideviceinfo"), ["-u", udid], token);
            var values = ParseKeyValues(info.Output);
            devices.Add(new DeviceInfo(udid,
                values.GetValueOrDefault("DeviceName", "이름 없는 iPhone"),
                values.GetValueOrDefault("ProductType", "Unknown"),
                values.GetValueOrDefault("ProductVersion", "?")));
        }
        return devices;
    }

    public Process StartSyslog(string udid, Action<string> onLine, Action<int> onExit) =>
        _runner.StartStreaming(Tool("idevicesyslog"), ["-u", udid], onLine, onExit);

    public Process StartSshTunnel(string udid, int localPort, Action<string> onLine, Action<int> onExit) =>
        _runner.StartStreaming(Tool("iproxy"), ["-u", udid, $"{localPort}:22"], onLine, onExit);

    public Process StartPortForward(string udid, int localPort, int devicePort, Action<string> onLine, Action<int> onExit) =>
        _runner.StartStreaming(Tool("iproxy"), ["-u", udid, $"{localPort}:{devicePort}"], onLine, onExit);

    public void OpenSshTerminal(string host, int port, string user)
    {
        var terminal = FindOnPath("wt.exe") ? "wt.exe" : "powershell.exe";
        var startInfo = new ProcessStartInfo { FileName = terminal, UseShellExecute = true };
        if (terminal.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("-NoExit");
            startInfo.ArgumentList.Add("-Command");
        }
        startInfo.ArgumentList.Add("ssh");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(port.ToString());
        startInfo.ArgumentList.Add($"{user}@{host}");
        Process.Start(startInfo);
    }

    public Task<ToolResult> InstallAsync(string udid, string ipaPath, CancellationToken token = default) =>
        _runner.RunAsync(Tool("ideviceinstaller"), ["-u", udid, "install", ipaPath], token);

    public Task<ToolResult> DownloadAsync(string udid, string remotePath, string localPath, CancellationToken token = default) =>
        _runner.RunAsync(Tool("afcclient"), ["-u", udid, "get", remotePath, localPath], token);

    public Task<ToolResult> UploadAsync(string udid, string localPath, string remotePath, CancellationToken token = default) =>
        _runner.RunAsync(Tool("afcclient"), ["-u", udid, "put", localPath, remotePath], token);

    public async Task<IReadOnlyList<AfcEntry>> ListDirectoryAsync(string udid, string remotePath, CancellationToken token = default)
    {
        var result = await _runner.RunAsync(Tool("afcclient"), ["-u", udid, "ls", remotePath], token);
        if (!result.Success) throw FriendlyError("폴더 조회", result);

        var names = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name is not "." and not ".." && !name.EndsWith(":"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var entries = new List<AfcEntry>();
        foreach (var name in names)
        {
            var fullPath = CombineRemote(remotePath, name);
            var info = await _runner.RunAsync(Tool("afcclient"), ["-u", udid, "info", fullPath], token);
            var isDirectory = info.Output.Contains("S_IFDIR", StringComparison.OrdinalIgnoreCase);
            long? size = null;
            foreach (var line in info.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2 && parts[0].Trim().Equals("st_size", StringComparison.OrdinalIgnoreCase) && long.TryParse(parts[1].Trim(), out var parsed)) size = parsed;
            }
            entries.Add(new AfcEntry(name, fullPath, isDirectory, size));
        }
        return entries.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Task<ToolResult> CreateDirectoryAsync(string udid, string remotePath, CancellationToken token = default) =>
        _runner.RunAsync(Tool("afcclient"), ["-u", udid, "mkdir", remotePath], token);

    public Task<ToolResult> DeleteAsync(string udid, string remotePath, bool recursive, CancellationToken token = default) =>
        _runner.RunAsync(Tool("afcclient"), ["-u", udid, "rm", recursive ? "-rf" : "-f", remotePath], token);

    public Task<ToolResult> ResignAsync(string ipaPath, string p12Path, string password, string profilePath, string outputPath, CancellationToken token = default) =>
        _runner.RunAsync(Tool("zsign"), ["-k", p12Path, "-p", password, "-m", profilePath, "-o", outputPath, ipaPath], token);

    public async Task<IpaSignatureStatus> CheckIpaSignatureAsync(string ipaPath, CancellationToken token = default)
    {
        var result = await _runner.RunAsync(Tool("zsign"), ["-C", ipaPath], token);
        var details = string.Join(Environment.NewLine, new[] { result.Output, result.Error }.Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
        var signed = details.Contains("Signed:", StringComparison.OrdinalIgnoreCase) &&
                     details.Contains("Yes", StringComparison.OrdinalIgnoreCase);
        var invalidWords = new[] { "expired", "revoked", "invalid", "not signed", "unsigned", "error", "failed" };
        var invalid = invalidWords.Any(word => details.Contains(word, StringComparison.OrdinalIgnoreCase));
        var valid = result.Success && signed && !invalid;
        var summary = valid ? "서명 확인 완료 · 바로 설치 가능" : signed ? "기존 서명을 사용할 수 없어 재서명이 필요합니다" : "미서명 IPA · 재서명이 필요합니다";
        return new IpaSignatureStatus(valid, summary, details);
    }

    public bool HasTool(string name) => File.Exists(Path.Combine(ToolDirectory, name + ".exe")) || FindOnPath(name + ".exe");

    private static bool FindOnPath(string file) => (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Any(path => File.Exists(Path.Combine(path, file)));

    private static string CombineRemote(string parent, string name) =>
        (parent.TrimEnd('/') + "/" + name.TrimStart('/')).Replace("//", "/");

    private static Dictionary<string, string> ParseKeyValues(string text) => text
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split(':', 2))
        .Where(parts => parts.Length == 2)
        .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

    private static InvalidOperationException FriendlyError(string action, ToolResult result) =>
        new($"{action}에 실패했습니다. Apple Devices/iTunes 드라이버와 libimobiledevice 도구를 확인하세요.\n\n{result.Error.Trim()}");
}
