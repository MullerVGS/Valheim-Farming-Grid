using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using FarmingGrid.Core;
using UnityEngine;

namespace FarmingGrid
{
    /// <summary>Mod config. Everything is read live: a change through Configuration Manager applies on the next frame.</summary>
    internal sealed class Settings
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<KeyboardShortcut> ToggleKey;
        public readonly ConfigEntry<bool> HoldToPlant;

        public readonly ConfigEntry<SpacingRule> Rule;
        public readonly ConfigEntry<float> ExtraSpacing;
        public readonly ConfigEntry<GridOrientation> Orientation;
        public readonly ConfigEntry<float> FixedAngle;
        public readonly ConfigEntry<float> ReachCells;
        public readonly ConfigEntry<bool> StepKeys;
        public readonly ConfigEntry<int> MaxStep;

        public readonly ConfigEntry<bool> BlockUnhealthy;

        public readonly ConfigEntry<bool> ShowGrid;
        public readonly ConfigEntry<int> GridExtent;
        public readonly ConfigEntry<Color> GridColor;
        public readonly ConfigEntry<bool> ShowGrowRadius;
        public readonly ConfigEntry<Color> FreeColor;
        public readonly ConfigEntry<Color> CrowdedColor;
        public readonly ConfigEntry<float> LineWidth;
        public readonly ConfigEntry<float> HeightOffset;

        public readonly ConfigEntry<string> CustomCropList;

        private readonly ManualLogSource _log;
        private readonly SnapSettings _snap = new SnapSettings();
        private Dictionary<string, float> _customCrops = new Dictionary<string, float>();

        public Settings(ConfigFile config, ManualLogSource log)
        {
            _log = log;

            Enabled = config.Bind("1 - General", "Enabled", true,
                "Snaps saplings to a grid when planting. Hold the game's free placement key (Shift) to plant freely on the spot.");
            ToggleKey = config.Bind("1 - General", "Toggle key", KeyboardShortcut.Empty,
                "Shortcut to toggle \"Enabled\" in game. Empty = no shortcut.");
            HoldToPlant = config.Bind("1 - General", "Hold to keep planting", true,
                "Holding the place button keeps planting saplings at the game's pace, waiting while the spot is invalid. " +
                "Stops when stamina runs out, seeds run out or the button is released. Works with the grid on or off.");

            Rule = config.Bind("2 - Spacing", "Rule", SpacingRule.Exact,
                "Exact: the minimum distance the game itself requires for both plants to grow (grow radius against the neighbour's body).\n" +
                "Wide: twice the largest grow radius. Roomier.");
            ExtraSpacing = config.Bind("2 - Spacing", "Extra spacing (m)", 0.05f,
                new ConfigDescription("Space added to the minimum distance.", new AcceptableValueRange<float>(0f, 2f)));
            Orientation = config.Bind("2 - Spacing", "Orientation", GridOrientation.Auto,
                "Auto: the grid follows the crops already planted. Fixed: uses the angle below.");
            FixedAngle = config.Bind("2 - Spacing", "Fixed angle (degrees)", 0f,
                new ConfigDescription("Grid angle when orientation is Fixed; 0 = aligned to north.", new AcceptableValueRange<float>(0f, 90f)));
            ReachCells = config.Bind("2 - Spacing", "Reach (cells)", 2.5f,
                new ConfigDescription("How many cells away from the nearest crop the sapling is still pulled onto the grid.", new AcceptableValueRange<float>(1f, 6f)));
            StepKeys = config.Bind("2 - Spacing", "Change step with snap keys", true,
                "The game's keys for cycling snap points (Q/E by default, rebindable in the game's controls) change the grid step while planting: " +
                "step 2 plants every other cell, 3 every third. Past the last step it wraps back to 1.");
            MaxStep = config.Bind("2 - Spacing", "Max step", 3,
                new ConfigDescription("Largest grid step the snap keys cycle through.", new AcceptableValueRange<int>(2, 6)));

            BlockUnhealthy = config.Bind("3 - Validation", "Block spots without room", true,
                "Prevents planting where the sapling would not grow: pressed against another crop, with an obstacle in its grow radius or under a roof.");

            ShowGrid = config.Bind("4 - Visual", "Show grid", true, "Draws the grid on the ground while planting near other crops.");
            GridExtent = config.Bind("4 - Visual", "Grid size (cells)", 2,
                new ConfigDescription("Cells drawn on each side of the chosen point.", new AcceptableValueRange<int>(1, 6)));
            GridColor = config.Bind("4 - Visual", "Grid color", new Color(0.85f, 0.85f, 0.85f, 0.35f), "Color and transparency of the lines.");
            ShowGrowRadius = config.Bind("4 - Visual", "Show grow radius", true,
                "Draws the circle the sapling needs clear, green when it fits and red when it does not.");
            FreeColor = config.Bind("4 - Visual", "Clear radius color", new Color(0.45f, 0.95f, 0.45f, 0.6f), "");
            CrowdedColor = config.Bind("4 - Visual", "Blocked radius color", new Color(1f, 0.35f, 0.3f, 0.7f), "");
            LineWidth = config.Bind("4 - Visual", "Line width", 0.015f,
                new ConfigDescription("", new AcceptableValueRange<float>(0.005f, 0.1f)));
            HeightOffset = config.Bind("4 - Visual", "Height above ground (m)", 0.05f,
                new ConfigDescription("", new AcceptableValueRange<float>(0f, 0.5f)));

            CustomCropList = config.Bind("5 - Compatibility", "Extra crops", "",
                "Prefabs that count as crops without a Plant component, each with its grow radius.\n" +
                "Format: Prefab: radius, OtherPrefab: radius. E.g.: RaspberryBush: 0.5, BlueberryBush: 0.5\n" +
                "Saplings with Plant and fully grown harvests are recognized automatically; this is only for mods.");

            CustomCropList.SettingChanged += (_, __) => LoadCustomCrops();
            LoadCustomCrops();
        }

        public IReadOnlyDictionary<string, float> CustomCrops => _customCrops;

        public SnapSettings Snap(int step)
        {
            _snap.Step = step;
            _snap.Rule = Rule.Value;
            _snap.ExtraSpacing = ExtraSpacing.Value;
            _snap.Orientation = Orientation.Value;
            _snap.FixedAngle = FixedAngle.Value;
            _snap.ReachCells = ReachCells.Value;
            return _snap;
        }

        private void LoadCustomCrops()
        {
            _customCrops = Core.CustomCrops.Parse(CustomCropList.Value, out var rejected);
            foreach (string entry in rejected)
                _log.LogWarning($"Ignoring extra crop, expected format \"Prefab: radius\": \"{entry}\"");
        }
    }
}
