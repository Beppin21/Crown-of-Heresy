using UnityEngine;
using UnityEngine.InputSystem;

public class ShopTrigger : MonoBehaviour, IInteractable
{
    [Header("UI del Pueblo")]
    [SerializeField] private GameObject shopPanel;

    private bool isOpen = false;

    // La tienda abierta ahora, para que el botón "Leave" de la ventana (ShopManager) la cierre
    public static ShopTrigger Current { get; private set; }

    private void Start()
    {
        if (shopPanel != null)
            shopPanel.SetActive(false);
    }

    private void Update()
    {
        if (!isOpen) return;

        // Si está abierta, podés cerrarla con Escape
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CloseShop();
            return;
        }

        // Cerrar el inventario (Tab) vuelve a bloquear el mouse: mientras la tienda esté abierta,
        // el mouse queda libre para poder hacer clic
        if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void Interact(GameObject interactor)
    {
        OpenShop();
    }

    public void OpenShop()
    {
        isOpen = true;
        Current = this;
        if (shopPanel != null) shopPanel.SetActive(true);

        // Libera el mouse para poder hacer clics en la tienda
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseShop()
    {
        isOpen = false;
        if (Current == this) Current = null;
        if (shopPanel != null) shopPanel.SetActive(false);

        // Bloquea el mouse de nuevo para volver a jugar
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
