using LceWorldConverter;

namespace LceWorldConverter.Cli;

internal static class CliCommandRouter
{
    public static int Run(string[] args)
    {
        if (InspectorCommandRouter.TryExecute(args, out int inspectorExitCode))
            return inspectorExitCode;

        Console.WriteLine("=== LCE World Converter ===");
        Console.WriteLine("Converts Java Edition worlds <-> Minecraft Legacy Console Edition saveData.ms files.\n");

        if (args.Length < 1)
        {
            foreach (string line in CommandLineOptionsParser.GetUsageLines())
                Console.WriteLine(line);
            return 1;
        }

        if (!CommandLineOptionsParser.TryParse(args, out ConversionRequest? request, out string? error))
        {
            Console.Error.WriteLine($"Error: {error}");
            return 1;
        }

        ConversionRequestValidationResult validation = ConversionRequestValidator.Validate(request!);
        if (!validation.IsValid)
        {
            Console.Error.WriteLine($"Error: {validation.FirstError}");
            return 1;
        }

        try
        {
            var service = new LceWorldConversionService();
            service.Convert(request!, new ConsoleConversionLogger());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Error during conversion: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
