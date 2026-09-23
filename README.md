# Farming Grid

Client-side [BepInEx](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) mod for **Valheim 1.0**
that snaps saplings to a grid when planting with the cultivator.

- The first sapling goes where you aim, the second orbits around it, and from the third on the grid follows your field.
- Snaps to the free grid point closest to your aim.
- Draws the grid on the ground and a grow-radius circle: green when the sapling will grow, red when it won't.
- Optionally blocks spots where the sapling would not grow (too close to another crop, an obstacle or a roof).
- Hold **Shift** (the game's free placement key) to plant without snapping.
- Press **Q/E** (the game's snap point keys, so your own bindings apply) to widen the grid step: every cell, every 2nd,
  every 3rd, then back to every cell.

Only needed on your own client.

## Installation

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
2. Copy `FarmingGrid.dll` to `Valheim/BepInEx/plugins/FarmingGrid/`.

## Configuration

Settings live in `BepInEx/config/dev.duenas.valheim.farminggrid.cfg` (created on first launch) and can be changed in game
with [Configuration Manager](https://thunderstore.io/c/valheim/p/Azumatt/Official_BepInEx_ConfigurationManager/).

| Option | Default | Description |
| --- | --- | --- |
| Enabled | `true` | Turns snapping on |
| Toggle key | empty | Shortcut to toggle snapping in game |
| Rule | `Exact` | `Exact` = the minimum distance the game requires; `Wide` = twice the largest grow radius |
| Extra spacing (m) | `0.05` | Space added to the minimum distance |
| Orientation | `Auto` | `Auto` follows the field; `Fixed` uses the fixed angle |
| Fixed angle (degrees) | `0` | Grid angle for `Fixed`; 0 = north |
| Reach (cells) | `2.5` | How far from the field the sapling is still snapped |
| Change step with snap keys | `true` | Q/E (or your rebinds) cycle the grid step while planting |
| Max step | `3` | Largest step before it wraps back to every cell |
| Block spots without room | `true` | Prevents planting where the sapling would not grow |
| Extra crops | empty | `Prefab: radius, ...` for modded plantables without a `Plant` component |

The **Visual** section controls the grid and circle appearance.

## Building

Game and BepInEx assemblies are referenced from your Valheim install:

```sh
dotnet build src -c Release -p:ValheimDir="<path to Valheim>"
dotnet test tests
```

## License

MIT.
