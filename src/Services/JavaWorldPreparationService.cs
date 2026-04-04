namespace LceWorldConverter;

public sealed class JavaWorldPreparationService
{
    public PreparedJavaWorld Open(string inputPath)
    {
        return PreparedJavaWorld.Open(inputPath);
    }
}
