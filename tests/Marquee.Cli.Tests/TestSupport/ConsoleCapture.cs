namespace Marquee.Cli.Tests.TestSupport;

/// Collects what a command writes to stdout and stderr until disposed.
sealed class ConsoleCapture : IDisposable
{
    readonly TextWriter _out = Console.Out;
    readonly TextWriter _error = Console.Error;
    readonly StringWriter _capturedOut = new();
    readonly StringWriter _capturedError = new();

    public ConsoleCapture()
    {
        Console.SetOut(_capturedOut);
        Console.SetError(_capturedError);
    }

    public string Out => _capturedOut.ToString();

    public string Error => _capturedError.ToString();

    public void Dispose()
    {
        Console.SetOut(_out);
        Console.SetError(_error);
    }
}
