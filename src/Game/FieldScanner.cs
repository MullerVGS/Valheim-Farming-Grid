using System.Collections.Generic;
using FarmingGrid.Core;
using UnityEngine;

namespace FarmingGrid.Game
{
    /// <summary>Physics queries around the sapling, using the same masks <see cref="Plant"/> uses to decide whether it grows.</summary>
    internal sealed class FieldScanner
    {
        private readonly CropCatalog _catalog;
        private readonly Collider[] _hits = new Collider[1024];
        private readonly List<Crop> _crops = new List<Crop>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private int _roofMask;

        public FieldScanner(CropCatalog catalog)
        {
            _catalog = catalog;
        }

        private int SpaceMask => _catalog.SpaceMask;

        /// <summary>The <c>Plant.HaveRoof</c> mask.</summary>
        private int RoofMask => _roofMask != 0 ? _roofMask
            : _roofMask = LayerMask.GetMask("Default", "static_solid", "piece");

        /// <summary>Crops within a radius, one per network object (several colliders of the same plant count once).</summary>
        public IReadOnlyList<Crop> Crops(Vector3 center, float radius, int family)
        {
            _crops.Clear();
            _seen.Clear();
            int count = Physics.OverlapSphereNonAlloc(center, radius, _hits, SpaceMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                ZNetView root = _hits[i].GetComponentInParent<ZNetView>();
                if (root == null || !_seen.Add(root.GetInstanceID()))
                    continue;
                if (_catalog.TryGetCrop(root, family, out Crop crop))
                    _crops.Add(crop);
            }
            return _crops;
        }

        /// <summary>
        /// Something that is not a crop inside the grow radius (fence, rock, tree, building): the game would flag
        /// the sapling as "needs more space". Listed crops are skipped because the spacing rule already handles them.
        /// </summary>
        public bool HasObstacle(Vector3 position, float growRadius, int family, out Collider obstacle)
        {
            int count = Physics.OverlapSphereNonAlloc(position, growRadius, _hits, SpaceMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                ZNetView root = _hits[i].GetComponentInParent<ZNetView>();
                if (root == null || !_catalog.TryGetCrop(root, family, out _))
                {
                    obstacle = _hits[i];
                    return true;
                }
            }
            obstacle = null;
            return false;
        }

        /// <summary>Roof overhead: the sapling would get "no sun".</summary>
        public bool HasRoof(Vector3 position) => Physics.Raycast(position, Vector3.up, 100f, RoofMask, QueryTriggerInteraction.Ignore);
    }
}
