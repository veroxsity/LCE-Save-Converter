using LceWorldConverter;

namespace LceWorldConverter.Gui;

public sealed class UiConversionLogger(Action<string> appendLog) : IConversionLogger
{
    public void Info(string message) => appendLog(message);

    public void Error(string message) => appendLog(message);
}
