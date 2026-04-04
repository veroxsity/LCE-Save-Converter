using System.IO.Compression;

namespace LceWorldConverter;

public sealed class PreparedJavaWorld : IDisposable
{
    private readonly string? _temporaryDirectory;

    private PreparedJavaWorld(string originalInputPath, string worldPath, string worldName, string? temporaryDirectory)
    {
        OriginalInputPath = originalInputPath;
        WorldPath = worldPath;
        WorldName = worldName;
        _temporaryDirectory = temporaryDirectory;
    }

    public string OriginalInputPath { get; }

    public string WorldPath { get; }

    public string WorldName { get; }

    public bool IsArchive => _temporaryDirectory != null;

    public static PreparedJavaWorld Open(string inputPath)
    {
        if (Directory.Exists(inputPath))
        {
            string worldName = Path.GetFileName(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(worldName))
                worldName = "World";

            return new PreparedJavaWorld(inputPath, inputPath, worldName, null);
        }

        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input world path was not found: {inputPath}", inputPath);

        if (!string.Equals(Path.GetExtension(inputPath), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Input must be a Java world folder or a .zip archive.");

        string extractionRoot = Path.Combine(Path.GetTempPath(), "LceWorldConverter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionRoot);

        try
        {
            ZipFile.ExtractToDirectory(inputPath, extractionRoot);
            string worldPath = FindWorldRoot(extractionRoot)
                ?? throw new InvalidOperationException("The selected zip does not contain a Java world folder with level.dat.");

            string worldName = Path.GetFileName(worldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(worldName))
                worldName = Path.GetFileNameWithoutExtension(inputPath);

            return new PreparedJavaWorld(inputPath, worldPath, worldName, extractionRoot);
        }
        catch
        {
            try
            {
                if (Directory.Exists(extractionRoot))
                    Directory.Delete(extractionRoot, recursive: true);
            }
            catch
            {
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (_temporaryDirectory == null)
            return;

        try
        {
            if (Directory.Exists(_temporaryDirectory))
                Directory.Delete(_temporaryDirectory, recursive: true);
        }
        catch
        {
        }
    }

    private static string? FindWorldRoot(string extractionRoot)
    {
        var candidates = Directory
            .GetFiles(extractionRoot, "level.dat", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Where(path => !path.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new
            {
                Path = path,
                Score = ScoreWorldDirectory(path),
                Depth = GetRelativeDepth(extractionRoot, path),
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Depth)
            .ToList();

        return candidates.FirstOrDefault()?.Path;
    }

    private static int ScoreWorldDirectory(string path)
    {
        int score = 1;

        if (Directory.Exists(Path.Combine(path, "region")))
            score += 10;

        if (Directory.Exists(Path.Combine(path, "DIM-1", "region")))
            score += 2;

        if (Directory.Exists(Path.Combine(path, "DIM1", "region")))
            score += 2;

        return score;
    }

    private static int GetRelativeDepth(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        if (relative == ".")
            return 0;

        return relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Length;
    }
}