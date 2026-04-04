using LceWorldConverter;

namespace LceWorldConverter.Cli;

public sealed class ConsoleConversionLogger : IConversionLogger
{
    public void Info(string message) => Console.WriteLine(message);

    public void Error(string message) => Console.Error.WriteLine(message);
}
