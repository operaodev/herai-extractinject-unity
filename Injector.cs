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

    public static List<FileText> ParseJson(string json)
    {
        return JsonSerializer.Deserialize<List<FileText>>(json, JsonOptions) ?? [];
    }

    public static List<FileText> LoadJson(string inputPath)
    {
        var json = File.ReadAllText(inputPath, Encoding.UTF8);
        return ParseJson(json);
    }

    public static int InjectAll(string[] filePaths, List<FileText> files, AssetsManager manager)
    {
        // Pre-group entries by target file name
        // (fileName -> (PathId -> List<(fieldPath, content, fullKey)>))
        var fileGrouped = new Dictionary<string, Dictionary<long, List<(string fieldPath, string content, string key)>>>(StringComparer.OrdinalIgnoreCase);
        var wildcardMap = new Dictionary<long, List<(string fieldPath, string content, string key)>>();

        foreach (var file in files)
        {
            foreach (var tf in file.TextFields)
            {
                if (string.IsNullOrEmpty(tf.Id)) continue;

                string targetFile = "";
                string idBody = tf.Id;

                if (tf.Id.Contains('#'))
                {
                    var split = tf.Id.Split('#', 2);
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
                    list.Add((fieldPath, tf.Content, tf.Id));
                }
                else
                {
                    if (!wildcardMap.TryGetValue(pathId, out var list))
                    {
                        list = [];
                        wildcardMap[pathId] = list;
                    }
                    list.Add((fieldPath, tf.Content, tf.Id));
                }
            }
        }

        var total = 0;
        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;

            var fileName = Path.GetFileName(path);
            var map = new Dictionary<long, List<(string fieldPath, string content, string key)>>();

            if (fileGrouped.TryGetValue(fileName, out var targetMap))
            {
                foreach (var (pid, list) in targetMap)
                {
                    map[pid] = list;
                }
            }

            // If no specific file mapping, combine with wildcard entries
            if (map.Count == 0 && wildcardMap.Count > 0)
            {
                map = wildcardMap;
            }

            if (map.Count > 0)
            {
                total += Inject(path, map, manager);
            }
        }
        return total;
    }

    public static int Inject(string filePath, Dictionary<long, List<(string fieldPath, string content, string key)>> pathIdMap, AssetsManager manager)
    {
        var afileInst = manager.LoadAssetsFile(filePath, true);
        var afile = afileInst.file;
        manager.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion);

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
