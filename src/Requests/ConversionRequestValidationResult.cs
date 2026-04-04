namespace LceWorldConverter;

public sealed class ConversionRequestValidationResult
{
    private static readonly ConversionRequestValidationResult _valid = new([]);

    public ConversionRequestValidationResult(IReadOnlyList<string> errors)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public string? FirstError => IsValid ? null : Errors[0];

    public static ConversionRequestValidationResult Valid => _valid;
}
