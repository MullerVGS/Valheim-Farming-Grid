using System;
using FarmingGrid.Core;
using Xunit;

namespace FarmingGrid.Tests
{
    public class StaminaPacingTests
    {
        // Player prefab defaults, a mid-game bar and a cultivator at 0 farming.
        private static StaminaModel Model(float max = 200f, float cost = 5f, float lowBonus = 1f, float scale = 1f)
            => new StaminaModel
            {
                Max = max, Regen = 5f, LowBonus = lowBonus, Scale = scale,
                RegenDelay = 1f, PlaceDelay = 0.4f, Cost = cost, Required = 5f,
            };

        [Fact]
        public void Regen_time_matches_stepping_the_games_formula()
        {
            var m = Model();
            float s = 10f, t = 0f;
            const float dt = 0.001f;
            while (s < 150f)
            {
                s += m.Regen * m.Scale * (1f + m.LowBonus * (1f - s / m.Max)) * dt;
                t += dt;
            }

            Assert.Equal(t, StaminaPacing.SecondsToRegen(m, 10f, 150f), 2);
        }

        [Fact]
        public void Without_the_low_bonus_regen_is_linear()
            => Assert.Equal(10f, StaminaPacing.SecondsToRegen(Model(lowBonus: 0f), 0f, 50f), 3);

        [Fact]
        public void The_chosen_point_beats_every_other_one()
        {
            var m = Model();
            float chosen = StaminaPacing.ResumeAt(m);
            float best = StaminaPacing.SaplingsPerSecond(m, chosen);

            for (int n = 1; m.Required + StaminaPacing.Margin + (n - 1) * m.Cost <= m.Max; n++)
                Assert.True(StaminaPacing.SaplingsPerSecond(m, m.Required + StaminaPacing.Margin + (n - 1) * m.Cost) <= best + 1e-6f);
        }

        [Fact]
        public void Resting_to_the_chosen_point_beats_refilling_the_bar_and_planting_one_by_one()
        {
            var m = Model();
            float chosen = StaminaPacing.ResumeAt(m);

            Assert.True(chosen > m.Required + m.Cost && chosen < m.Max * 0.75f);
            Assert.True(StaminaPacing.SaplingsPerSecond(m, chosen) > StaminaPacing.SaplingsPerSecond(m, m.Max));
            Assert.True(StaminaPacing.SaplingsPerSecond(m, chosen) > StaminaPacing.SaplingsPerSecond(m, m.Required + StaminaPacing.Margin));
        }

        [Fact]
        public void Every_batch_starts_above_the_games_threshold()
        {
            var m = Model(cost: 2.5f);
            float chosen = StaminaPacing.ResumeAt(m);

            Assert.True((chosen - m.Required - StaminaPacing.Margin) % m.Cost < 1e-3f);
        }

        [Fact]
        public void Stalled_regen_or_a_bar_too_small_waits_for_a_full_bar()
        {
            Assert.Equal(200f, StaminaPacing.ResumeAt(Model(scale: 0f)));
            Assert.Equal(4f, StaminaPacing.ResumeAt(Model(max: 4f)));
        }
    }
}
