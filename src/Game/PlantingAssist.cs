using System;
using System.Collections.Generic;
using FarmingGrid.Core;
using HarmonyLib;
using UnityEngine;

namespace FarmingGrid.Game
{
    /// <summary>
    /// Runs after the game positions the build ghost: if it is a sapling, pulls it onto the grid,
    /// redoes the game's position-dependent checks and flags the spot as lacking room when the sapling would not grow.
    /// Since <c>Player.TryPlacePiece</c> only reads the status set here, placement obeys without another patch.
    /// </summary>
    internal sealed class PlantingAssist
    {
        /// <summary>Rescan only if the ghost moves this far or the interval below elapses.</summary>
        private const float RescanDistance = 0.05f;
        private const float RescanInterval = 0.2f;

        private static readonly Func<Player, bool> BlockedByPlayers =
            AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "CheckPlacementGhostVSPlayers"));

        private readonly Settings _settings;
        private readonly CropCatalog _catalog;
        private readonly FieldScanner _scanner;
        private readonly GridRenderer _renderer;
        private readonly List<Crop> _nearby = new List<Crop>();

        /// <summary>Height difference still attributable to the ghost's pivot; beyond it the aim was resting on something else.</summary>
        private const float MaxPivotLift = 0.15f;

        private readonly HashSet<string> _described = new HashSet<string>();
        private string _lastReason;
        private string _scannedPrefab;
        private Vector3 _scannedAt;
        private float _scannedTime;
        private int _step = 1;
        private int _shownFrame = -10;

        public PlantingAssist(Settings settings)
        {
            _settings = settings;
            _catalog = new CropCatalog(settings);
            _scanner = new FieldScanner(_catalog);
            _renderer = new GridRenderer(settings);
            if (BlockedByPlayers == null)
                Plugin.Log.LogWarning("Player.CheckPlacementGhostVSPlayers does not exist in this version; skipping the player-on-sapling check.");
        }

        /// <summary>A sapling ghost was on the grid last frame, so the snap keys belong to us.</summary>
        public bool Showing => Time.frameCount - _shownFrame <= 1;

        /// <summary>Moves the grid step by <paramref name="delta"/>, wrapping around at both ends.</summary>
        public int CycleStep(int delta)
        {
            int max = _settings.MaxStep.Value;
            _step = ((Math.Min(_step, max) - 1 + delta) % max + max) % max + 1;
            return _step;
        }

        public void Hide() => _renderer.Hide();

        public void Destroy() => _renderer.Destroy();

        public void AfterGhostUpdate(Player player, GameObject ghost, ref Player.PlacementStatus status, bool flashGuardStone)
        {
            if (!_settings.Enabled.Value || ghost == null || !ghost.activeSelf || status == Player.PlacementStatus.NoRayHits
                || !_catalog.TryGetGhost(ghost, out Crop ghostCrop))
            {
                _renderer.Hide();
                return;
            }

            _shownFrame = Time.frameCount;
            var piece = ghost.GetComponent<Piece>();
            Vector3 raw = ghost.transform.position;
            float grow = ghostCrop.GrowRadius;
            SnapSettings snap = _settings.Snap(Math.Min(_step, _settings.MaxStep.Value));

            IReadOnlyList<Crop> nearby = Nearby(ghost.name, raw, ghostCrop, snap);
            SnapResult result = FreePlacementHeld(player)
                ? GridSolver.Free(ghostCrop, nearby, snap)
                : GridSolver.Solve(ghostCrop, nearby, snap);

            Vector3 final = raw;
            if (result.Snapped)
            {
                // Keep only the pivot offset; if the aim was resting on a rock, the sapling drops to the ground.
                float lift = raw.y - Ground(raw);
                if (Mathf.Abs(lift) > MaxPivotLift)
                    lift = 0f;
                final = new Vector3(result.Position.X, 0f, result.Position.Z);
                final.y = Ground(final) + lift;
                ghost.transform.position = final;
                Revalidate(player, piece, final, ref status, flashGuardStone);
            }

            bool crowded = result.Crowded;
            string reason = null;
            if (_settings.BlockUnhealthy.Value && status == Player.PlacementStatus.Valid)
            {
                if (crowded)
                {
                    reason = CrowdedBy(ghostCrop.At(result.Position), snap);
                    status = Player.PlacementStatus.MoreSpace;
                }
                else if (_scanner.HasObstacle(final, grow, ghostCrop.Family, out Collider obstacle))
                {
                    crowded = true;
                    reason = $"obstacle {Describe(obstacle)} at {Distance(NearestPoint(obstacle, final), final):0.00} m (grow radius {grow:0.00})";
                    status = Player.PlacementStatus.MoreSpace;
                }
                else if (_scanner.HasRoof(final))
                {
                    crowded = true;
                    reason = "roof overhead";
                    status = Player.PlacementStatus.Invalid;
                }
            }
            Explain(ghost.name, ghostCrop, reason);
            piece.SetInvalidPlacementHeightlight(status != Player.PlacementStatus.Valid);

            if (result.Snapped)
                _renderer.DrawGrid(result.Lattice, final);
            else
                _renderer.HideGrid();
            _renderer.DrawRing(final, grow, crowded || status != Player.PlacementStatus.Valid);
        }

        /// <summary>Logs why a spot was refused, once per reason, to aid diagnosis without flooding the log.</summary>
        private void Explain(string prefab, Crop ghost, string reason)
        {
            if (!_described.Contains(prefab))
            {
                _described.Add(prefab);
                Plugin.Log.LogInfo($"{prefab}: grow radius {ghost.GrowRadius:0.00} m, body {ghost.BodyRadius:0.00} m, family {ghost.Family}.");
            }
            if (reason == null || reason == _lastReason)
                return;
            _lastReason = reason;
            Plugin.Log.LogInfo($"{prefab} lacks room: {reason}.");
        }

        private string CrowdedBy(Crop ghost, SnapSettings snap)
        {
            foreach (Crop other in _nearby)
            {
                if (!Spacing.Conflicts(ghost, other, snap.Rule, snap.ExtraSpacing))
                    continue;
                return $"crop at {Vec2.Distance(ghost.Position, other.Position):0.00} m, minimum {Spacing.Required(ghost, other, snap.Rule, snap.ExtraSpacing):0.00} m " +
                       $"(theirs: radius {other.GrowRadius:0.00}, body {other.BodyRadius:0.00}, family {other.Family})";
            }
            return "neighbouring crop";
        }

        private static string Describe(Collider c)
        {
            ZNetView root = c.GetComponentInParent<ZNetView>();
            string owner = root != null ? Utils.GetPrefabName(root.gameObject) : "(no ZNetView)";
            return $"{owner}/{c.name} [{LayerMask.LayerToName(c.gameObject.layer)}]";
        }

        /// <summary><c>Collider.ClosestPoint</c> does not accept a concave MeshCollider; fall back to its bounding box.</summary>
        private static Vector3 NearestPoint(Collider c, Vector3 point)
            => c is MeshCollider mesh && !mesh.convex ? c.bounds.ClosestPoint(point) : c.ClosestPoint(point);

        private static float Distance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private IReadOnlyList<Crop> Nearby(string prefab, Vector3 raw, Crop ghost, SnapSettings snap)
        {
            float now = Time.time;
            if (prefab == _scannedPrefab && (raw - _scannedAt).sqrMagnitude < RescanDistance * RescanDistance
                && now - _scannedTime < RescanInterval)
                return _nearby;

            // Widest cell the field around this sapling can have, times reach + search + margin.
            float widestCell = (2f * ghost.GrowRadius + snap.ExtraSpacing + 0.5f) * snap.Step;
            float radius = widestCell * (snap.ReachCells + snap.SearchRadius + 1.5f);

            _nearby.Clear();
            _nearby.AddRange(_scanner.Crops(raw, radius, ghost.Family));
            _scannedPrefab = prefab;
            _scannedAt = raw;
            _scannedTime = now;
            return _nearby;
        }

        /// <summary>
        /// The <c>UpdatePlacementGhost</c> checks that depend on where the sapling is, in the game's order
        /// (the last failing one wins). Position-independent statuses are left as the game set them.
        /// </summary>
        private static void Revalidate(Player player, Piece piece, Vector3 position, ref Player.PlacementStatus status, bool flashGuardStone)
        {
            switch (status)
            {
                case Player.PlacementStatus.Valid:
                case Player.PlacementStatus.NeedCultivated:
                case Player.PlacementStatus.NeedDirt:
                case Player.PlacementStatus.NoBuildZone:
                case Player.PlacementStatus.PrivateZone:
                case Player.PlacementStatus.BlockedbyPlayer:
                case Player.PlacementStatus.WrongBiome:
                    break;
                default:
                    return;
            }

            status = Player.PlacementStatus.Valid;
            Heightmap heightmap = Heightmap.FindHeightmap(position);
            if (piece.m_cultivatedGroundOnly && (heightmap == null || !heightmap.IsCultivated(position)))
                status = Player.PlacementStatus.NeedCultivated;
            if (piece.m_vegetationGroundOnly && !HasVegetationGround(heightmap, position))
                status = Player.PlacementStatus.NeedDirt;
            if (Location.IsInsideNoBuildLocation(position))
                status = Player.PlacementStatus.NoBuildZone;
            if (!PrivateArea.CheckAccess(position, 0f, flashGuardStone))
                status = Player.PlacementStatus.PrivateZone;
            if (BlockedByPlayers != null && BlockedByPlayers(player))
                status = Player.PlacementStatus.BlockedbyPlayer;
            if (piece.m_onlyInBiome != Heightmap.Biome.None && (Heightmap.FindBiome(position) & piece.m_onlyInBiome) == 0)
                status = Player.PlacementStatus.WrongBiome;
        }

        private static bool HasVegetationGround(Heightmap heightmap, Vector3 position)
        {
            if (heightmap == null)
                return false;
            float mask = heightmap.GetVegetationMask(position);
            return heightmap.GetBiome(position) == Heightmap.Biome.AshLands ? mask <= 0.1f : mask >= 0.25f;
        }

        /// <summary>The same key the game uses to disable snapping to build points (Shift on keyboard).</summary>
        private static bool FreePlacementHeld(Player player)
        {
            if (ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive())
                return player.AlternativePlacementActive;
            return ZInput.GetButton("AltPlace") || (ZInput.GetButton("JoyAltPlace") && !ZInput.GetButton("JoyRotate"));
        }

        private static float Ground(Vector3 p)
        {
            ZoneSystem zones = ZoneSystem.instance;
            return zones != null && zones.GetGroundHeight(p, out float height) ? height : p.y;
        }
    }
}
