# herai-extractinject-unity

A CLI tool to **extract** and **inject** translatable text from Unity games (classic `.assets` serialized files and `.unity3d` asset bundles).

It scans Unity `TextAsset`, `MonoBehaviour`, `GameObject`, `Font`, `LocalizationAsset`, `GUIText` and `TextMesh` assets, flattens them into a simple `assetname / assetclass / id / field / content` model, and lets you write translated text back into the game files.

---

## Features

- Extracts game text from serialized Unity assets (`*.assets`) and asset bundles (`*.unity3d` / UnityFS bundles)
- Scans directories recursively; auto-detects bundle vs serialized files by header
- Works with both **Mono** (managed DLLs) and **IL2CPP** games for `MonoBehaviour` field resolution
- Flat output model: `assetname, assetclass, id, field, content`
- Outputs **JSON** or **CSV**; writes to a file or prints to stdout
- Heuristic content filter (on by default) that discards technical strings: GUIDs, versions, font metadata, Unity reserved keywords, internal identifiers (localization keys, animation names), binary blobs, etc.
- Re-injects translated text back into the original files, including inside asset bundles

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

# Raw extraction without filtering
herai-extractinject-unity extract "/path/to/game" --all --json all-strings.json

# Filter with a higher minimum length
herai-extractinject-unity extract "/path/to/game" --min-length 4 --csv text.csv
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
| `--filter`, `-f` | Enable the content filter (default: on). |
| `--all`, `-a` | Disable the filter and keep every raw string (extract only). |
| `--min-length N` | Minimum string length to keep after filtering (extract only, default `2`). |

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

## Filtering

By default the extractor removes strings that are almost certainly **not** game-facing text:

- GUIDs, UUIDs, hex values, colors, version numbers
- Font names and TextMesh Pro font metadata
- Unity/UI component settings (`m_AnimationTriggers`, `m_HorizontalAxis`, `m_OnClick`, ...)
- Internal identifiers: localization keys, animation/timeline names, `m_*` fields, `k__BackingField`
- File paths, URLs, Unity reserved words (`MonoBehaviour`, `UnityEngine`, ...)
- Binary data (e.g. serialized MIDI) and control characters

Use `--all` if you need the unfiltered dump, or `--min-length N` to tighten/loosen the length threshold.

---

## Supported asset types

- `TextAsset`
- `MonoBehaviour` (requires the game's `Managed` DLLs or IL2CPP globals)
- `GameObject`
- `Font`, `LocalizationAsset`, `GUIText`, `TextMesh`

---

## License

MIT