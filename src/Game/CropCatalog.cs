using System.Collections.Generic;
using FarmingGrid.Core;
using UnityEngine;

namespace FarmingGrid.Game
{
    /// <summary>
    /// Recognizes which world objects are crops and measures each one's footprint.
    /// Three sources: saplings (<see cref="Plant"/>), fully grown harvests (each sapling's <c>m_grownPrefabs</c>,
    /// which occupy the same spot without a <see cref="Plant"/>) and the extra list from the config.
    /// </summary>
    internal sealed class CropCatalog
    {
        /// <summary>Cultivated-ground crops; everything else (trees, bushes, modded plantables) belongs to the other family.</summary>
        public const int Cultivated = 1;
        public const int Wild = 0;

        private readonly struct Sapling
        {
            public readonly float Grow;
            public readonly float Body;
            public readonly int Family;

            public Sapling(float grow, float body, int family)
            {
                Grow = grow;
                Body = body;
                Family = family;
            }
        }

        private readonly Settings _settings;
        private readonly Dictionary<string, Sapling> _grownFrom = new Dictionary<string, Sapling>();
        private ZNetScene _indexedScene;
        private int _spaceMask;

        public CropCatalog(Settings settings)
        {
            _settings = settings;
        }

        /// <summary>The <c>Plant.HaveGrowSpace</c> mask: only colliders on these layers count as a body.</summary>
        public int SpaceMask => _spaceMask != 0 ? _spaceMask
            : _spaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");

        /// <summary>Footprint of the sapling the player is holding, or <c>false</c> if it is not a crop.</summary>
        public bool TryGetGhost(GameObject ghost, out Crop crop)
        {
            Vector3 p = ghost.transform.position;
            var position = new Vec2(p.x, p.z);
            var plant = ghost.GetComponent<Plant>();
            if (plant != null)
            {
                crop = new Crop(position, plant.m_growRadius, BodyRadius(ghost, ownOnly: true), FamilyOf(plant));
                return plant.m_growRadius > 0f;
            }
            if (_settings.CustomCrops.TryGetValue(Utils.GetPrefabName(ghost), out float radius))
            {
                crop = new Crop(position, radius, BodyRadius(ghost, ownOnly: false), Wild);
                return true;
            }
            crop = default;
            return false;
        }

        /// <summary>
        /// If the network object (prefab root) counts as a crop for a sapling of <paramref name="family"/>, returns its footprint.
        /// A growing sapling always counts, because its grow radius reaches the neighbour. Something already grown only counts
        /// if it is the same family (the ripe onion in the garden); grown trees and bushes are left to physics like any obstacle,
        /// because their geometric body (canopy, branches) comes out far larger than the trunk the game actually tests.
        /// </summary>
        public bool TryGetCrop(ZNetView root, int family, out Crop crop)
        {
            GameObject go = root.gameObject;
            Vector3 p = go.transform.position;
            var position = new Vec2(p.x, p.z);

            var plant = go.GetComponent<Plant>();
            if (plant != null)
            {
                crop = new Crop(position, plant.m_growRadius, BodyRadius(go, ownOnly: true), FamilyOf(plant));
                return true;
            }

            string prefab = Utils.GetPrefabName(go);
            if (GrownFrom(prefab, out Sapling sapling) && sapling.Family == family)
            {
                crop = Crop.Grown(position, BodyRadius(go, ownOnly: false), sapling.Grow, sapling.Body, sapling.Family);
                return true;
            }
            if (family == Wild && _settings.CustomCrops.TryGetValue(prefab, out float radius))
            {
                crop = new Crop(position, radius, BodyRadius(go, ownOnly: false), Wild);
                return true;
            }

            crop = default;
            return false;
        }

        private static int FamilyOf(Plant plant) => plant.m_needCultivatedGround ? Cultivated : Wild;

