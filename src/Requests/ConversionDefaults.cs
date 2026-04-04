namespace LceWorldConverter;

public static class ConversionDefaults
{
    private static readonly string[] _supportedTargetVersions =
    [
        "1.12.2",
        "1.13.2",
        "1.14.4",
        "1.15.2",
        "1.16.5",
        "1.17.1",
        "1.18.2",
        "1.19.4",
        "1.20.4",
        "1.21.4",
        "1.21.11",
    ];

    public const string DefaultTargetVersion = "1.12.2";

    public static IReadOnlyList<string> SupportedTargetVersions => _supportedTargetVersions;

    public static string NormalizeTargetVersion(string? targetVersion)
    {
        return string.IsNullOrWhiteSpace(targetVersion)
            ? DefaultTargetVersion
            : targetVersion.Trim();
    }

    public static string GetDefaultOutputDirectory(ConversionDirection direction, string inputPath, string currentDirectory)
    {
        return direction switch
        {
            ConversionDirection.JavaToLce => Path.Combine(currentDirectory, GetSafeWorldName(inputPath, "ConvertedWorld")),
            ConversionDirection.LceToJava => Path.Combine(currentDirectory, GetSafeWorldName(inputPath, "RecoveredJavaWorld")),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unsupported conversion direction."),
        };
    }

    private static string GetSafeWorldName(string inputPath, string fallback)
    {
        string trimmed = inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileNameWithoutExtension(trimmed);
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileName(trimmed);

        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }
}
