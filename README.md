# herai-extractinject-unity

A CLI tool to **extract** and **inject** translatable text from Unity games (classic `.assets` serialized files and `.unity3d` asset bundles).

It scans Unity `TextAsset`, `MonoBehaviour`, `GameObject`, `Font`, `LocalizationAsset`, `GUIText` and `TextMesh` assets, flattens them into a simple `assetname / assetclass / id / field / content` model, and lets you write translated text back into the game files.

---

## Download

Pre-built releases (self-contained, no .NET required) are published on the [Releases](../../releases) page as GitHub tags `v*`:

| Platform | Asset |
|----------|-------|
| Linux (x64) | `herai-extractinject-unity-linux-x64.tar.gz` |
| Windows (x64) | `herai-extractinject-unity-win-x64.zip` |

```bash
# Linux
tar -xzf herai-extractinject-unity-linux-x64.tar.gz
./herai-extractinject-unity --help

# Windows (PowerShell)
Expand-Archive herai-extractinject-unity-win-x64.zip -DestinationPath herai-extractinject-unity
.\herai-extractinject-unity.exe --help
```

Release notes are generated automatically from the commits between tags.

---

## Requirements

- .NET 8 SDK or newer runtime (`RollForward` is enabled)
- `classdata.tpk` (Unity class database). The build automatically downloads it from the UABEA release if it is not present next to the project.

---

## Build

```bash
dotnet build -c Release
```

The executable is produced at:

- `bin/Release/net8.0/herai-extractinject-unity`

---

## Usage

```
herai-extractinject-unity extract <gameDir|assets...> [--json|--csv [<output>]] [options]
herai-extractinject-unity inject  <gameDir|assets...>                (reads JSON/CSV from stdin)
herai-extractinject-unity inject  <input.json|csv> <gameDir|assets...>
```

### Extract

```bash
# Extract to stdout (JSON)
herai-extractinject-unity extract "/path/to/game" > text.json

# Extract to a JSON file
herai-extractinject-unity extract "/path/to/game" --json text.json

# Extract to a CSV file
herai-extractinject-unity extract "/path/to/game" --csv text.csv

# Positional output file also works
herai-extractinject-unity extract "/path/to/game" text.json
```

### Inject

```bash
# From a JSON file (matches by the source prefix in each id)
herai-extractinject-unity inject --json translated.json "/path/to/game"

# From a CSV file
herai-extractinject-unity inject --csv translated.csv "/path/to/game"

# Piping from stdin
cat translated.json | herai-extractinject-unity inject "/path/to/game"

# Explicit format flag
herai-extractinject-unity inject --json translated.json "/path/to/game"
```

> **Note:** injection rewrites the `.assets` / `.unity3d` files in place. Make a backup of your game first.

---

## Options

| Flag | Meaning |
|------|---------|
| `--json`, `-j` | Output/input format is JSON (default). If followed by a non-flag path, writes/reads that file. |
| `--csv`, `-c` | Output/input format is CSV. Same rule for the following path. |

---

## Data model

Each extracted entry is a flat record:

| Field | Description |
|-------|-------------|
| `assetname` | Asset display name (`m_Name`), or `file#pathId` when unnamed |
| `assetclass` | Asset class, e.g. `TextAsset`, `MonoBehaviour`, `GameObject`, or the numeric Unity type id |
| `id` | Unique key that ties a record to a specific string field. Format: `sourceFile#typeId_pathId_fieldPath` |
| `field` | Serialized field path (dotted, e.g. `m_text`, `Array.0.content`) |
| `content` | The string value |

### id format

```
<source>#<typeId>_<pathId>_<dotted.field.path>
```

- `<source>` is the name of the asset file that contains the string (`level0`, `sharedassets0.assets`, ...). For bundles it is the inner file name.
- `<typeId>` is the Unity class id of the asset.
- `<pathId>` is the asset's path id inside its file.
- The rest is the dotted field path within the serialized object.

The injector parses the id, groups records by `<source>`, and rewrites the matching string fields. Edits in a translation file should keep the `id` unchanged.

### JSON example

```json
[
  {
    "assetname": "level0#140",
    "assetclass": "114",
    "id": "level0#114_140_m_text",
    "field": "m_text",
    "content": "Who knows what they will come up with next."
  }
]
```

### CSV example

```csv
assetname,assetclass,id,field,content
level0#140,114,level0#114_140_m_text,m_text,Who knows what they will come up with next.
```

---

## Extraction

Every string found in the supported assets is extracted as-is, with no filtering: field names, GUIDs, metadata, dialogue and everything else is kept so nothing is lost. Filter the results afterwards with a separate tool if needed.

---

## Supported asset types

- `TextAsset`
- `MonoBehaviour` (requires the game's `Managed` DLLs or IL2CPP globals)
- `GameObject`
- `Font`, `LocalizationAsset`, `GUIText`, `TextMesh`

---

## License

MIT