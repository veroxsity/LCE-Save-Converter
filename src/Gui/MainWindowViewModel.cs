using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LceWorldConverter.Gui;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private ConversionDirection _selectedDirection = ConversionDirection.JavaToLce;
    private string _javaInputPath = string.Empty;
    private string _javaOutputPath = string.Empty;
    private string _javaWorldType = "classic";
    private bool _javaAllDimensions;
    private bool _javaCopyPlayers;
    private bool _javaPreserveEntities;
    private string _lceInputPath = string.Empty;
    private string _lceOutputPath = string.Empty;
    private string _lceTargetVersion = "1.21.11";
    private bool _lceAllDimensions;
    private bool _lceCopyPlayers;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> WorldTypeKeys => WorldProfiles.Keys;

    public IReadOnlyList<string> TargetVersions => ConversionDefaults.SupportedTargetVersions;

    public ConversionDirection SelectedDirection
    {
        get => _selectedDirection;
        set => SetField(ref _selectedDirection, value);
    }

    public string JavaInputPath
    {
        get => _javaInputPath;
        set => SetField(ref _javaInputPath, value);
    }

    public string JavaOutputPath
    {
        get => _javaOutputPath;
        set => SetField(ref _javaOutputPath, value);
    }

    public string JavaWorldType
    {
        get => _javaWorldType;
        set => SetField(ref _javaWorldType, string.IsNullOrWhiteSpace(value) ? "classic" : value);
    }

    public bool JavaAllDimensions
    {
        get => _javaAllDimensions;
        set => SetField(ref _javaAllDimensions, value);
    }

    public bool JavaCopyPlayers
    {
        get => _javaCopyPlayers;
        set => SetField(ref _javaCopyPlayers, value);
    }

    public bool JavaPreserveEntities
    {
        get => _javaPreserveEntities;
        set => SetField(ref _javaPreserveEntities, value);
    }

    public string LceInputPath
    {
        get => _lceInputPath;
        set => SetField(ref _lceInputPath, value);
    }

    public string LceOutputPath
    {
        get => _lceOutputPath;
        set => SetField(ref _lceOutputPath, value);
    }

    public string LceTargetVersion
    {
        get => _lceTargetVersion;
        set => SetField(ref _lceTargetVersion, ConversionDefaults.NormalizeTargetVersion(value));
    }

    public bool LceAllDimensions
    {
        get => _lceAllDimensions;
        set => SetField(ref _lceAllDimensions, value);
    }

    public bool LceCopyPlayers
    {
        get => _lceCopyPlayers;
        set => SetField(ref _lceCopyPlayers, value);
    }

    public string ReviewSummary => BuildReviewSummary();

    public string ConvertButtonText => SelectedDirection == ConversionDirection.JavaToLce
        ? "Convert To LCE"
        : "Convert To Java";

    public void AutoFillJavaOutput(string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(JavaOutputPath) || string.IsNullOrWhiteSpace(JavaInputPath))
            return;

        JavaOutputPath = ConversionDefaults.GetDefaultOutputDirectory(ConversionDirection.JavaToLce, JavaInputPath, baseDirectory);
    }

    public void AutoFillLceOutput(string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(LceOutputPath) || string.IsNullOrWhiteSpace(LceInputPath))
            return;

        string worldName = Path.GetFileNameWithoutExtension(LceInputPath);
        if (string.IsNullOrWhiteSpace(worldName))
            worldName = "JavaWorld";

        LceOutputPath = Path.Combine(baseDirectory, $"{worldName}-java");
    }

    public bool TryBuildCurrentRequest(out ConversionRequest? request, out string title, out string message)
    {
        request = SelectedDirection == ConversionDirection.JavaToLce
            ? BuildJavaToLceRequest()
            : BuildLceToJavaRequest();

        ConversionRequestValidationResult validation = ConversionRequestValidator.Validate(request);
        if (validation.IsValid)
        {
            title = string.Empty;
            message = string.Empty;
            return true;
        }

        title = MapValidationTitle(validation.FirstError);
        message = validation.FirstError ?? "The current conversion request is invalid.";
        request = null;
        return false;
    }

    private string BuildReviewSummary()
    {
        if (SelectedDirection == ConversionDirection.JavaToLce)
        {
            string dimensions = JavaAllDimensions ? "Convert Overworld, Nether, and End" : "Convert Overworld only";
            string players = JavaCopyPlayers ? "Copy numeric player data" : "Do not copy numeric player data";
            string entities = JavaPreserveEntities ? "Preserve entities and tile data" : "Skip entities and tile data";

            return
                $"Direction: Java -> LCE{Environment.NewLine}" +
                $"World type: {JavaWorldType}{Environment.NewLine}" +
                $"Input: {FormatSummaryPath(JavaInputPath)}{Environment.NewLine}" +
                $"Output folder: {FormatSummaryPath(JavaOutputPath)}{Environment.NewLine}" +
                $"Dimensions: {dimensions}{Environment.NewLine}" +
                $"Players: {players}{Environment.NewLine}" +
                $"Entities: {entities}";
        }

        string dimensionsSummary = LceAllDimensions ? "Export Nether and End" : "Export Overworld only";
        string playersSummary = LceCopyPlayers ? "Export players/*.dat" : "Do not export players";

        return
            $"Direction: LCE -> Java{Environment.NewLine}" +
            $"Target version: {LceTargetVersion}{Environment.NewLine}" +
            $"Input: {FormatSummaryPath(LceInputPath)}{Environment.NewLine}" +
            $"Output folder: {FormatSummaryPath(LceOutputPath)}{Environment.NewLine}" +
            $"Dimensions: {dimensionsSummary}{Environment.NewLine}" +
            $"Players: {playersSummary}";
    }

    private ConversionRequest BuildJavaToLceRequest()
    {
        WorldProfile worldProfile = WorldProfiles.TryParse(JavaWorldType, out WorldProfile parsedProfile)
            ? parsedProfile
            : WorldProfile.Classic;

        return new ConversionRequest
        {
            Direction = ConversionDirection.JavaToLce,
            InputPath = JavaInputPath,
            OutputDirectory = JavaOutputPath,
            WorldProfile = worldProfile,
            ConvertAllDimensions = JavaAllDimensions,
            CopyPlayers = JavaCopyPlayers,
            PreserveEntities = JavaPreserveEntities,
        };
    }

    private ConversionRequest BuildLceToJavaRequest()
    {
        return new ConversionRequest
        {
            Direction = ConversionDirection.LceToJava,
            InputPath = LceInputPath,
            OutputDirectory = LceOutputPath,
            WorldProfile = WorldProfile.Classic,
            TargetVersion = ConversionDefaults.NormalizeTargetVersion(LceTargetVersion),
            ConvertAllDimensions = LceAllDimensions,
            CopyPlayers = LceCopyPlayers,
            PreserveEntities = false,
        };
    }

    private static string FormatSummaryPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? "Not set" : path;
    }

    private static string MapValidationTitle(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "Invalid Request";

        if (error.Contains("Output", StringComparison.OrdinalIgnoreCase))
            return "Missing Output";

        if (error.Contains("Input", StringComparison.OrdinalIgnoreCase))
            return "Invalid Input";

        return "Invalid Options";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(ReviewSummary));
        OnPropertyChanged(nameof(ConvertButtonText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
