namespace WABA.Domain.Models;

public sealed record PixelRegion(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}
