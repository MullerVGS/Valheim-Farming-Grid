using FarmingGrid.Core;
using Xunit;

namespace FarmingGrid.Tests
{
    public class CustomCropsTests
    {
        [Fact]
        public void Parses_name_and_radius_with_decimal_point()
        {
            var crops = CustomCrops.Parse("RaspberryBush: 0.5, BlueberryBush:0.75", out var rejected);

            Assert.Empty(rejected);
            Assert.Equal(0.5f, crops["RaspberryBush"]);
            Assert.Equal(0.75f, crops["BlueberryBush"]);
        }

        [Fact]
        public void Strips_Clone_suffix_and_accepts_semicolons()
        {
            var crops = CustomCrops.Parse("Pickable_Thistle(Clone): 0.5; CloudberryBush: 0.5", out _);

            Assert.True(crops.ContainsKey("Pickable_Thistle"));
            Assert.True(crops.ContainsKey("CloudberryBush"));
        }

        [Fact]
        public void Rejects_entries_without_radius_or_with_invalid_radius()
        {
            var crops = CustomCrops.Parse("NoRadius, Negative: -1, Text: abc, Good: 1", out var rejected);

            Assert.Single(crops);
            Assert.Equal(3, rejected.Count);
        }

        [Fact]
        public void Empty_text_has_no_crops()
        {
            Assert.Empty(CustomCrops.Parse("  ", out var rejected));
            Assert.Empty(rejected);
        }
    }
}
