using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Text;
using System.Text.Json;

public static class Extractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static List<TextEntry> ExtractAssets(string[] filePaths, AssetsManager manager)
    {
        var result = new List<TextEntry>();

        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath)) continue;

            if (!IsUnityFile(filePath))
            {
                Console.Error.WriteLine($"[Extractor] Skipping non-Unity file: {filePath}");
                continue;
            }

            try
            {
                if (AssetsFile.IsAssetsFile(filePath))
                {
                    ExtractFromFile(filePath, manager, result);
                }
                else
                {
                    ExtractFromBundle(filePath, manager, result);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Extractor] Error in {filePath}: {ex.Message}");
            }
        }

        return result;
    }

    public static bool IsUnityFile(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 8) return false;

            Span<byte> buf = stackalloc byte[8];
            int read = fs.Read(buf);
            if (read < 8) return false;

            var head = buf[..read];
            if (head[0] == 1 && head[1] == 0 && head[2] == 0 && head[3] == 0) return true;
            return head.StartsWith("Unity"u8) || head.StartsWith("Raw"u8);
        }
        catch
        {
            return false;
        }
    }

    private static void ExtractFromFile(string filePath, AssetsManager manager, List<TextEntry> result)
    {
        var afileInst = manager.LoadAssetsFile(filePath, true);
        try
        {
            ExtractFromAssetsFile(afileInst, Path.GetFileName(filePath), manager, result);
        }
        finally
        {
            manager.UnloadAssetsFile(afileInst);
        }
    }

    private static void ExtractFromBundle(string filePath, AssetsManager manager, List<TextEntry> result)
    {
        var bunInst = manager.LoadBundleFile(filePath);
        try
        {
            var bun = bunInst.file;
            var names = bun.GetAllFileNames();
            for (int i = 0; i < names.Count; i++)
            {
                var innerName = names[i];
                if (!bun.IsAssetsFile(i)) continue;

                AssetsFileInstance? afileInst = null;
                try
                {
                    afileInst = manager.LoadAssetsFileFromBundle(bunInst, i);
                    if (afileInst == null) continue;

                    ExtractFromAssetsFile(afileInst, afileInst.name, manager, result);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Extractor] Error in {filePath} -> {innerName}: {ex}");
                }
                finally
                {
                    if (afileInst != null)
                    {
                        manager.UnloadAssetsFile(afileInst);
                    }
                }
            }
        }
        finally
        {
            manager.UnloadBundleFile(bunInst);
        }
    }

    private static void ExtractFromAssetsFile(AssetsFileInstance afileInst, string source, AssetsManager manager, List<TextEntry> result)
    {
        var afile = afileInst.file;
        manager.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion);

        // TextAsset
        foreach (var info in afile.GetAssetsOfType(AssetClassID.TextAsset))
        {
            var baseField = SafeGetBaseField(afileInst, info, manager);
            if (baseField == null) continue;

            var scriptField = baseField["m_Script"];
            if (scriptField != null && !scriptField.IsDummy && !string.IsNullOrEmpty(scriptField.AsString))
            {
                result.Add(new()
                {
                    AssetName = GetAssetName(baseField, source, info.PathId),
                    AssetClass = "TextAsset",
                    Id = $"{source}#{info.TypeId}_{info.PathId}_m_Script",
                    Field = "m_Script",
                    Content = scriptField.AsString
                });
            }
        }

        // MonoBehaviour
        foreach (var info in afile.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var baseField = SafeGetBaseField(afileInst, info, manager);
            if (baseField == null) continue;

            var assetName = GetAssetName(baseField, source, info.PathId);
            ExtractStrings(baseField, info, result, source, assetName);
        }

        // GameObject
        foreach (var info in afile.GetAssetsOfType(AssetClassID.GameObject))
        {
            var baseField = SafeGetBaseField(afileInst, info, manager);
            if (baseField == null) continue;

            var nameField = baseField["m_Name"];
            if (nameField != null && !nameField.IsDummy && !string.IsNullOrEmpty(nameField.AsString))
            {
                result.Add(new()
                {
                    AssetName = nameField.AsString,
                    AssetClass = "GameObject",
                    Id = $"{source}#{info.TypeId}_{info.PathId}_m_Name",
                    Field = "m_Name",
                    Content = nameField.AsString
                });
            }
        }

        // Other string assets (Font, LocalizationAsset, GUIText, TextMesh)
        AssetClassID[] otherTypes = [AssetClassID.Font, AssetClassID.LocalizationAsset, AssetClassID.GUIText, AssetClassID.TextMesh];
        foreach (var typeId in otherTypes)
        {
            foreach (var info in afile.GetAssetsOfType(typeId))
            {
                var baseField = SafeGetBaseField(afileInst, info, manager);
                if (baseField == null) continue;

                var assetName = GetAssetName(baseField, source, info.PathId);
                ExtractStrings(baseField, info, result, source, assetName);
            }
        }
    }

    public static List<TextEntry> ApplyFilter(List<TextEntry> entries, int minLength)
    {
        return entries.Where(e => TextFilter.IsRelevant(e, minLength)).ToList();
    }

    public static string ToJsonString(List<TextEntry> entries)
    {
        return JsonSerializer.Serialize(entries, JsonOptions);
    }

    public static void SaveJson(string outputPath, List<TextEntry> entries)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = ToJsonString(entries);
        File.WriteAllText(outputPath, json, Encoding.UTF8);
    }

    public static string ToCsvString(List<TextEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("assetname,assetclass,id,field,content");
        foreach (var e in entries)
        {
            sb.AppendLine($"{EscapeCsv(e.AssetName)},{EscapeCsv(e.AssetClass)},{EscapeCsv(e.Id)},{EscapeCsv(e.Field)},{EscapeCsv(e.Content)}");
        }
        return sb.ToString();
    }

    public static void SaveCsv(string outputPath, List<TextEntry> entries)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var csv = ToCsvString(entries);
        File.WriteAllText(outputPath, csv, Encoding.UTF8);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private static AssetTypeValueField? SafeGetBaseField(AssetsFileInstance inst, AssetFileInfo info, AssetsManager manager)
    {
        try
        {
            return manager.GetBaseField(inst, info);
        }
        catch
        {
            return null;
        }
    }

    private static void ExtractStrings(AssetTypeValueField field, AssetFileInfo info, List<TextEntry> result, string source, string assetName, string? prefix = null)
    {
        if (field == null || field.IsDummy) return;

        if (field.TemplateField?.Type == "string" && !string.IsNullOrEmpty(field.AsString))
        {
            var fieldPath = prefix ?? "value";
            result.Add(new()
            {
                AssetName = assetName,
                AssetClass = info.TypeId.ToString(),
                Id = $"{source}#{info.TypeId}_{info.PathId}_{fieldPath}",
                Field = fieldPath,
                Content = field.AsString
            });
            return;
        }

        if (field.Children is { Count: > 0 })
        {
            var isArray = (field.TemplateField != null && field.TemplateField.IsArray) || field.TemplateField?.Type == "Array";
            var dataIndex = 0;

            for (int i = 0; i < field.Children.Count; i++)
            {
                var child = field.Children[i];
                if (child == null || child.IsDummy) continue;
                if (child.FieldName == "size" && child.TemplateField?.Type == "int") continue;

                string childPrefix;
                if (string.IsNullOrEmpty(prefix))
                {
                    childPrefix = child.FieldName;
                }
                else if (isArray || field.FieldName == "Array")
                {
                    childPrefix = $"{prefix}.{dataIndex}";
                    dataIndex++;
                }
                else
                {
                    childPrefix = $"{prefix}.{child.FieldName}";
                }

                ExtractStrings(child, info, result, source, assetName, childPrefix);
            }
        }
    }

    private static string GetAssetName(AssetTypeValueField baseField, string source, long pathId)
    {
        var nameField = baseField["m_Name"];
        if (nameField != null && !nameField.IsDummy && !string.IsNullOrEmpty(nameField.AsString))
        {
            return nameField.AsString;
        }
        return $"{source}#{pathId}";
    }
}