using System.Collections.Generic;
using System.Linq;
using FarmingGrid.Core;
using UnityEngine;

namespace FarmingGrid.Game
{
    /// <summary>Draws the grid and the grow radius with a fixed set of reused LineRenderers.</summary>
    internal sealed class GridRenderer
    {
        /// <summary>Segments per cell: the line follows the terrain instead of cutting through the ground between two intersections.</summary>
        private const int SegmentsPerCell = 4;
        private const int RingSegments = 48;

        private readonly Settings _settings;
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private GameObject _root;
        private LineRenderer _ring;
        private Material _material;
        private bool _materialSearched;
        private bool _gridVisible;
        private bool _ringVisible;

        private Vector3 _lastCenter;
        private Lattice _lastLattice;
        private int _lastExtent;
        private bool _hasLast;

        public GridRenderer(Settings settings)
        {
            _settings = settings;
            settings.GridExtent.SettingChanged += (_, __) => Destroy();
            settings.GridColor.SettingChanged += (_, __) => Destroy();
            settings.LineWidth.SettingChanged += (_, __) => Destroy();
            settings.HeightOffset.SettingChanged += (_, __) => _hasLast = false;
        }

        public void DrawGrid(Lattice lattice, Vector3 center)
        {
            if (!_settings.ShowGrid.Value)
            {
                HideGrid();
                return;
            }
            if (!EnsureObjects())
                return;

            int extent = _settings.GridExtent.Value;
            if (_gridVisible && _hasLast && _lastExtent == extent && (_lastCenter - center).sqrMagnitude < 1e-6f
                && _lastLattice.AxisU.Equals(lattice.AxisU) && _lastLattice.Cell == lattice.Cell)
                return;

            var c = new Vec2(center.x, center.z);
            Vec2 u = lattice.AxisU * lattice.Cell;
            Vec2 v = lattice.AxisV * lattice.Cell;
            int line = 0;
            for (int j = -extent; j <= extent; j++)
            {
                Trace(_lines[line++], c + v * j - u * extent, u, extent * 2);
                Trace(_lines[line++], c + u * j - v * extent, v, extent * 2);
            }

            _lastCenter = center;
            _lastLattice = lattice;
            _lastExtent = extent;
            _hasLast = true;
            SetGridVisible(true);
        }

        public void DrawRing(Vector3 center, float radius, bool crowded)
        {
            if (!_settings.ShowGrowRadius.Value || radius <= 0f)
            {
                HideRing();
                return;
            }
            if (!EnsureObjects())
                return;

            Color color = crowded ? _settings.CrowdedColor.Value : _settings.FreeColor.Value;
            _ring.startColor = color;
            _ring.endColor = color;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * Mathf.PI * 2f / RingSegments;
                var p = new Vector3(center.x + Mathf.Cos(a) * radius, 0f, center.z + Mathf.Sin(a) * radius);
                p.y = Ground(p) + _settings.HeightOffset.Value;
                _ring.SetPosition(i, p);
            }
            if (!_ringVisible)
            {
                _ring.enabled = true;
                _ringVisible = true;
            }
        }

        public void Hide()
        {
            HideGrid();
            HideRing();
        }

        public void HideGrid() => SetGridVisible(false);

        public void HideRing()
        {
            if (_ringVisible && _ring != null)
                _ring.enabled = false;
            _ringVisible = false;
        }

        public void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root);
            _root = null;
            _ring = null;
            _lines.Clear();
            _gridVisible = false;
            _ringVisible = false;
            _hasLast = false;
        }

        private void Trace(LineRenderer lr, Vec2 start, Vec2 step, int cells)
        {
            int points = cells * SegmentsPerCell + 1;
            lr.positionCount = points;
            float offset = _settings.HeightOffset.Value;
            for (int i = 0; i < points; i++)
            {
                Vec2 q = start + step * (i / (float)SegmentsPerCell);
                var p = new Vector3(q.X, 0f, q.Z);
                p.y = Ground(p) + offset;
                lr.SetPosition(i, p);
            }
        }

        private static float Ground(Vector3 p)
        {
            ZoneSystem zones = ZoneSystem.instance;
            return zones != null && zones.GetGroundHeight(p, out float height) ? height : p.y;
        }

        private void SetGridVisible(bool visible)
        {
            if (_gridVisible == visible)
                return;
            foreach (LineRenderer lr in _lines)
            {
                if (lr != null)
                    lr.enabled = visible;
            }
            _gridVisible = visible;
        }

        /// <summary>The objects die on scene change (menu ↔ world); recreate them when gone.</summary>
        private bool EnsureObjects()
        {
            if (_root != null)
                return true;

            _lines.Clear();
            _gridVisible = false;
            _ringVisible = false;
            _hasLast = false;

            if (_material == null && !_materialSearched)
            {
                _materialSearched = true;
                _material = FindLineMaterial();
            }
            if (_material == null)
                return false;

            _root = new GameObject("FarmingGrid");
            Object.DontDestroyOnLoad(_root);

            int lineCount = (_settings.GridExtent.Value * 2 + 1) * 2;
            for (int i = 0; i < lineCount; i++)
                _lines.Add(NewLine("grid", _settings.GridColor.Value, loop: false));
            _ring = NewLine("grow-radius", _settings.FreeColor.Value, loop: true);
            _ring.positionCount = RingSegments;
            return true;
        }

        private LineRenderer NewLine(string name, Color color, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _material;
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.widthMultiplier = _settings.LineWidth.Value;
            lr.startColor = color;
            lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        /// <summary>Material with per-vertex color and transparency. Tries the default sprite shader, then the built-in line material.</summary>
        private static Material FindLineMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                Plugin.Log.LogInfo("Drawing the grid with the Sprites/Default shader.");
                return new Material(shader) { name = "FarmingGrid Line" };
            }

            Material builtin = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == "Default-Line");
            if (builtin != null)
            {
                Plugin.Log.LogInfo("Drawing the grid with the Default-Line material.");
                return builtin;
            }

            Plugin.Log.LogError("No line material available; the grid will not be drawn (snapping still works).");
            return null;
        }
    }
}
