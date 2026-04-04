namespace LceWorldConverter;

public static class ConversionRequestValidator
{
    public static ConversionRequestValidationResult Validate(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        ValidateCommon(request, errors);

        switch (request.Direction)
        {
            case ConversionDirection.JavaToLce:
                ValidateJavaToLce(request, errors);
                break;
            case ConversionDirection.LceToJava:
                ValidateLceToJava(request, errors);
                break;
            default:
                errors.Add($"Unsupported conversion direction: {request.Direction}");
                break;
        }

        return errors.Count == 0
            ? ConversionRequestValidationResult.Valid
            : new ConversionRequestValidationResult(errors);
    }

    private static void ValidateCommon(ConversionRequest request, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
            errors.Add("Input path is required.");

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
            errors.Add("Output directory is required.");

        if (!string.IsNullOrWhiteSpace(request.OutputDirectory) && File.Exists(request.OutputDirectory))
            errors.Add("Output directory points to a file, not a folder.");
    }

    private static void ValidateJavaToLce(ConversionRequest request, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
            return;

        bool isFolder = Directory.Exists(request.InputPath);
        bool isZip = File.Exists(request.InputPath)
            && string.Equals(Path.GetExtension(request.InputPath), ".zip", StringComparison.OrdinalIgnoreCase);

        if (!isFolder && !isZip)
            errors.Add("Java input must be an existing world folder or .zip archive.");
    }

    private static void ValidateLceToJava(ConversionRequest request, List<string> errors)
    {
        if (request.PreserveEntities)
            errors.Add("--preserve-entities is only valid with Java->LCE conversion.");

        if (!File.Exists(request.InputPath))
            errors.Add("LCE input must be an existing saveData.ms file.");

        if (string.IsNullOrWhiteSpace(ConversionDefaults.NormalizeTargetVersion(request.TargetVersion)))
            errors.Add("Target version is required.");
    }
}
