namespace Lockerit.Core.Security;

public sealed class QrCodeMatrix
{
    private readonly bool[,] _modules;

    internal QrCodeMatrix(bool[,] modules)
    {
        _modules = modules;
        Size = modules.GetLength(0);
    }

    public int Size { get; }

    public bool IsDark(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        return _modules[y, x];
    }
}
