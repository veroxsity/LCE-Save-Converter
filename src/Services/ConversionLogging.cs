namespace LceWorldConverter;

public interface IConversionLogger
{
    void Info(string message);
    void Error(string message);
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
