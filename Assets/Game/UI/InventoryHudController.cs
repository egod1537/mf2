using System;
using System.Collections.Generic;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.Game.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer;

namespace MF2.Game.UI
{
    [DisallowMultipleComponent]
    public sealed class InventoryHudController : MonoBehaviour
    {
        [SerializeField] private GameObject inventoryPanel;
        [SerializeField] private UnityEngine.UI.Text healthText;
        [SerializeField] private UnityEngine.UI.Image healthFill;
        [SerializeField] private RectTransform dragGhost;
        [SerializeField] private UnityEngine.UI.Text dragGhostLabel;
        [SerializeField] private InventorySlotButton[] quickbarSlots = Array.Empty<InventorySlotButton>();
        [SerializeField] private InventorySlotButton[] bagSlots = Array.Empty<InventorySlotButton>();
        [SerializeField] private InventorySlotButton[] equipmentSlots = Array.Empty<InventorySlotButton>();

        private ILocalInventoryReader inventoryReader;
        private IEntityStateReader entityReader;
        private IInventoryCommandSender commandSender;
        private InventoryContainerKind selectedContainer;
        private int selectedSlot = -1;
        private int selectedCount;

        public bool IsOpen => inventoryPanel != null && inventoryPanel.activeSelf;

        [Inject]
        private void Construct(
            ILocalInventoryReader inventories,
            IEntityStateReader entities,
            IInventoryCommandSender sender)
        {
            inventoryReader = inventories;
            entityReader = entities;
            commandSender = sender;
        }

        public void Configure(
            GameObject panel,
            InventorySlotButton[] quickbar,
            InventorySlotButton[] bag,
            InventorySlotButton[] equipment,
            UnityEngine.UI.Text health,
            UnityEngine.UI.Image healthBarFill = null,
            RectTransform ghost = null,
            UnityEngine.UI.Text ghostLabel = null)
        {
            inventoryPanel = panel;
            quickbarSlots = quickbar ?? Array.Empty<InventorySlotButton>();
            bagSlots = bag ?? Array.Empty<InventorySlotButton>();
            equipmentSlots = equipment ?? Array.Empty<InventorySlotButton>();
            healthText = health;
            healthFill = healthBarFill;
            dragGhost = ghost;
            dragGhostLabel = ghostLabel;
            if (dragGhost != null)
                dragGhost.gameObject.SetActive(false);
            BindButtons(quickbarSlots, InventoryContainerKind.Quickbar);
            BindButtons(bagSlots, InventoryContainerKind.Bag);
            BindButtons(equipmentSlots, InventoryContainerKind.Equipment);
        }

        private void OnEnable()
        {
            if (inventoryReader != null)
                inventoryReader.InventoryChanged += Render;
            Render();
        }

        private void OnDisable()
        {
            if (inventoryReader != null)
                inventoryReader.InventoryChanged -= Render;
            GameplayInputGate.IsInventoryOpen = false;
            GameplayInputGate.IsPointerOverInterface = false;
            CancelSelection();
        }

        private void Update()
        {
            GameplayInputGate.IsPointerOverInterface =
                EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.eKey.wasPressedThisFrame)
                    SetOpen(!IsOpen);
                for (var i = 0; i < InventoryState.QuickbarSlotCount; i++)
                {
                    if (WasNumberPressed(keyboard, i))
                        SelectQuickbar(i);
                }
                if (keyboard.escapeKey.wasPressedThisFrame && IsOpen)
                    SetOpen(false);
            }

