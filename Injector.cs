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
        return JsonSerializer.Deserialize<List<FileText>>(json, JsonOptions) ?? new List<FileText>();
    }

    public static List<FileText> LoadJson(string inputPath)
    {
        var json = File.ReadAllText(inputPath, Encoding.UTF8);
        return ParseJson(json);
    }

    public static int InjectAll(string[] filePaths, List<FileText> files, AssetsManager manager)
    {
        var total = 0;
        foreach (var path in filePaths)
        {
            if (File.Exists(path))
            {
                total += Inject(path, files, manager);
            }
        }
        return total;
    }

    public static int Inject(string filePath, List<FileText> files, AssetsManager manager)
    {
        var afileInst = manager.LoadAssetsFile(filePath, true);
        var afile = afileInst.file;
        manager.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion);

        // Map id -> content
        var values = new Dictionary<string, string>();
        foreach (var file in files)
        {
            foreach (var tf in file.TextFields)
            {
                if (!string.IsNullOrEmpty(tf.Id))
                {
                    values[tf.Id] = tf.Content;
                }
            }
        }

        var modifiedCount = 0;

        foreach (var (key, newContent) in values)
        {
            var parts = key.Split('_', 3);
            if (parts.Length < 3) continue;
            if (!long.TryParse(parts[1], out var pathId)) continue;

            var fieldPath = parts[2];
            var info = afile.Metadata.AssetInfos.FirstOrDefault(i => i.PathId == pathId);
            if (info == null) continue;

            var baseField = manager.GetBaseField(afileInst, info);
            if (baseField == null) continue;

            var targetField = FindFieldByPath(baseField, fieldPath);
            if (targetField == null || targetField.IsDummy) continue;

            if (targetField.TemplateField != null && targetField.TemplateField.Type == "string")
            {
                targetField.AsString = newContent;
                info.SetNewData(baseField);
                modifiedCount++;
            }
        }

        if (modifiedCount > 0)
        {
            var replacePath = Path.Combine(
                Path.GetDirectoryName(filePath)!,
                Path.GetFileNameWithoutExtension(filePath) + "_replace" + Path.GetExtension(filePath)
            );

            File.Move(filePath, replacePath, overwrite: true);

            using var writer = new AssetsFileWriter(filePath);
            afile.Write(writer);

            if (File.Exists(replacePath))
            {
                File.Delete(replacePath);
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
