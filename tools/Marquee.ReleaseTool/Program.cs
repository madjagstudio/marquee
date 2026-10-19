using Marquee.Host.Updates;
using Marquee.ReleaseTool;

// The owner's release tool; tools/new-signing-key.ps1 and tools/sign-release.ps1 run it. See docs/releasing.md.
try
{
    return new ReleaseCommands(Console.Out, Console.Error, ConsolePassword.Ask, ReleaseKey.Public).Run(args);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
