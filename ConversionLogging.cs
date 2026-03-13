namespace LceWorldConverter;

public interface IConversionLogger
{
    void Info(string message);
    void Error(string message);
}

public sealed class ConsoleConversionLogger : IConversionLogger
{
    public void Info(string message) => Console.WriteLine(message);

    public void Error(string message) => Console.Error.WriteLine(message);
}

internal sealed class NullConversionLogger : IConversionLogger
{
    public static readonly NullConversionLogger Instance = new();

    public void Info(string message)
    {
    }

    public void Error(string message)
    {
    }
}