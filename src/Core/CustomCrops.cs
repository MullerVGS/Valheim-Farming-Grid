using System;
using System.Collections.Generic;
using System.Globalization;

namespace FarmingGrid.Core
{
    /// <summary>
    /// Parses the extra crops list from the config: <c>Prefab: radius, OtherPrefab: radius</c>.
    /// Meant for things planted without a <c>Plant</c> component (bushes from mods like PlantEverything).
    /// </summary>
    public static class CustomCrops
    {
        private static readonly char[] EntrySeparators = { ',', ';', '\n' };

        public static Dictionary<string, float> Parse(string text, out List<string> rejected)
        {
            var crops = new Dictionary<string, float>(StringComparer.Ordinal);
            rejected = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return crops;

            foreach (string raw in text.Split(EntrySeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = raw.Trim();
                if (entry.Length == 0)
                    continue;

                int colon = entry.LastIndexOf(':');
                string name = colon > 0 ? StripClone(entry.Substring(0, colon).Trim()) : null;
                if (name == null || name.Length == 0
                    || !float.TryParse(entry.Substring(colon + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float radius)
                    || radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius))
                {
                    rejected.Add(entry);
                    continue;
                }
                crops[name] = radius;
            }
            return crops;
        }

        /// <summary>The game names instances <c>Prefab(Clone)</c>; the config uses the prefab name.</summary>
        private static string StripClone(string name)
        {
            const string suffix = "(Clone)";
            return name.EndsWith(suffix, StringComparison.Ordinal) ? name.Substring(0, name.Length - suffix.Length).Trim() : name;
        }
    }
}
