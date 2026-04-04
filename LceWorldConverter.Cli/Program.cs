namespace LceWorldConverter.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        return CliCommandRouter.Run(args);
    }
}
