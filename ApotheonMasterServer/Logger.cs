using System;
using System.Runtime.CompilerServices;

namespace ApotheonMasterServer;

public interface ILogger
{
    void Debug(string message);

    void Info(string message);

    void Warning(string message);

    void Error(string message);
}

public class Logger : ILogger
{
    #region Public Methods

    public void Debug(string str)
    {
#if DEBUG
        Print(nameof(Debug), str);
#endif
    }

    public void Info(string str)
    {
        Print(nameof(Info), str);
    }

    public void Warning(string str)
    {
        Print(nameof(Warning), str);
    }

    public void Error(string str)
    {
        Print(nameof(Error), str);
    }

    #endregion

    #region Non-Public Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Print(string type, string str)
    {
        Console.WriteLine($"[{type} {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z] {str}");
    }

    #endregion
}
