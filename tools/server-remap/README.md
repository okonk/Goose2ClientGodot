# server-remap

Transforms `Aspereta Goose Data.xlsx` to reference the converted Illutia-format
assets, and merges Xendria quest/class/NPC content. See
`docs/plans/2026-07-24-aspereta-server-remap.md`.

## Workbook remap

```bash
python3 -m remap.main   # from this directory
```

## Aspereta → Illutia map files (server)

Illutia server mode loads Goose2-format maps from `Data/Illutia/Maps/`. The remapped
workbook expects `Map10001.map` … (Aspereta `MapN` renumbered by +10000). Convert
and install:

```bash
python3 remap_maps.py
# defaults:
#   --src /home/hayden/code/illutiagooseserver/Goose/Data/Aspereta/Maps
#   --dst /home/hayden/code/illutiagooseserver/Goose/Data/Illutia/Maps
#   --mapping ../../tools/AssetConverter/data/aspereta-mapping.tsv
```

## Tests

```bash
python3 -m pytest tests/ -v
```
