using System.Text.RegularExpressions;

public static partial class TextFilter
{
    [GeneratedRegex(@"^[0-9a-fA-F]{32}$")]
    private static partial Regex Guid32();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex GuidDashed();

    [GeneratedRegex(@"^v?\d+(\.\d+)([\.\-\s][0-9a-zA-Z\-]+)*$")]
    private static partial Regex VersionLike();

    [GeneratedRegex(@"^[\d\s.,%°±+\-]+$")]
    private static partial Regex NumericOnly();

    [GeneratedRegex(@"^[0-9a-fA-F]{8,}$")]
    private static partial Regex HexLong();

    [GeneratedRegex(@"^m_[A-Za-z0-9_]+$")]
    private static partial Regex MemberName();

    [GeneratedRegex(@"^<[^>]+>k__BackingField$")]
    private static partial Regex BackingField();

    [GeneratedRegex(@"^[^\p{L}\p{N}]+$")]
    private static partial Regex SymbolOnly();

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]+$")]
    private static partial Regex IdentifierLike();

    [GeneratedRegex(@"^[A-Z][a-zA-Z0-9]*[A-Z][a-zA-Z0-9]*$")]
    private static partial Regex PascalCase();

    [GeneratedRegex(@"\.(asset|assets|prefab|mat|material|png|jpg|jpeg|tga|psd|ttf|otf|shader|cginc|hlsl|cs|dll|fbx|obj|wav|mp3|ogg|txt|json|bytes|unity3d|ab|bundle|resS|resource|meta)$", RegexOptions.IgnoreCase)]
    private static partial Regex AssetExtension();

    [GeneratedRegex(@"(://|^[a-zA-Z]:[\\/]|\.\./|^[\\/])")]
    private static partial Regex UrlHint();

    private static readonly string[] TechnicalKeywords =
    [
        "UnityEngine", "UnityEditor", "UnityEngineInternal", "TMPro", "MonoBehaviour",
        "System.Reflection", "System.Collections", "System.Runtime", "Assembly-",
        "namespace", "using System", "class ", "k__BackingField",
        "PlayerSettings", "EditorBuildSettings", "GraphicsSettings",
        "type=", "$type", "TextMeshPro", "Object.Instantiate", "GetComponent",
        "Resources.Load", "Application.", "Debug.Log", "AssetDatabase", "Coroutine"
    ];

    private static readonly string[] NoisyFieldParts =
    [
        "m_Icon", "m_Version", "m_FaceInfo", "m_CreationSettings", "m_SourceFontFileGUID",
        "m_FallbackFontAssetTable", "m_GlyphTable", "m_CharacterTable", "m_AtlasTextures",
        "m_AtlasMaterial", "m_AtlasWidth", "m_AtlasHeight", "m_Shader", "m_Material",
        "m_IsMultiAtlasTexturesEnabled", "m_ClearDynamicDataOnBuild", "m_FontAtlasSS",
        "m_LineSpacing", "m_AscentLine", "m_DescentLine", "m_CapHeight", "m_Scale",
        "m_Weight", "m_ItalicStyle", "m_Padding", "m_GlobalDynamicFontScale",
        "m_TextureType", "m_Guid", "m_GUID", "m_FileID", "m_FamilyName", "m_StyleName",
        "m_LineHeight", "m_Metrics", "m_FontInfo", "m_DefaultFontAsset", "m_FontNames",
        "m_FontWeights", "m_AnimationTriggers", "m_HorizontalAxis", "m_VerticalAxis",
        "m_SubmitButton", "m_CancelButton", "m_MethodName", "m_OnClick", "m_InputActions",
        "m_Input", "m_ActionEvents", "m_ActionName", "m_ActionId", "m_SelectedClass",
        "m_SelectedScript", "m_SerializerVersion", "m_Behaviour", "m_IsActive",
        "m_SpriteName", "m_AssetGUID", "m_EditorClassIdentifier", "m_Pattern",
        "m_AnimationClip", "m_StateName", "m_Animator", "m_Controller", "m_Template",
        "m_Placeholder", "m_ContentType", "m_LineType", "m_FontAsset", "m_TextComponent",
        "m_MainLight", "m_RenderMode", "m_Camera", "m_EventSystem", "m_MarkupValue",
        "_sortingLayer", "_sequence", "_styleKey", "materialPropertyName",
        "highlightWhileListIncomplete", "phaseStitchHighlightRules", "_trackName",
        "m_Clips", "m_CachedPlayableAsset", "_snapPoints", "m_NameKey",
        "localizationKey", "localizedString", "_localizedKeyName"
    ];

    public static bool IsRelevant(TextEntry entry, int minLength)
    {
        var content = entry.Content?.Trim() ?? "";

        if (content.Length == 0) return false;
        if (content.Length < minLength) return false;
        if (content.Length > 100000) return false;

        if (ContainsBinaryControl(content)) return false;

        if (entry.Id == entry.AssetName && entry.Field == "m_Name") return false;
        if (content == entry.AssetName && entry.Field == "m_Name") return false;

        var fieldPath = entry.Field ?? "";
        foreach (var part in NoisyFieldParts)
        {
            if (fieldPath.Contains(part, StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (content.Length <= 64)
        {
            if (Guid32().IsMatch(content)) return false;
            if (GuidDashed().IsMatch(content)) return false;
            if (VersionLike().IsMatch(content)) return false;
            if (NumericOnly().IsMatch(content)) return false;
            if (HexLong().IsMatch(content)) return false;
            if (SymbolOnly().IsMatch(content)) return false;
        }

        if (AssetExtension().IsMatch(content)) return false;
        if (UrlHint().IsMatch(content)) return false;
        if (content.StartsWith('{') || content.StartsWith('[')) return false;

        if (content.Length <= 80)
        {
            if (MemberName().IsMatch(content)) return false;
            if (BackingField().IsMatch(content)) return false;
        }

        if (content.Length <= 64 && IdentifierLike().IsMatch(content))
        {
            if (content.Contains('_') || content.Contains('.')) return false;
            if (PascalCase().IsMatch(content)) return false;
        }

        foreach (var kw in TechnicalKeywords)
        {
            if (content.Contains(kw, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private static bool ContainsBinaryControl(string content)
    {
        foreach (var c in content)
        {
            if (c < 0x20 && c is not '\t' and not '\n' and not '\r') return true;
        }
        return false;
    }
}