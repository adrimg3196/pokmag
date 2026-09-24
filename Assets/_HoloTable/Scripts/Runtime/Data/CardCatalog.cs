using System;
using System.Collections.Generic;
using UnityEngine;

namespace HoloTable.Data
{
    /// <summary>
    /// Maps reference-image names coming from the tracking SDK to entity definitions.
    /// Keep one catalog per game (or one master catalog) and assign it to the spawn director.
    /// </summary>
    [CreateAssetMenu(menuName = "HoloTable/Card Catalog", fileName = "CardCatalog")]
    public sealed class CardCatalog : ScriptableObject
    {
        [SerializeField] private List<EntityDefinition> definitions = new List<EntityDefinition>();
        [SerializeField] private List<CardCatalog> includes = new List<CardCatalog>();

        private Dictionary<string, EntityDefinition> _lookup;

        public bool TryResolve(string referenceName, out EntityDefinition definition)
        {
            if (string.IsNullOrEmpty(referenceName))
            {
                definition = null;
                return false;
            }

            if (_lookup == null)
            {
                _lookup = new Dictionary<string, EntityDefinition>(StringComparer.OrdinalIgnoreCase);
                Collect(this, new HashSet<CardCatalog>());
            }

            return _lookup.TryGetValue(referenceName, out definition);
        }

        private void Collect(CardCatalog catalog, HashSet<CardCatalog> visited)
        {
            if (catalog == null || !visited.Add(catalog)) return;

            foreach (EntityDefinition def in catalog.definitions)
            {
                if (def == null) continue;
                foreach (string name in def.ReferenceImageNames)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    if (_lookup.TryGetValue(name, out EntityDefinition existing) && existing != def)
                    {
                        Debug.LogWarning($"[HoloTable] Reference image '{name}' is mapped to both '{existing.name}' and '{def.name}'. Keeping the first.", this);
                        continue;
                    }

                    _lookup[name] = def;
                }
            }

            foreach (CardCatalog child in catalog.includes)
            {
                Collect(child, visited);
            }
        }

        private void OnValidate() => _lookup = null;
    }
}
