using UnityEngine;
using UnityEngine.InputSystem;

public class ShopTrigger : MonoBehaviour, IInteractable
{
    [Header("UI del Pueblo")]
    [SerializeField] private GameObject shopPanel;

    private bool isOpen = false;

    private void Start()
    {
        if (shopPanel != null)
            shopPanel.SetActive(false);
    }

    private void Update()
    {
        // Si está abierta, podés cerrarla con Escape
        if (isOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CloseShop();
        }
    }

    public void Interact(GameObject interactor)
    {
        OpenShop();
    }

    public void OpenShop()
    {
        isOpen = true;
        if (shopPanel != null) shopPanel.SetActive(true);

        // Libera el mouse para poder hacer clics en la tienda
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseShop()
    {
        isOpen = false;
        if (shopPanel != null) shopPanel.SetActive(false);

        // Bloquea el mouse de nuevo para volver a jugar
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}