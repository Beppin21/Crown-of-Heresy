using UnityEngine;
using UnityEngine.InputSystem;

public class ShopTrigger : MonoBehaviour, IInteractable
{
    [Header("UI del Pueblo")]
    [SerializeField] private GameObject shopPanel;

    // Versión del armado del puesto en la escena (el puesto y la mercader). La aplica sola
    // Scripts/Editor/TownShopSetup.cs a las escenas con una versión vieja: subirla al cambiarlo.
    // 2: la mercader no se había escalado ni ubicado (se medían solo mallas con esqueleto y ella no tiene)
    // 3: la mercader riggeada en Mixamo reemplaza a la de pose fija, con su capa
    // 4: la mercader es un personaje de Mixamo (el rig de la de Belén quedó con el brazo trabado)
    // 5: las texturas del personaje de Mixamo (venían adentro del FBX y no se veían)
    public const int LayoutVersion = 5;
    [SerializeField, HideInInspector] private int layoutVersion;
    public int AppliedLayoutVersion => layoutVersion;

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
        if (DialogosTienda.Instancia != null) DialogosTienda.Instancia.AlAbrir();

        // Libera el mouse para poder hacer clics en la tienda
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseShop()
    {
        isOpen = false;
        if (Current == this) Current = null;
        if (shopPanel != null) shopPanel.SetActive(false);
        if (DialogosTienda.Instancia != null) DialogosTienda.Instancia.AlCerrar();

        // Bloquea el mouse de nuevo para volver a jugar
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
