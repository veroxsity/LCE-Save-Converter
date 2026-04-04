namespace LceWorldConverter;

public sealed class JavaToLceConversionOrchestrator
{
    public ConversionResult Convert(ConversionOptions options, IConversionLogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        return LceWorldConversionService.ConvertJavaToLceCore(options, logger);
    }
}
