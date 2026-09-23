using System;
using BepInEx;
using BepInEx.Logging;
using FarmingGrid.Game;
using HarmonyLib;
using UnityEngine;

namespace FarmingGrid
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.duenas.valheim.farminggrid";
        public const string Name = "Farming Grid";
        public const string Version = "1.1.0";

        internal static ManualLogSource Log;

        private static Plugin _instance;
        private Harmony _harmony;
        private Settings _settings;
        private PlantingAssist _assist;
        private bool _failed;

        private void Awake()
        {
            _instance = this;
            Log = Logger;
            _settings = new Settings(Config, Logger);
            _assist = new PlantingAssist(_settings);
            _settings.Enabled.SettingChanged += (_, __) => _assist.Hide();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(PlacementPatch));
        }

        private void Update()
        {
            if (TextInputFocused())
                return;

            if (_settings.ToggleKey.Value.IsDown())
            {
                _settings.Enabled.Value = !_settings.Enabled.Value;
                Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft,
                    _settings.Enabled.Value ? "Farming grid enabled" : "Farming grid disabled");
            }

            int delta = _settings.StepKeys.Value && _assist.Showing ? SnapKeyDelta() : 0;
            if (delta != 0)
            {
                int step = _assist.CycleStep(delta);
                Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft,
                    step == 1 ? "Grid step: every cell" : $"Grid step: every {step} cells");
            }
        }

        /// <summary>The game's snap point keys, read under the same conditions <c>Player.UpdatePlacementGhost</c> reads them.</summary>
        private static int SnapKeyDelta()
        {
            if (ZInput.GetButton("JoyAltKeys") || Hud.IsPieceSelectionVisible()
                || (Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large))
                return 0;
            if (ZInput.GetButtonDown("TabLeft") || (ZInput.GetButtonUp("JoyPrevSnap") && ZInput.GetButtonLastPressedTimer("JoyPrevSnap") < 0.33f))
                return -1;
            if (ZInput.GetButtonDown("TabRight") || (ZInput.GetButtonUp("JoyNextSnap") && ZInput.GetButtonLastPressedTimer("JoyNextSnap") < 0.33f))
                return 1;
            return 0;
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _assist?.Destroy();
            _instance = null;
        }

        private static bool TextInputFocused()
            => Console.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus()) || TextInput.IsVisible();

        /// <summary>
        /// An exception here would escape the player's LateUpdate every frame. If one happens,
        /// the mod logs it once, hides the grid and gets out of the way: the game carries on with normal placement.
        /// </summary>
        internal static void OnGhostUpdated(Player player, GameObject ghost, ref Player.PlacementStatus status, bool flashGuardStone)
        {
            Plugin self = _instance;
            if (self == null || self._failed || player != Player.m_localPlayer)
                return;
            try
            {
                self._assist.AfterGhostUpdate(player, ghost, ref status, flashGuardStone);
            }
            catch (Exception e)
            {
                self._failed = true;
                Log.LogError($"Farming Grid disabled for this session after an unexpected error:\n{e}");
                try { self._assist.Hide(); } catch { /* already getting out of the way */ }
            }
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class PlacementPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, bool flashGuardStone, GameObject ___m_placementGhost, ref Player.PlacementStatus ___m_placementStatus)
            => Plugin.OnGhostUpdated(__instance, ___m_placementGhost, ref ___m_placementStatus, flashGuardStone);
    }
}
