using Xunit;

namespace LceWorldConverter.Tests;

public sealed class ConversionRequestValidatorTests
{
    [Fact]
    public void Validate_JavaRequestAcceptsExistingZipInput()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"request-validator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string zipPath = Path.Combine(tempRoot, "World.zip");
        File.WriteAllBytes(zipPath, []);

        try
        {
            var request = new ConversionRequest
            {
                Direction = ConversionDirection.JavaToLce,
                InputPath = zipPath,
                OutputDirectory = Path.Combine(tempRoot, "out"),
                WorldProfile = WorldProfile.FlatMedium,
            };

            ConversionRequestValidationResult result = ConversionRequestValidator.Validate(request);
            Assert.True(result.IsValid, result.FirstError);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Validate_LceRequestRejectsPreserveEntities()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"request-validator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string savePath = Path.Combine(tempRoot, "saveData.ms");
        File.WriteAllBytes(savePath, [0, 0, 0, 0]);

        try
        {
            var request = new ConversionRequest
            {
                Direction = ConversionDirection.LceToJava,
                InputPath = savePath,
                OutputDirectory = Path.Combine(tempRoot, "out"),
                WorldProfile = WorldProfile.Classic,
                PreserveEntities = true,
            };

            ConversionRequestValidationResult result = ConversionRequestValidator.Validate(request);
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("--preserve-entities", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
