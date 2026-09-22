using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Text;
using System.Text.Json;

public static class Injector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static List<TextEntry> ParseJson(string json)
    {
        return JsonSerializer.Deserialize<List<TextEntry>>(json, JsonOptions) ?? [];
    }

    public static List<TextEntry> LoadJson(string inputPath)
    {
        var json = File.ReadAllText(inputPath, Encoding.UTF8);
        return ParseJson(json);
    }

    public static List<TextEntry> ParseCsv(string csv)
    {
        var entries = new List<TextEntry>();
        using var reader = new StringReader(csv);
        string? line;
        bool first = true;

        while ((line = reader.ReadLine()) != null)
        {
            if (first)
            {
                first = false;
                if (line.StartsWith("assetname"))
                    continue;
            }

            var fields = ParseCsvLine(line);
            if (fields.Count < 5) continue;

            entries.Add(new()
            {
                AssetName = fields[0],
                AssetClass = fields[1],
                Id = fields[2],
                Field = fields[3],
                Content = fields[4]
            });
        }
        return entries;
    }

    public static List<TextEntry> LoadCsv(string inputPath)
    {
        var csv = File.ReadAllText(inputPath, Encoding.UTF8);
        return ParseCsv(csv);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        int i = 0;
        while (i <= line.Length)
        {
            if (i == line.Length)
            {
                fields.Add("");
                break;
            }

            if (line[i] == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            i++;
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[i]);
                        i++;
                    }
                }
                fields.Add(sb.ToString());
                if (i < line.Length && line[i] == ',') i++;
            }
            else
            {
                var nextComma = line.IndexOf(',', i);
                if (nextComma == -1)
                {
                    fields.Add(line[i..]);
                    break;
                }
                else
                {
                    fields.Add(line[i..nextComma]);
                    i = nextComma + 1;
                }
            }
        }
        return fields;
    }

    public static int InjectAll(string[] filePaths, List<TextEntry> entries, AssetsManager manager)
    {
        // Pre-group entries by target asset file name (the source used in ids)
        // (assetFileName -> (PathId -> List<(fieldPath, content, fullKey)>))
        var fileGrouped = new Dictionary<string, Dictionary<long, List<(string fieldPath, string content, string key)>>>(StringComparer.OrdinalIgnoreCase);
        var wildcardMap = new Dictionary<long, List<(string fieldPath, string content, string key)>>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Id)) continue;

            string targetFile = "";
            string idBody = entry.Id;

            if (entry.Id.Contains('#'))
            {
                var split = entry.Id.Split('#', 2);
                targetFile = split[0];
                idBody = split[1];
            }

            var parts = idBody.Split('_', 3);
            if (parts.Length < 3) continue;
            if (!long.TryParse(parts[1], out var pathId)) continue;

            var fieldPath = parts[2];

            if (!string.IsNullOrEmpty(targetFile))
            {
                if (!fileGrouped.TryGetValue(targetFile, out var targetMap))
                {
                    targetMap = [];
                    fileGrouped[targetFile] = targetMap;
                }
                if (!targetMap.TryGetValue(pathId, out var list))
                {
                    list = [];
                    targetMap[pathId] = list;
                }
                list.Add((fieldPath, entry.Content, entry.Id));
            }
            else
            {
                if (!wildcardMap.TryGetValue(pathId, out var list))
                {
                    list = [];
                    wildcardMap[pathId] = list;
                }
                list.Add((fieldPath, entry.Content, entry.Id));
            }
        }

        var total = 0;
        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;

            try
            {
                if (AssetsFile.IsAssetsFile(path))
                {
                    var map = BuildMap(fileGrouped, wildcardMap, Path.GetFileName(path));
                    if (map.Count > 0)
                    {
                        total += InjectAssetsFile(path, map, manager);
                    }
                }
                else if (Extractor.IsUnityFile(path))
                {
                    total += InjectBundle(path, fileGrouped, wildcardMap, manager);
                }
                else
                {
                    Console.Error.WriteLine($"[Injector] Skipping non-Unity file: {path}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Injector] Error in {path}: {ex.Message}");
            }
        }
        return total;
    }

    private static Dictionary<long, List<(string fieldPath, string content, string key)>> BuildMap(
        Dictionary<string, Dictionary<long, List<(string fieldPath, string content, string key)>>> grouped,
        Dictionary<long, List<(string fieldPath, string content, string key)>> wildcard,
        string assetFileName)
    {
        if (grouped.TryGetValue(assetFileName, out var targetMap))
        {
            return targetMap;
        }
        return wildcard.Count > 0 ? wildcard : [];
    }

    public static int InjectAssetsFile(string filePath, Dictionary<long, List<(string fieldPath, string content, string key)>> pathIdMap, AssetsManager manager)
    {
        var afileInst = manager.LoadAssetsFile(filePath, true);
        var afile = afileInst.file;
        manager.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion);

        var modifiedCount = ApplyUpdates(afileInst, pathIdMap, manager);

        if (modifiedCount > 0)
        {
            var tempPath = filePath + ".tmp";

            using (var writer = new AssetsFileWriter(tempPath))
            {
                afile.Write(writer);
            }

            manager.UnloadAssetsFile(afileInst);
            File.Move(tempPath, filePath, overwrite: true);
        }
        else
        {
            manager.UnloadAssetsFile(afileInst);
        }

        return modifiedCount;
    }

    private static int InjectBundle(
        string filePath,
        Dictionary<string, Dictionary<long, List<(string fieldPath, string content, string key)>>> fileGrouped,
        Dictionary<long, List<(string fieldPath, string content, string key)>> wildcardMap,
        AssetsManager manager)
    {
        var bunInst = manager.LoadBundleFile(filePath);
        var bun = bunInst.file;
        var modifiedCount = 0;

        try
        {
            var names = bun.GetAllFileNames();
            for (int i = 0; i < names.Count; i++)
            {
                if (!bun.IsAssetsFile(i)) continue;

                var afileInst = manager.LoadAssetsFileFromBundle(bunInst, i);
                if (afileInst == null) continue;

                var map = BuildMap(fileGrouped, wildcardMap, afileInst.name);
                if (map.Count == 0) continue;

                manager.LoadClassDatabaseFromPackage(afileInst.file.Metadata.UnityVersion);
                modifiedCount += ApplyUpdates(afileInst, map, manager);
            }

            if (modifiedCount > 0)
            {
                var dirAtEnd = (bun.Header.FileStreamHeader.Flags & AssetBundleFSHeaderFlags.BlockAndDirAtEnd) != 0;
                var tempPath = filePath + ".tmp";

                using (var writer = new AssetsFileWriter(tempPath))
                {
                    bun.Pack(writer, bunInst.originalCompression, dirAtEnd);
                }

                File.Move(tempPath, filePath, overwrite: true);
            }
        }
        finally
        {
            manager.UnloadBundleFile(bunInst);
        }

        return modifiedCount;
    }

    private static int ApplyUpdates(AssetsFileInstance afileInst, Dictionary<long, List<(string fieldPath, string content, string key)>> pathIdMap, AssetsManager manager)
    {
        var afile = afileInst.file;

        var assetInfoMap = new Dictionary<long, AssetFileInfo>();
        foreach (var info in afile.Metadata.AssetInfos)
        {
            assetInfoMap[info.PathId] = info;
        }

        var modifiedCount = 0;

        foreach (var (pathId, updates) in pathIdMap)
        {
            if (!assetInfoMap.TryGetValue(pathId, out var info)) continue;

            AssetTypeValueField? baseField = null;
            try
            {
                baseField = manager.GetBaseField(afileInst, info);
            }
            catch
            {
                continue;
            }

            if (baseField == null) continue;

            var assetModified = false;
            foreach (var (fieldPath, newContent, key) in updates)
            {
                try
                {
                    var targetField = FindFieldByPath(baseField, fieldPath);
                    if (targetField == null || targetField.IsDummy) continue;

                    if (targetField.TemplateField != null && targetField.TemplateField.Type == "string")
                    {
                        targetField.AsString = newContent;
                        assetModified = true;
                        modifiedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Injector] Warning updating field {key}: {ex.Message}");
                }
            }

            if (assetModified)
            {
                info.SetNewData(baseField);
            }
        }

        return modifiedCount;
    }

    private static AssetTypeValueField? FindFieldByPath(AssetTypeValueField baseField, string fieldPath)
    {
        var parts = fieldPath.Split('.');
        var current = baseField;

        foreach (var part in parts)
        {
            if (current == null || current.IsDummy) return null;
            if (part == "Array") continue;

            if (int.TryParse(part, out var index))
            {
                if (current["Array"] != null && !current["Array"].IsDummy)
                {
                    current = current["Array"][index];
                }
                else if (current.Children != null && index >= 0 && index < current.Children.Count)
                {
                    current = current[index];
                }
                else
                {
                    return null;
                }
            }
            else
            {
                var child = current[part];
                if (child != null && !child.IsDummy)
                {
                    current = child;
                }
                else
                {
                    return null;
                }
            }
        }

        return current;
    }
}
