namespace LceWorldConverter;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== LCE World Converter ===");
        Console.WriteLine("Converts Java Edition worlds to Minecraft Legacy Console Edition (WIN64) format.\n");

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: LceWorldConverter <java_world_path> <output_savedata.ms> [--large-world]");
            Console.WriteLine();
            Console.WriteLine("  java_world_path   Path to Java Edition world folder (containing level.dat)");
            Console.WriteLine("  output_savedata.ms Path for the output LCE save file");
            Console.WriteLine("  --large-world      Use 320-chunk (5120 block) world size instead of 54-chunk (864 block)");
            return;
        }

        string javaWorldPath = args[0];
        string outputPath = args[1];
        bool largeWorld = args.Length > 2 && args[2] == "--large-world";

        if (!Directory.Exists(javaWorldPath))
        {
            Console.Error.WriteLine($"Error: Java world directory not found: {javaWorldPath}");
            return;
        }

        string levelDatPath = Path.Combine(javaWorldPath, "level.dat");
        if (!File.Exists(levelDatPath))
        {
            Console.Error.WriteLine($"Error: level.dat not found in: {javaWorldPath}");
            return;
        }

        Console.WriteLine($"Source:      {javaWorldPath}");
        Console.WriteLine($"Output:      {outputPath}");
        Console.WriteLine($"World size:  {(largeWorld ? "Large (320 chunks)" : "Legacy (54 chunks)")}");
        Console.WriteLine();

        // TODO: Conversion pipeline
        Console.WriteLine("[placeholder] Conversion pipeline not yet implemented.");
    }
}
