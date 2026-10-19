namespace Marquee.ReleaseTool;

/// Reads a password typed at the console without echoing it. The characters go into a fixed buffer that is cleared
/// afterwards, rather than a string or a growing list that would leave copies behind.
static class ConsolePassword
{
    const int MaximumLength = 1024;

    public static char[] Ask(string prompt)
    {
        // ReadKey needs a real console; a redirected stdin would mean the password sits in a file or a pipe.
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("The password must be typed at a console; input is redirected.");

        Console.Write(prompt);
        var buffer = new char[MaximumLength];
        var length = 0;
        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                    break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (length > 0)
                        buffer[--length] = '\0';
                }
                else if (!char.IsControl(key.KeyChar) && length < MaximumLength)
                {
                    buffer[length++] = key.KeyChar;
                }
            }

            Console.WriteLine();
            return buffer[..length];
        }
        finally
        {
            Array.Clear(buffer);
        }
    }
}
