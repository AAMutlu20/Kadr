using UnityEngine;

namespace Items
{
    /// <summary>
    /// Static data for one kind of item. Create with: right click in Project, Create, Kadr, Item.
    /// Width and Height are the footprint on the inventory grid in cells, before rotation (a shotgun-sized
    /// item would be 3 x 1, a potion 1 x 1).
    /// </summary>
    [CreateAssetMenu(menuName = "Kadr/Item", fileName = "NewItem")]
    public class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string id = "item";
        [SerializeField] private string displayName = "Item";
        [SerializeField, TextArea] private string description;
        [Tooltip("Optional. Without an icon the grid shows a plain tile with the item's name.")]
        [SerializeField] private Sprite icon;

        [Header("3D inventory model")]
        [Tooltip("Shown in the suitcase. Author it lying flat with its LONG side along X and its root at zero rotation. " +
                 "It is scaled automatically to fit the grid footprint below. Empty = a plain tinted block.")]
        [SerializeField] private GameObject inventoryModel;

        [Header("Grid footprint (cells)")]
        [SerializeField, Min(1)] private int width = 1;
        [SerializeField, Min(1)] private int height = 1;

        [Header("Stacking")]
        [Tooltip("1 means the item does not stack.")]
        [SerializeField, Min(1)] private int maxStack = 1;

        [Header("Use")]
        [Tooltip("Leave empty for items that cannot be used (keys, documents, batteries for now).")]
        [SerializeField] private ItemEffect effect;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public GameObject InventoryModel => inventoryModel;
        public int Width => width;
        public int Height => height;
        public int MaxStack => maxStack;
        public ItemEffect Effect => effect;
    }
}