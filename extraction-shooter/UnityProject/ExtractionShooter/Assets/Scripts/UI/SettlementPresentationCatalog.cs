using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SettlementPresentationCatalog", menuName = "Chef Dungeon/Settlement Presentation Catalog")]
public sealed class SettlementPresentationCatalog : ScriptableObject
{
    [Serializable]
    public sealed class ResourcePresentation
    {
        public ResourceType type;
        public string displayName;
        public Sprite icon;
    }

    [Serializable]
    public sealed class PetPresentation
    {
        public PetType type;
        public string displayName;
        public Sprite icon;
    }

    public List<ResourcePresentation> resources = new List<ResourcePresentation>();
    public List<PetPresentation> pets = new List<PetPresentation>();

    public bool TryGetResource(ResourceType type, out ResourcePresentation presentation)
    {
        if (resources != null)
        {
            foreach (ResourcePresentation entry in resources)
            {
                if (entry == null || entry.type != type) continue;
                presentation = entry;
                return true;
            }
        }
        presentation = null;
        return false;
    }

    public bool TryGetPet(PetType type, out PetPresentation presentation)
    {
        if (pets != null)
        {
            foreach (PetPresentation entry in pets)
            {
                if (entry == null || entry.type != type) continue;
                presentation = entry;
                return true;
            }
        }
        presentation = null;
        return false;
    }
}
