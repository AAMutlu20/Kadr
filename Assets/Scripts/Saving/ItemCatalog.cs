using System.Linq;
using Items;
using UnityEngine;

namespace Saving
{
    /// <summary>
    /// Every ItemDefinition in the game, so a save file can say "pistol" and the game can find the asset again.
    /// One asset, in a folder called Resources, named exactly ItemCatalog:
    ///   Assets > Create > Kadr > Item Catalog, move it to Assets/Resources, then right-click its header in the
    ///   inspector and run "Collect all ItemDefinitions". Run that again whenever you add an item.
    /// </summary>
    [CreateAssetMenu(menuName = "Kadr/Item Catalog", fileName = "ItemCatalog")]
    public class ItemCatalog : ScriptableObject
    {
        [SerializeField] private ItemDefinition[] items = new ItemDefinition[0];

        private static ItemCatalog _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        public static ItemCatalog Instance
        {
            get
            {
                if (_instance) return _instance;

                _instance = Resources.Load<ItemCatalog>("ItemCatalog");
                if (!_instance) Debug.LogError("[ItemCatalog] No asset named ItemCatalog in a Resources folder. Items cannot be saved or loaded.");
                return _instance;
            }
        }

        public ItemDefinition Find(string id)
        {
            if (items == null || string.IsNullOrEmpty(id)) return null;

            foreach (var item in items)
            {
                if (item && item.Id == id) return item;
            }

            return null;
        }

#if UNITY_EDITOR
        [ContextMenu("Collect all ItemDefinitions")]
        private void Collect()
        {
            items = UnityEditor.AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(guid => UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)))
                .Where(item => item)
                .ToArray();

            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var item in items)
            {
                if (!seen.Add(item.Id)) Debug.LogWarning($"[ItemCatalog] Two items share the id \"{item.Id}\". Ids must be unique.", item);
            }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[ItemCatalog] {items.Length} items collected.", this);
        }
#endif
    }
}
