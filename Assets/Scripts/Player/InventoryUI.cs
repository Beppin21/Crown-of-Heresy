using NUnit.Framework.Interfaces;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    [Header("Referencias de UI")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private TextMeshProUGUI itemListText;
    [SerializeField] private TextMeshProUGUI moneyDisplay;

    // Lista en memoria donde se guardan los datos de los objetos recogidos
    public static readonly List<LootData> inventory = new List<LootData>();
    public static int money = 0; 
    private bool isOpen = false;

    private void Start()
    {
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);

        // Si venimos de otra escena y ya teníamos cosas, actualiza el texto
        UpdateUI();
    }

    private void Update()
    {
        // Detecta la tecla TAB con el New Input System
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            ToggleInventory();
        }
    }

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

        itemListText.text = "";
        foreach (LootData item in inventory)
        {
            itemListText.text += $"• {item.ItemName} - ${item.Value}\n  <i>{item.Description}</i>\n\n";
        }
    }
}