namespace IosInspector.Models;

public sealed record RemoteCommandResult(int ExitStatus, string Output, string Error)
{
    public bool Success => ExitStatus == 0;
}
