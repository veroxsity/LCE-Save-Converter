namespace LceWorldConverter;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--inspect")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LceWorldConverter --inspect <saveData.ms_path>");
                return 1;
            }

            SaveDataInspector.Inspect(args[1]);
            return 0;
        }

        Console.WriteLine("=== LCE World Converter ===");
        Console.WriteLine("Converts Java Edition Minecraft worlds into Minecraft Legacy Console Edition saveData.ms files.\n");

        if (args.Length < 1)
        {
            foreach (string line in CommandLineOptionsParser.GetUsageLines())
                Console.WriteLine(line);
            return 1;
        }

        if (!CommandLineOptionsParser.TryParse(args, out ConversionOptions? options, out string? error))
        {
            Console.Error.WriteLine($"Error: {error}");
            return 1;
        }

        try
        {
            var service = new LceWorldConversionService();
            service.Convert(options!, new ConsoleConversionLogger());
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
