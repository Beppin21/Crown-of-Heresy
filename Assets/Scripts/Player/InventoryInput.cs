using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Recibe el mensaje "OnInventary" que manda el Player Input (Behavior: Send Messages)
// cuando se dispara la accion "Inventary" (Tab), y lo delega a InventoryUI.
public class InventoryInput : MonoBehaviour
{
    [SerializeField] private InventoryUI inventoryUI;

#if ENABLE_INPUT_SYSTEM
    public void OnInventary(InputValue value)
    {
        if (!value.isPressed) return;
        inventoryUI?.ToggleInventory();
    }
#endif
}
