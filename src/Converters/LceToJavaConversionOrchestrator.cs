namespace LceWorldConverter;

public sealed class LceToJavaConversionOrchestrator
{
    public ConversionResult Convert(ConversionOptions options, IConversionLogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        return LceWorldConversionService.ConvertLceToJavaCore(options, logger);
    }
}
