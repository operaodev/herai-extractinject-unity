using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Cpp2IL;

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
var classdataPath = Path.Combine(baseDir, "classdata.tpk");
if (!File.Exists(classdataPath) && File.Exists("classdata.tpk"))
{
    classdataPath = Path.GetFullPath("classdata.tpk");
}

if (args.Length == 0)
{
    PrintHelp();
    return;
}

var command = args[0].ToLower();
var subArgs = args[1..];

try
{
    switch (command)
    {
        case "extract":
        case "extractor":
            await HandleExtract(subArgs, classdataPath);
            break;

        case "inject":
        case "injector":
            await HandleInject(subArgs, classdataPath);
            break;

        default:
            PrintHelp();
            Environment.Exit(1);
            break;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[Error] {ex.Message}");
    Environment.Exit(1);
}

static async Task HandleExtract(string[] args, string classdataPath)
{
    string? outputPath = null;
    string outputFormat = "json";
    var inputPaths = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--json" || args[i] == "-j")
        {
            outputFormat = "json";
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                outputPath = args[i + 1];
                i++;
            }
        }
        else if (args[i] == "--csv" || args[i] == "-c")
        {
            outputFormat = "csv";
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                outputPath = args[i + 1];
                i++;
            }
        }
        else
        {
            inputPaths.Add(args[i]);
        }
    }

    // Positional compatibility: extract <gameDir/assets...> <output.json/.csv>
    if (outputPath == null)
    {
        for (int i = inputPaths.Count - 1; i >= 0; i--)
        {
            var candidate = inputPaths[i];
            if (candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                candidate.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(candidate))
                {
                    outputPath = candidate;
                    outputFormat = candidate.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "csv" : "json";
                    inputPaths.RemoveAt(i);
                    break;
                }
            }
        }
    }

    if (inputPaths.Count == 0)
    {
        throw new ArgumentException("No game directory or Unity asset paths specified for extraction.");
    }

    var filePaths = ExpandPaths(inputPaths).ToArray();
    if (filePaths.Length == 0)
    {
        throw new FileNotFoundException($"No Unity asset files found in specified path(s): {string.Join(", ", inputPaths)}");
    }

    var managedPath = FindManagedPath(inputPaths.ToArray());
    var initializer = await UnityToolsInitializer.GetOrCreateAsync(classdataPath, managedPath);
    var manager = initializer.Manager;

    var entries = Extractor.ExtractAssets(filePaths, manager);
    Console.Error.WriteLine($"[Extractor] {entries.Count} strings extracted.");

    if (!string.IsNullOrEmpty(outputPath))
    {
        if (outputFormat == "csv")
            Extractor.SaveCsv(outputPath, entries);
        else
            Extractor.SaveJson(outputPath, entries);
    }
    else
    {
        if (outputFormat == "csv")
            Console.Out.Write(Extractor.ToCsvString(entries));
        else
            Console.Out.Write(Extractor.ToJsonString(entries));
    }
}

static async Task HandleInject(string[] args, string classdataPath)
{
    string? inputFilePath = null;
    string inputFormat = "json";
    var targetPaths = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--json" || args[i] == "-j")
        {
            inputFormat = "json";
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                inputFilePath = args[i + 1];
                i++;
            }
        }
        else if (args[i] == "--csv" || args[i] == "-c")
        {
            inputFormat = "csv";
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                inputFilePath = args[i + 1];
                i++;
            }
        }
        else
        {
            targetPaths.Add(args[i]);
        }
    }

    List<TextEntry> entries;

    if (!string.IsNullOrEmpty(inputFilePath))
    {
        if (!File.Exists(inputFilePath))
        {
            throw new FileNotFoundException($"Input file not found: {inputFilePath}");
        }
        entries = inputFormat == "csv"
            ? Injector.LoadCsv(inputFilePath)
            : Injector.LoadJson(inputFilePath);
    }
    else if (Console.IsInputRedirected)
    {
        var input = await Console.In.ReadToEndAsync();
        entries = inputFormat == "csv"
            ? Injector.ParseCsv(input)
            : Injector.ParseJson(input);
    }
    else if (targetPaths.Count >= 2)
    {
        var firstArg = targetPaths[0];
        targetPaths.RemoveAt(0);

        if (firstArg.TrimStart().StartsWith('[') || firstArg.TrimStart().StartsWith('{'))
        {
            entries = Injector.ParseJson(firstArg);
        }
        else if (File.Exists(firstArg))
        {
            if (firstArg.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                entries = Injector.LoadCsv(firstArg);
            else
                entries = Injector.LoadJson(firstArg);
        }
        else
        {
            entries = Injector.ParseJson(firstArg);
        }
    }
    else
    {
        throw new ArgumentException("Usage: herai-extractinject-unity inject [--json|--csv [<file>]] <gameDir/assetsFile>");
    }

    if (targetPaths.Count == 0)
    {
        throw new ArgumentException("No target game directory or .assets paths specified for injection.");
    }

    var filePaths = ExpandPaths(targetPaths).ToArray();
    if (filePaths.Length == 0)
    {
        throw new FileNotFoundException($"No Unity asset files found in specified target path(s): {string.Join(", ", targetPaths)}");
    }

    var managedPath = FindManagedPath(targetPaths.ToArray());
    var initializer = await UnityToolsInitializer.GetOrCreateAsync(classdataPath, managedPath);
    var manager = initializer.Manager;

    Injector.InjectAll(filePaths, entries, manager);
}

