using System;
using FarmingGrid.Core;
using HarmonyLib;
using UnityEngine;

namespace FarmingGrid.Game
{
    /// <summary>
    /// Holding the place button keeps planting. <c>Player.UpdatePlacement</c> places when the button was pressed less than
    /// 0.2 s ago and <c>m_placeDelay</c> has passed since the last tool use; re-pressing it right when the game is ready
    /// leaves stamina, seeds, skill and durability to the game, exactly as a click. Out of stamina it rests until
    /// <see cref="StaminaPacing"/> says resuming plants the most saplings per second, then carries on.
    /// </summary>
    internal sealed class HoldPlanting
    {
        private static readonly AccessTools.FieldRef<Player, float> PlacePressedTime =
            AccessTools.FieldRefAccess<Player, float>("m_placePressedTime");
        private static readonly AccessTools.FieldRef<Player, float> LastToolUseTime =
            AccessTools.FieldRefAccess<Player, float>("m_lastToolUseTime");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> RightItem =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem");
        private static readonly Func<Player, float> BuildStamina =
            AccessTools.MethodDelegate<Func<Player, float>>(AccessTools.Method(typeof(Player), "GetBuildStamina"));

        private readonly Settings _settings;
        private readonly PlantingAssist _assist;
        private readonly HoldToPlant _hold = new HoldToPlant();

        public HoldPlanting(Settings settings, PlantingAssist assist)
        {
            _settings = settings;
            _assist = assist;
        }

        public void BeforePlacement(Player player, bool takeInput)
        {
            if (!_settings.HoldToPlant.Value || !takeInput || !player.InPlaceMode() || Hud.IsPieceSelectionVisible()
                || ZInput.GetButton("JoyAltKeys"))
            {
                _hold.Stop();
                return;
            }

            float lastUse = LastToolUseTime(player);
            float required = RightItem(player)?.m_shared.m_attack.m_attackStamina ?? 0f;
            var frame = new HoldFrame
            {
                Pressed = (ZInput.GetButtonDown("Attack") || ZInput.GetButtonDown("JoyPlace")) && !Hud.InRadial(),
                Held = ZInput.GetButton("Attack") || ZInput.GetButton("JoyPlace"),
                OnSapling = _assist.OnSapling,
                Ready = Time.time - lastUse > player.m_placeDelay,
                SpotValid = _assist.SpotValid,
                HaveStamina = player.HaveStamina(required),
                LastToolUse = lastUse,
            };
            float resumeAt = 0f;
            if (_hold.Resting || !frame.HaveStamina)
            {
                resumeAt = StaminaPacing.ResumeAt(Model(player, required));
                float stamina = player.GetStamina();
                frame.Rested = stamina >= resumeAt || stamina >= player.GetMaxStamina() - 0.01f;
            }

            switch (_hold.Next(frame))
            {
                case HoldAction.Place:
                    PlacePressedTime(player) = Time.time;
                    break;
                case HoldAction.Rest:
                    player.Message(MessageHud.MessageType.TopLeft, $"Catching breath, planting again at {Mathf.CeilToInt(resumeAt)} stamina");
                    break;
                case HoldAction.OutOfStamina:
                    Hud.instance?.StaminaBarEmptyFlash();
                    break;
            }
        }

        private static StaminaModel Model(Player player, float required)
        {
            float scale = 1f;
            player.GetSEMan().ModifyStaminaRegen(ref scale);
            return new StaminaModel
            {
                Max = player.GetMaxStamina(),
                Regen = player.m_staminaRegen,
                LowBonus = player.m_staminaRegenTimeMultiplier,
                Scale = scale * global::Game.m_staminaRegenRate,
                RegenDelay = player.m_staminaRegenDelay,
                PlaceDelay = player.m_placeDelay,
                Cost = BuildStamina(player) * global::Game.m_staminaRate,
                Required = required,
            };
        }
    }
}