        private bool GrownFrom(string prefab, out Sapling sapling)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene != null && scene != _indexedScene)
                Index(scene);
            return _grownFrom.TryGetValue(prefab, out sapling);
        }

        /// <summary>A pickable onion occupies the onion sapling's spot: records which sapling each harvest grows from.</summary>
        private void Index(ZNetScene scene)
        {
            _indexedScene = scene;
            _grownFrom.Clear();
            foreach (GameObject prefab in scene.m_prefabs)
            {
                var plant = prefab != null ? prefab.GetComponent<Plant>() : null;
                if (plant == null || plant.m_grownPrefabs == null)
                    continue;
                var sapling = new Sapling(plant.m_growRadius, BodyRadius(prefab, ownOnly: true), FamilyOf(plant));
                foreach (GameObject grown in plant.m_grownPrefabs)
                {
                    if (grown == null || (_grownFrom.TryGetValue(grown.name, out Sapling known) && known.Grow >= sapling.Grow))
                        continue;
                    _grownFrom[grown.name] = sapling;
                }
            }
            Plugin.Log.LogInfo($"{_grownFrom.Count} harvests recognized from the game's saplings.");
        }

        /// <summary>
        /// Horizontal reach of the colliders, measured from their geometry (the ghost's collider is disabled
        /// and its <c>Collider.bounds</c> comes back zeroed). <paramref name="ownOnly"/> looks only at the root object, which is what
        /// <c>Plant.HaveGrowSpace</c> recognizes as a plant.
        /// </summary>
        private float BodyRadius(GameObject go, bool ownOnly)
        {
            Collider[] colliders = ownOnly ? go.GetComponents<Collider>() : go.GetComponentsInChildren<Collider>(includeInactive: true);
            Vector3 origin = go.transform.position;
            float radius = 0f;
            foreach (Collider c in colliders)
            {
                if (c.isTrigger || !InSpaceMask(c))
                    continue;
                radius = Mathf.Max(radius, HorizontalReach(c, origin));
            }
            return radius;
        }

        /// <summary>
        /// The ghost moves everything to the "ghost" layer; there the prefab's layer applies. A collider outside the mask
        /// (interaction, hitbox) is invisible to the game's space test, so it is not a body.
        /// </summary>
        private bool InSpaceMask(Collider c)
        {
            int layer = c.gameObject.layer;
            return layer == GhostLayer || (SpaceMask & (1 << layer)) != 0;
        }

        private static int _ghostLayer = -2;
        private static int GhostLayer => _ghostLayer != -2 ? _ghostLayer : _ghostLayer = LayerMask.NameToLayer("ghost");

        private static float HorizontalReach(Collider c, Vector3 origin)
        {
            Transform t = c.transform;
            Vector3 scale = t.lossyScale;
            float flat = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));

            switch (c)
            {
                case SphereCollider s:
                    return Offset(t.TransformPoint(s.center), origin) + s.radius * Mathf.Max(flat, Mathf.Abs(scale.y));
                case CapsuleCollider k:
                    float along = k.direction == 1 ? k.radius : k.height * 0.5f;
                    return Offset(t.TransformPoint(k.center), origin) + along * flat;
                case BoxCollider b:
                    Vector3 half = Vector3.Scale(b.size * 0.5f, scale);
                    return Offset(t.TransformPoint(b.center), origin) + new Vector2(half.x, half.z).magnitude;
                case MeshCollider m when m.sharedMesh != null:
                    Bounds bounds = m.sharedMesh.bounds;
                    Vector3 ext = Vector3.Scale(bounds.extents, scale);
                    return Offset(t.TransformPoint(bounds.center), origin) + new Vector2(ext.x, ext.z).magnitude;
                default:
                    return c.enabled ? new Vector2(c.bounds.extents.x, c.bounds.extents.z).magnitude : 0f;
            }
        }

        private static float Offset(Vector3 point, Vector3 origin) => new Vector2(point.x - origin.x, point.z - origin.z).magnitude;
    }
}