static void PrintHelp()
{
    Console.Error.WriteLine("Herai Unity (Extractor / Injector)");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  herai-extractinject-unity extract <gameDir/assets...> [--json|--csv [<output>]]");
    Console.Error.WriteLine("  herai-extractinject-unity inject  <gameDir/assets...>                (reads from stdin)");
    Console.Error.WriteLine("  herai-extractinject-unity inject  <input> <gameDir/assets...>");
    Console.Error.WriteLine("  herai-extractinject-unity inject  --json|--csv <input> <gameDir/assets...>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Options:");
    Console.Error.WriteLine("  --json, -j           Output/input format JSON (default)");
    Console.Error.WriteLine("  --csv, -c            Output/input format CSV");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Examples:");
    Console.Error.WriteLine("  herai-extractinject-unity extract \"/path/to/game\" > text.json");
    Console.Error.WriteLine("  herai-extractinject-unity extract \"/path/to/game\" --json output.json");
    Console.Error.WriteLine("  herai-extractinject-unity extract \"/path/to/game\" --csv output.csv");
    Console.Error.WriteLine("  herai-extractinject-unity extract \"/path/to/game\" \"output.json\"");
    Console.Error.WriteLine("  cat text.json | herai-extractinject-unity inject \"/path/to/game\"");
    Console.Error.WriteLine("  herai-extractinject-unity inject --csv translated.csv \"/path/to/game\"");
}

static IEnumerable<string> ExpandPaths(IEnumerable<string> inputPaths)
{
    foreach (var inputPath in inputPaths)
    {
        if (Directory.Exists(inputPath))
        {
            foreach (var file in Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories))
            {
                if (Extractor.IsUnityFile(file))
                {
                    yield return file;
                }
            }
        }
        else if (File.Exists(inputPath))
        {
            yield return inputPath;
        }
        else
        {
            Console.Error.WriteLine($"[Path not found] {inputPath}");
        }
    }
}

static string? FindManagedPath(string[] paths)
{
    foreach (var path in paths)
    {
        var fullPath = Path.GetFullPath(path);
        var dir = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        while (dir != null)
        {
            var managedPath = Path.Combine(dir, "Managed");
            if (Directory.Exists(managedPath))
            {
                return managedPath;
            }

            var dataDir = Directory.GetDirectories(dir, "*_Data", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (dataDir != null)
            {
                var dataManagedPath = Path.Combine(dataDir, "Managed");
                if (Directory.Exists(dataManagedPath))
                {
                    return dataManagedPath;
                }
            }

            dir = Path.GetDirectoryName(dir);
        }
    }

    return null;
}

class UnityToolsInitializer
{
    private static UnityToolsInitializer? _instance;

    public AssetsManager Manager { get; private set; } = null!;
    public string? ManagedPath { get; private set; }

    public static async Task<UnityToolsInitializer> GetOrCreateAsync(string classdataPath, string? managedPath)
    {
        if (_instance != null)
        {
            return _instance;
        }

        var initializer = new UnityToolsInitializer();
        await initializer.InitializeAsync(classdataPath, managedPath);
        _instance = initializer;
        return initializer;
    }

    private async Task InitializeAsync(string classdataPath, string? managedPath)
    {
        Manager = new AssetsManager();
        Manager.LoadClassPackage(classdataPath);

        ManagedPath = managedPath;
        if (!string.IsNullOrEmpty(managedPath))
        {
            SetupMonoBehaviourSupport(managedPath);
        }
    }

    private void SetupMonoBehaviourSupport(string managedPath)
    {
        var il2cppFiles = FindCpp2IlFiles.Find(managedPath);
        if (il2cppFiles.success)
        {
            Manager.MonoTempGenerator = new Cpp2IlTempGenerator(il2cppFiles.metaPath, il2cppFiles.asmPath);
            Console.Error.WriteLine("[Initializer] Using IL2CPP support");
            return;
        }

        if (Directory.Exists(managedPath) && Directory.GetFiles(managedPath, "*.dll").Length > 0)
        {
            Manager.MonoTempGenerator = new MonoCecilTempGenerator(managedPath);
            Console.Error.WriteLine("[Initializer] Using Mono support");
        }
    }
}
