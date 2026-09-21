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
    string? jsonOutputPath = null;
    var inputPaths = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--json" || args[i] == "-j")
        {
            if (i + 1 < args.Length)
            {
                jsonOutputPath = args[i + 1];
                i++;
            }
            else
            {
                throw new ArgumentException("Missing path after --json flag");
            }
        }
        else
        {
            inputPaths.Add(args[i]);
        }
    }

    // Positional compatibility: extract <output.json> <path...>
    if (jsonOutputPath == null && inputPaths.Count >= 2 && inputPaths[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    {
        jsonOutputPath = inputPaths[0];
        inputPaths.RemoveAt(0);
    }

    if (inputPaths.Count == 0)
    {
        throw new ArgumentException("No game directory or .assets paths specified for extraction.");
    }

    var filePaths = ExpandPaths(inputPaths).ToArray();
    if (filePaths.Length == 0)
    {
        throw new FileNotFoundException($"No .assets files found in specified path(s): {string.Join(", ", inputPaths)}");
    }

    var managedPath = FindManagedPath(inputPaths.ToArray());
    var initializer = await UnityToolsInitializer.GetOrCreateAsync(classdataPath, managedPath);
    var manager = initializer.Manager;

    var files = Extractor.ExtractAssets(filePaths, manager);

    if (!string.IsNullOrEmpty(jsonOutputPath))
    {
        Extractor.SaveJson(jsonOutputPath, files);
    }
    else
    {
        var json = Extractor.ToJsonString(files);
        Console.Out.WriteLine(json);
    }
}

static async Task HandleInject(string[] args, string classdataPath)
{
    string? jsonFilePath = null;
    var targetPaths = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--json" || args[i] == "-j")
        {
            if (i + 1 < args.Length)
            {
                jsonFilePath = args[i + 1];
                i++;
            }
            else
            {
                throw new ArgumentException("Missing path after --json flag");
            }
        }
        else
        {
            targetPaths.Add(args[i]);
        }
    }

    List<FileText> files;

    if (!string.IsNullOrEmpty(jsonFilePath))
    {
        if (!File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException($"JSON file not found: {jsonFilePath}");
        }
        files = Injector.LoadJson(jsonFilePath);
    }
    else if (targetPaths.Count >= 2)
    {
        var firstArg = targetPaths[0];
        targetPaths.RemoveAt(0);

        if (firstArg.TrimStart().StartsWith('[') || firstArg.TrimStart().StartsWith('{'))
        {
            files = Injector.ParseJson(firstArg);
        }
        else if (File.Exists(firstArg))
        {
            files = Injector.LoadJson(firstArg);
        }
        else
        {
            files = Injector.ParseJson(firstArg);
        }
    }
    else
    {
        throw new ArgumentException("Usage: herai-unity inject [--json <file.json> | <json_string>] <gameDir/assetsFile>");
    }

    if (targetPaths.Count == 0)
    {
        throw new ArgumentException("No target game directory or .assets paths specified for injection.");
    }

    var filePaths = ExpandPaths(targetPaths).ToArray();
    if (filePaths.Length == 0)
    {
        throw new FileNotFoundException($"No .assets files found in specified target path(s): {string.Join(", ", targetPaths)}");
    }

    var managedPath = FindManagedPath(targetPaths.ToArray());
    var initializer = await UnityToolsInitializer.GetOrCreateAsync(classdataPath, managedPath);
    var manager = initializer.Manager;

    Injector.InjectAll(filePaths, files, manager);
}

static void PrintHelp()
{
    Console.Error.WriteLine("Herai Unity (Extractor / Injector)");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  herai-unity extract <gameDir/assets...> [--json <output.json>]");
    Console.Error.WriteLine("  herai-unity inject  <json_string> <gameDir/assets...>");
    Console.Error.WriteLine("  herai-unity inject  --json <input.json> <gameDir/assets...>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Examples:");
    Console.Error.WriteLine("  herai-unity extract \"/path/to/game\" > text.json");
    Console.Error.WriteLine("  herai-unity extract \"/path/to/game\" --json text.json");
    Console.Error.WriteLine("  herai-unity inject  --json translated.json \"/path/to/game\"");
    Console.Error.WriteLine("  herai-unity inject  \"[{\"name\":...}]\" \"/path/to/game\"");
}

static IEnumerable<string> ExpandPaths(IEnumerable<string> inputPaths)
{
    foreach (var inputPath in inputPaths)
    {
        if (Directory.Exists(inputPath))
        {
            foreach (var file in Directory.GetFiles(inputPath, "*.assets", SearchOption.AllDirectories))
            {
                yield return file;
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
