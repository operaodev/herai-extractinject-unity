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

    public static List<FileText> ExtractAssets(string[] filePaths, AssetsManager manager)
    {
        var result = new List<FileText>();

        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath)) continue;

            AssetsFileInstance? afileInst = null;
            try
            {
                afileInst = manager.LoadAssetsFile(filePath, true);
                var afile = afileInst.file;
                manager.LoadClassDatabaseFromPackage(afile.Metadata.UnityVersion);

                var source = Path.GetFileName(filePath);

                // TextAsset
                foreach (var info in afile.GetAssetsOfType(AssetClassID.TextAsset))
                {
                    var baseField = manager.GetBaseField(afileInst, info);
                    if (baseField == null) continue;

                    var scriptField = baseField["m_Script"];
                    if (scriptField != null && !scriptField.IsDummy && !string.IsNullOrEmpty(scriptField.AsString))
                    {
                        result.Add(new()
                        {
                            Name = GetAssetName(baseField, source, info.PathId),
                            Class = "TextAsset",
                            TextFields =
                            [
                                new()
                                {
                                    Id = $"{source}#{info.TypeId}_{info.PathId}_m_Script",
                                    Field = "m_Script",
                                    Content = scriptField.AsString
                                }
                            ]
                        });
                    }
                }

                // MonoBehaviour
                foreach (var info in afile.GetAssetsOfType(AssetClassID.MonoBehaviour))
                {
                    var baseField = manager.GetBaseField(afileInst, info);
                    if (baseField == null) continue;

                    var fields = new List<TextField>();
                    ExtractStrings(baseField, info, fields, source);

                    if (fields.Count > 0)
                    {
                        result.Add(new()
                        {
                            Name = GetAssetName(baseField, source, info.PathId),
                            Class = "MonoBehaviour",
                            TextFields = fields
                        });
                    }
                }

                // GameObject
                foreach (var info in afile.GetAssetsOfType(AssetClassID.GameObject))
                {
                    var baseField = manager.GetBaseField(afileInst, info);
                    if (baseField == null) continue;

                    var nameField = baseField["m_Name"];
                    if (nameField != null && !nameField.IsDummy && !string.IsNullOrEmpty(nameField.AsString))
                    {
                        result.Add(new()
                        {
                            Name = nameField.AsString,
                            Class = "GameObject",
                            TextFields =
                            [
                                new()
                                {
                                    Id = $"{source}#{info.TypeId}_{info.PathId}_m_Name",
                                    Field = "m_Name",
                                    Content = nameField.AsString
                                }
                            ]
                        });
                    }
                }

                // Other string assets (Font, LocalizationAsset, GUIText, TextMesh)
                AssetClassID[] otherTypes = [AssetClassID.Font, AssetClassID.LocalizationAsset, AssetClassID.GUIText, AssetClassID.TextMesh];
                foreach (var typeId in otherTypes)
                {
                    foreach (var info in afile.GetAssetsOfType(typeId))
                    {
                        var baseField = manager.GetBaseField(afileInst, info);
                        if (baseField == null) continue;

                        var fields = new List<TextField>();
                        ExtractStrings(baseField, info, fields, source);

                        if (fields.Count > 0)
                        {
                            result.Add(new()
                            {
                                Name = GetAssetName(baseField, source, info.PathId),
                                Class = typeId.ToString(),
                                TextFields = fields
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Extractor] Error in {filePath}: {ex.Message}");
            }
            finally
            {
                if (afileInst != null)
                {
                    manager.UnloadAssetsFile(afileInst);
                }
            }
        }

        return result;
    }

    public static string ToJsonString(List<FileText> files)
    {
        return JsonSerializer.Serialize(files, JsonOptions);
    }

    public static void SaveJson(string outputPath, List<FileText> files)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = ToJsonString(files);
        File.WriteAllText(outputPath, json, Encoding.UTF8);
    }

    private static void ExtractStrings(AssetTypeValueField field, AssetFileInfo info, List<TextField> fields, string source, string? prefix = null)
    {
        if (field == null || field.IsDummy) return;

        if (field.TemplateField?.Type == "string" && !string.IsNullOrEmpty(field.AsString))
        {
            var fieldPath = prefix ?? "value";
            fields.Add(new()
            {
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

                ExtractStrings(child, info, fields, source, childPrefix);
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
