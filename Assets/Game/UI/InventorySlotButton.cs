using MF2.Game.Domain;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MF2.Game.UI
{
    [DisallowMultipleComponent]
    public sealed class InventorySlotButton : MonoBehaviour,
        IPointerClickHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler
    {
        [SerializeField] private UnityEngine.UI.Image background;
        [SerializeField] private UnityEngine.UI.Image itemIcon;
        [SerializeField] private UnityEngine.UI.Image selection;
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

        public void ConfigureVisuals(
            UnityEngine.UI.Image slotBackground,
            UnityEngine.UI.Image icon,
            UnityEngine.UI.Image selectionOverlay,
            UnityEngine.UI.Text itemLabel)
        {
            background = slotBackground;
            itemIcon = icon;
            selection = selectionOverlay;
            label = itemLabel;
        }

        public void Render(ItemStack stack, bool selected)
        {
            if (background != null)
                background.color = Color.white;
            if (itemIcon != null)
            {
                itemIcon.enabled = !stack.IsEmpty;
                itemIcon.color = ItemColor(stack.ItemId);
            }
            if (selection != null)
                selection.gameObject.SetActive(selected);
            if (label != null)
                label.text = stack.IsEmpty ? string.Empty : $"{ItemName(stack.ItemId)}\n{stack.Count}";
        }

        public void OnPointerClick(PointerEventData eventData) =>
            owner?.HandleSlotClick(container, slot, eventData.button);

        public void OnBeginDrag(PointerEventData eventData) =>
            owner?.BeginSlotDrag(container, slot, eventData.position);

        public void OnDrag(PointerEventData eventData) =>
            owner?.MoveSlotDrag(eventData.position);

        public void OnEndDrag(PointerEventData eventData) =>
            owner?.EndSlotDrag();

        public void OnDrop(PointerEventData eventData) =>
            owner?.DropOnSlot(container, slot);

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
