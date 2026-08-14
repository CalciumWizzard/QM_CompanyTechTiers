# Config fixtures

These are Quasimorph's own config files, captured from the running game. They are
**gitignored on purpose** — they are Magnum Scriptum's assets, not ours.

Needed by the test suite:

- `config_items.txt`
- `config_faction_drops.txt`
- `config_crafting.txt`
- `localization.txt` (~12 MB)

## Regenerating

Set `"DumpConfigsOnLoad": true` in
`%LOCALAPPDATA%\..\LocalLow\Magnum Scriptum Ltd\Quasimorph_ModConfigs\QM_CompanyTechTiers\config.json`,
launch the game to the main menu, quit, then copy the files out of the dump folder
named in `Player.log`.

Captured from game build 1.0.1.566s.7e4da55.

## Reading game internals

A full decompile of `Assembly-CSharp.dll` is the fastest way to answer "what does the
game actually do here". Produce one with:

    dotnet tool install -g ilspycmd --version 9.0.0.7889
    DOTNET_ROLL_FORWARD=Major ilspycmd -p -o <outdir> \
      "C:/Program Files (x86)/Steam/steamapps/common/Quasimorph/Quasimorph_Data/Managed/Assembly-CSharp.dll"

Types worth reading first: `UserModSystem`, `ConfigLoader`, `CustomResources`,
`FactionDropCollection`, `DatadiskRecord`, `Localization`, `DescriptorsCollection`.
