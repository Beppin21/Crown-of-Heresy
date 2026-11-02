using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    private const string LinkColor = "#E8C46A";

    [Header("Referencias de UI")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private TextMeshProUGUI itemListText;
    [SerializeField] private TextMeshProUGUI moneyDisplay;

    // Lista en memoria donde se guardan los datos de los objetos recogidos
    public static readonly List<LootData> inventory = new List<LootData>();
    public static int money = 0;
    private bool isOpen = false;

    public bool IsOpen => isOpen;

    private void Start()
    {
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);

        // Si venimos de otra escena y ya teníamos cosas, actualiza el texto
        UpdateUI();
    }

    private void Update()
    {
        if (InputSystem.actions.FindAction("Inventory").WasPressedThisFrame())
        {
            ToggleInventory();
        }

        // Clic sobre "Equip" / "Unequip" de un arma (son links del texto de la lista)
        if (isOpen && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            HandleClick(Mouse.current.position.ReadValue());
    }

    // El toggle lo dispara PlayerController.OnInventary (acción "inventary" del Input System,
    // bindeada a Tab), no una lectura directa acá, para no togglear dos veces por el mismo Tab.
    public void ToggleInventory()
    {
        isOpen = !isOpen;
        inventoryPanel.SetActive(isOpen);

        // Control del cursor: si el inventario está abierto liberamos el mouse, si no lo bloqueamos
        Cursor.lockState = isOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = isOpen;

        if (isOpen)
            UpdateUI();
    }

    // Método que llamará el objeto de loot al ser recogido
    public void AddItem(LootData item)
    {
        inventory.Add(item);
        Debug.Log($"Guardado en el inventario: {item.ItemName}");

        if (isOpen)
            UpdateUI();
    }

    // Lo llama PlayerCombat al cambiar de arma, para que se vea cuál quedó equipada
    public void RefreshIfOpen()
    {
        if (isOpen)
            UpdateUI();
    }

    // Refresca el texto en pantalla listando todo lo que tenemos
    private void UpdateUI()
    {
        if (itemListText == null) return;
        moneyDisplay.text = $"You have: ${money}";

        if (inventory.Count == 0)
        {
            itemListText.text = "The inventory is empty";
            return;
        }

        PlayerCombat combat = GetComponent<PlayerCombat>();
        itemListText.text = combat != null
            ? $"Weapons: up to {PlayerCombat.MaxEquipped} equipped, keys 1-{PlayerCombat.MaxEquipped} to switch\n\n"
            : "";

        for (int i = 0; i < inventory.Count; i++)
        {
            LootData item = inventory[i];
            string line = item is WeaponSO weapon && combat != null
                ? WeaponLine(combat, weapon, i)
                : $"• {item.ItemName} - ${item.Value}";
            itemListText.text += $"{line}\n  <i>{item.Description}</i>\n\n";
        }
    }

    // "[1] Heavy Sword (Dmg 25, Wgt 8) - $100 (in hand)  Unequip": la tecla del arma si está
    // equipada, y el link para equiparla o desequiparla (el id del link es su índice en el inventario)
    private static string WeaponLine(PlayerCombat combat, WeaponSO weapon, int index)
    {
        int key = combat.GetSlotKey(weapon);
        string line = (key > 0 ? $"[{key}] " : "• ") +
                      $"{weapon.ItemName} (Dmg {weapon.Damage}, Wgt {weapon.Weight}) - ${weapon.Value}";
        if (combat.EquippedWeapon == weapon)
            line += " (in hand)";

        if (key > 0)
            return line + $"   <link=\"{index}\"><color={LinkColor}><u>Unequip</u></color></link>";
        if (combat.HasFreeSlot)
            return line + $"   <link=\"{index}\"><color={LinkColor}><u>Equip</u></color></link>";
        return line + $"   <color=#888888>({PlayerCombat.MaxEquipped}/{PlayerCombat.MaxEquipped} equipped)</color>";
    }

    // Si el clic cayó sobre un link de arma, la equipa o la desequipa
    private void HandleClick(Vector2 screenPosition)
    {
        if (itemListText == null) return;

        Canvas canvas = itemListText.canvas;
        UnityEngine.Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        int link = TMP_TextUtilities.FindIntersectingLink(itemListText, screenPosition, uiCamera);
        if (link < 0) return;

        string id = itemListText.textInfo.linkInfo[link].GetLinkID();
        if (!int.TryParse(id, out int index) || index < 0 || index >= inventory.Count) return;

        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat == null || !(inventory[index] is WeaponSO weapon)) return;

        if (combat.IsEquipped(weapon))
            combat.Unequip(weapon);
        else
            combat.EquipToSlot(weapon);
        UpdateUI();
    }
}
