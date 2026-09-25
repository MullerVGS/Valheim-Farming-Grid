using FarmingGrid.Core;
using Xunit;

namespace FarmingGrid.Tests
{
    public class HoldToPlantTests
    {
        private static HoldFrame Holding(float lastUse = 0f, bool ready = true, bool valid = true, bool stamina = true, bool sapling = true)
            => new HoldFrame { Held = true, OnSapling = sapling, Ready = ready, SpotValid = valid, HaveStamina = stamina, LastToolUse = lastUse };

        private static HoldToPlant PressedOnSapling()
        {
            var hold = new HoldToPlant();
            var press = Holding();
            press.Pressed = true;
            Assert.Equal(HoldAction.None, hold.Next(press));
            return hold;
        }

        [Fact]
        public void First_press_is_left_to_the_game()
        {
            var hold = new HoldToPlant();
            var press = Holding();
            press.Pressed = true;

            Assert.Equal(HoldAction.None, hold.Next(press));
            Assert.True(hold.Holding);
        }

        [Fact]
        public void Keeps_pressing_every_time_the_game_is_ready()
        {
            var hold = PressedOnSapling();

            Assert.Equal(HoldAction.Place, hold.Next(Holding(lastUse: 1f)));
            Assert.Equal(HoldAction.None, hold.Next(Holding(lastUse: 2f, ready: false)));
            Assert.Equal(HoldAction.Place, hold.Next(Holding(lastUse: 2f)));
        }

        [Fact]
        public void Waits_while_the_spot_is_invalid_and_resumes_on_a_valid_one()
        {
            var hold = PressedOnSapling();

            Assert.Equal(HoldAction.None, hold.Next(Holding(valid: false)));
            Assert.Equal(HoldAction.None, hold.Next(Holding(valid: false)));
            Assert.Equal(HoldAction.Place, hold.Next(Holding()));
        }

        [Fact]
        public void Stops_when_stamina_runs_out_until_released()
        {
            var hold = PressedOnSapling();

            Assert.Equal(HoldAction.OutOfStamina, hold.Next(Holding(stamina: false)));
            Assert.False(hold.Holding);
            Assert.Equal(HoldAction.None, hold.Next(Holding()));

            hold.Next(new HoldFrame());
            var press = Holding();
            press.Pressed = true;
            hold.Next(press);
            Assert.Equal(HoldAction.Place, hold.Next(Holding()));
        }

        [Fact]
        public void Stops_when_the_game_refused_the_press()
        {
            var hold = PressedOnSapling();

            Assert.Equal(HoldAction.Place, hold.Next(Holding(lastUse: 5f)));
            Assert.Equal(HoldAction.None, hold.Next(Holding(lastUse: 5f)));
            Assert.False(hold.Holding);
        }

        [Fact]
        public void Stops_when_released_or_the_ghost_is_no_longer_a_sapling()
        {
            var hold = PressedOnSapling();
            hold.Next(new HoldFrame());
            Assert.False(hold.Holding);

            hold = PressedOnSapling();
            Assert.Equal(HoldAction.None, hold.Next(Holding(sapling: false)));
            Assert.False(hold.Holding);
        }

        [Fact]
        public void Pressing_on_something_else_does_not_start()
        {
            var hold = new HoldToPlant();
            var press = Holding(sapling: false);
            press.Pressed = true;
            hold.Next(press);

            Assert.Equal(HoldAction.None, hold.Next(Holding()));
        }
    }
}
