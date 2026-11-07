using MF2.Game.Domain;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MF2.Game.UI
{
    [DisallowMultipleComponent]
    public sealed class InventorySlotButton : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private UnityEngine.UI.Image background;
        [SerializeField] private UnityEngine.UI.Text label;
        private InventoryHudController owner;
        private InventoryContainerKind container;
        private int slot;

        public void Configure(
            InventoryHudController controller,
            InventoryContainerKind containerKind,
            int slotIndex)
        {
            owner = controller;
            container = containerKind;
            slot = slotIndex;
            if (background == null)
                background = GetComponent<UnityEngine.UI.Image>();
            if (label == null)
                label = GetComponentInChildren<UnityEngine.UI.Text>(true);
        }

        public void Render(ItemStack stack, bool selected)
        {
            if (background != null)
                background.color = selected
                    ? new Color(0.95f, 0.78f, 0.24f, 0.96f)
                    : ItemColor(stack.ItemId);
            if (label != null)
                label.text = stack.IsEmpty ? string.Empty : $"{ItemName(stack.ItemId)}\n{stack.Count}";
        }

        public void OnPointerClick(PointerEventData eventData) =>
            owner?.HandleSlotClick(container, slot, eventData.button);

        private static string ItemName(uint id)
        {
            if (id == ItemCatalog.Stone) return "STONE";
            if (id == ItemCatalog.Dirt) return "DIRT";
            if (id == ItemCatalog.Grass) return "GRASS";
            if (id == ItemCatalog.Water) return "WATER";
            return "?";
        }

        private static Color ItemColor(uint id)
        {
            if (id == ItemCatalog.Stone) return new Color(0.35f, 0.38f, 0.42f, 0.94f);
            if (id == ItemCatalog.Dirt) return new Color(0.35f, 0.22f, 0.12f, 0.94f);
            if (id == ItemCatalog.Grass) return new Color(0.22f, 0.48f, 0.18f, 0.94f);
            if (id == ItemCatalog.Water) return new Color(0.12f, 0.42f, 0.72f, 0.82f);
            return new Color(0.055f, 0.07f, 0.095f, 0.94f);
        }
    }
}