            var scroll = Mouse.current?.scroll.ReadValue().y ?? 0f;
            if (!IsOpen && Mathf.Abs(scroll) > 0.01f &&
                inventoryReader != null &&
                inventoryReader.TryGetLocalInventory(out _, out var inventory))
            {
                var direction = scroll > 0f ? -1 : 1;
                SelectQuickbar((inventory.SelectedQuickbarSlot + direction +
                    InventoryState.QuickbarSlotCount) % InventoryState.QuickbarSlotCount);
            }
            RenderStatus();
        }

        public void HandleSlotClick(
            InventoryContainerKind container,
            int slot,
            PointerEventData.InputButton button)
        {
            if (inventoryReader == null ||
                !inventoryReader.TryGetLocalInventory(out _, out var inventory))
                return;

            if (selectedSlot < 0)
            {
                var stack = inventory.GetSlot(container, slot);
                if (stack.IsEmpty)
                    return;
                var keyboard = Keyboard.current;
                if (keyboard != null &&
                    (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) &&
                    TryQuickTransfer(inventory, container, slot, stack))
                {
                    return;
                }
                selectedContainer = container;
                selectedSlot = slot;
                selectedCount = button == PointerEventData.InputButton.Right
                    ? Math.Max(1, (stack.Count + 1) / 2)
                    : stack.Count;
                Render();
                return;
            }

            if (container == selectedContainer && slot == selectedSlot)
            {
                CancelSelection();
                Render();
                return;
            }

            commandSender?.SendInventoryCommand(InventoryCommand.Move(
                selectedContainer,
                selectedSlot,
                container,
                slot,
                button == PointerEventData.InputButton.Right ? 1 : selectedCount));
            CancelSelection();
            Render();
        }

        public void BeginSlotDrag(
            InventoryContainerKind container,
            int slot,
            Vector2 screenPosition)
        {
            if (!IsOpen || inventoryReader == null ||
                !inventoryReader.TryGetLocalInventory(out _, out var inventory))
                return;
            var stack = inventory.GetSlot(container, slot);
            if (stack.IsEmpty)
                return;

            selectedContainer = container;
            selectedSlot = slot;
            selectedCount = stack.Count;
            if (dragGhostLabel != null)
                dragGhostLabel.text = $"{GetItemName(stack.ItemId)}\n{stack.Count}";
            if (dragGhost != null)
            {
                dragGhost.gameObject.SetActive(true);
                dragGhost.position = screenPosition;
                dragGhost.SetAsLastSibling();
            }
            Render();
        }

        public void MoveSlotDrag(Vector2 screenPosition)
        {
            if (dragGhost != null && dragGhost.gameObject.activeSelf)
                dragGhost.position = screenPosition;
        }

        public void DropOnSlot(InventoryContainerKind container, int slot)
        {
            if (selectedSlot < 0)
                return;
            if (container != selectedContainer || slot != selectedSlot)
            {
                commandSender?.SendInventoryCommand(InventoryCommand.Move(
                    selectedContainer,
                    selectedSlot,
                    container,
                    slot,
                    selectedCount));
            }
            CancelSelection();
            Render();
        }

        public void EndSlotDrag()
        {
            if (selectedSlot >= 0)
            {
                CancelSelection();
                Render();
            }
            else if (dragGhost != null)
            {
                dragGhost.gameObject.SetActive(false);
            }
        }

        private bool TryQuickTransfer(
            InventoryState inventory,
            InventoryContainerKind sourceContainer,
            int sourceSlot,
            ItemStack source)
        {
            var targetContainer = sourceContainer == InventoryContainerKind.Bag
                ? InventoryContainerKind.Quickbar
                : InventoryContainerKind.Bag;
            if (sourceContainer == InventoryContainerKind.Equipment)
                targetContainer = InventoryContainerKind.Bag;

            var targetSlot = -1;
            var count = source.Count;
            for (var i = 0; i < InventoryState.GetSlotCount(targetContainer); i++)
            {
                var target = inventory.GetSlot(targetContainer, i);
                if (target.ItemId != source.ItemId || target.IsEmpty)
                    continue;
                var capacity = ItemCatalog.GetMaximumStack(source.ItemId) - target.Count;
                if (capacity <= 0)
                    continue;
                targetSlot = i;
                count = Math.Min(count, capacity);
                break;
            }
            if (targetSlot < 0)
            {
                for (var i = 0; i < InventoryState.GetSlotCount(targetContainer); i++)
                {
                    if (!inventory.GetSlot(targetContainer, i).IsEmpty)
                        continue;
                    targetSlot = i;
                    break;
                }
            }
            if (targetSlot < 0)
                return false;
            return commandSender != null && commandSender.SendInventoryCommand(
                InventoryCommand.Move(
                    sourceContainer,
                    sourceSlot,
                    targetContainer,
                    targetSlot,
                    count));
        }

        private void SetOpen(bool open)
        {
            if (inventoryPanel != null)
                inventoryPanel.SetActive(open);
            GameplayInputGate.IsInventoryOpen = open;
            if (!open)
                CancelSelection();
        }

        private void SelectQuickbar(int slot)
        {
            if (slot < 0 || slot >= InventoryState.QuickbarSlotCount)
                return;
            commandSender?.SendInventoryCommand(InventoryCommand.SelectQuickbar(slot));
        }

        private void Render()
        {
            if (inventoryReader == null ||
                !inventoryReader.TryGetLocalInventory(out var playerId, out var inventory))
            {
                RenderEmpty(quickbarSlots);
                RenderEmpty(bagSlots);
                RenderEmpty(equipmentSlots);
                if (healthText != null)
                    healthText.text = "HP -- / --";
                if (healthFill != null)
                    healthFill.fillAmount = 0f;
                return;
            }

            RenderContainer(quickbarSlots, InventoryContainerKind.Quickbar, inventory);
            RenderContainer(bagSlots, InventoryContainerKind.Bag, inventory);
            RenderContainer(equipmentSlots, InventoryContainerKind.Equipment, inventory);
            RenderStatus();
        }

        private void RenderStatus()
        {
            if (healthText == null || inventoryReader == null || entityReader == null ||
                !inventoryReader.TryGetLocalInventory(out var playerId, out _) ||
                !entityReader.TryGetPlayer(playerId, out var model))
            {
                return;
            }
            healthText.text =
                $"HP {model.Health} / {model.MaximumHealth}    " +
                $"DASH {model.Actions.DashCharges}/{PlayerActionState.MaximumDashCharges}";
            if (healthFill != null)
            {
                healthFill.fillAmount = model.MaximumHealth > 0
                    ? Mathf.Clamp01((float)model.Health / model.MaximumHealth)
                    : 0f;
            }
        }

        private void RenderContainer(
            InventorySlotButton[] views,
            InventoryContainerKind container,
            InventoryState inventory)
        {
            for (var i = 0; i < views.Length; i++)
            {
                var selected = container == selectedContainer && i == selectedSlot;
                if (container == InventoryContainerKind.Quickbar &&
                    i == inventory.SelectedQuickbarSlot)
                    selected = true;
                views[i]?.Render(inventory.GetSlot(container, i), selected);
            }
        }

        private static void RenderEmpty(IEnumerable<InventorySlotButton> views)
        {
            foreach (var view in views)
                view?.Render(ItemStack.Empty, false);
        }

        private void BindButtons(
            IReadOnlyList<InventorySlotButton> views,
            InventoryContainerKind container)
        {
            for (var i = 0; i < views.Count; i++)
                views[i]?.Configure(this, container, i);
        }

        private void CancelSelection()
        {
            selectedSlot = -1;
            selectedCount = 0;
            if (dragGhost != null)
                dragGhost.gameObject.SetActive(false);
        }

        private static string GetItemName(uint id)
        {
            if (id == ItemCatalog.Stone) return "STONE";
            if (id == ItemCatalog.Dirt) return "DIRT";
            if (id == ItemCatalog.Grass) return "GRASS";
            if (id == ItemCatalog.Water) return "WATER";
            return "?";
        }

        private static bool WasNumberPressed(Keyboard keyboard, int index) => index switch
        {
            0 => keyboard.digit1Key.wasPressedThisFrame,
            1 => keyboard.digit2Key.wasPressedThisFrame,
            2 => keyboard.digit3Key.wasPressedThisFrame,
            3 => keyboard.digit4Key.wasPressedThisFrame,
            4 => keyboard.digit5Key.wasPressedThisFrame,
            5 => keyboard.digit6Key.wasPressedThisFrame,
            6 => keyboard.digit7Key.wasPressedThisFrame,
            _ => false
        };
    }

}
