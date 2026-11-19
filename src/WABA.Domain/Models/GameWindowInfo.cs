namespace WABA.Domain.Models;

public sealed record GameWindowInfo(
    nint Handle,
    string Title,
    string ProcessName,
    int ClientWidth,
    int ClientHeight,
    int ScreenLeft,
    int ScreenTop)
{
    public override string ToString()
        => $"{Title}  [{ProcessName}]  {ClientWidth}×{ClientHeight}";
}
