using System;

namespace FarmingGrid.Core
{
    /// <summary>The numbers the game uses to spend and regenerate the player's stamina, read on the frame they are needed.</summary>
    public struct StaminaModel
    {
        public float Max;
        /// <summary>Base regeneration per second (<c>Player.m_staminaRegen</c>).</summary>
        public float Regen;
        /// <summary>Extra regeneration the emptier the bar is (<c>Player.m_staminaRegenTimeMultiplier</c>).</summary>
        public float LowBonus;
        /// <summary>Everything else multiplying regeneration: status effects and the world's stamina regen modifier.</summary>
        public float Scale;
        /// <summary>Seconds after spending stamina before it starts regenerating (<c>Player.m_staminaRegenDelay</c>).</summary>
        public float RegenDelay;
        /// <summary>Seconds between placements (<c>Player.m_placeDelay</c>).</summary>
        public float PlaceDelay;
        /// <summary>Stamina a placement actually spends.</summary>
        public float Cost;
        /// <summary>Stamina the game requires to be above before it lets you place.</summary>
        public float Required;
    }

    /// <summary>
    /// When to resume planting after running out of stamina so that the most saplings go in per second.
    /// <para>
    /// The game regenerates <c>Regen × (1 + LowBonus × (1 − stamina/Max)) × Scale</c> per second, and only once
    /// <c>RegenDelay</c> has passed without spending any. Placing every <c>PlaceDelay</c> (0.4 s) keeps resetting that
    /// delay, so nothing comes back while planting: the bar only refills in the pauses. Planting down to empty is always
    /// right (starting stamina is free and an emptier bar refills faster), but every pause pays the regen delay once.
    /// Short pauses waste it over and over; long ones spend time refilling the slow, nearly full part of the bar. The
    /// best pause sits in between and is found here by trying every batch size.
    /// </para>
    /// </summary>
    public static class StaminaPacing
    {
        /// <summary>How far above the game's threshold a batch starts, so the last placement of it is not refused.</summary>
        public const float Margin = 0.05f;

        /// <summary>Stamina to wait for before planting again. <c>Max</c> when regeneration is stalled or a single sapling is out of reach.</summary>
        public static float ResumeAt(in StaminaModel m)
        {
            if (m.Cost <= 0f || RatePerSecond(m, m.Max) <= 0f)
                return m.Max;

            float best = m.Max;
            float bestRate = 0f;
            for (int n = 1; ; n++)
            {
                float start = m.Required + Margin + (n - 1) * m.Cost;
                if (start > m.Max)
                    break;
                float end = Math.Max(0f, start - n * m.Cost);
                float cycle = (n - 1) * m.PlaceDelay + m.RegenDelay + SecondsToRegen(m, end, start);
                float rate = n / cycle;
                if (rate > bestRate)
                {
                    bestRate = rate;
                    best = start;
                }
            }
            return best;
        }

        /// <summary>Saplings per second when resting until <paramref name="resumeAt"/> and planting down to empty, over and over.</summary>
        public static float SaplingsPerSecond(in StaminaModel m, float resumeAt)
        {
            int n = (int)Math.Ceiling((resumeAt - m.Required) / m.Cost);
            if (n < 1)
                return 0f;
            float end = Math.Max(0f, resumeAt - n * m.Cost);
            return n / ((n - 1) * m.PlaceDelay + m.RegenDelay + SecondsToRegen(m, end, resumeAt));
        }

        /// <summary>Seconds of regeneration (after the delay) to go from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static float SecondsToRegen(in StaminaModel m, float from, float to)
        {
            if (to <= from)
                return 0f;
            float k = m.Regen * m.Scale;
            if (k <= 0f)
                return float.PositiveInfinity;
            if (m.LowBonus == 0f || m.Max <= 0f)
                return (to - from) / k;
            // ds/dt = k (1 + L) − (k L / Max) s
            float a = k * (1f + m.LowBonus);
            float b = k * m.LowBonus / m.Max;
            return (float)(Math.Log((a - b * from) / (a - b * to)) / b);
        }

        private static float RatePerSecond(in StaminaModel m, float stamina)
            => m.Regen * m.Scale * (1f + m.LowBonus * (1f - stamina / m.Max));
    }
}
