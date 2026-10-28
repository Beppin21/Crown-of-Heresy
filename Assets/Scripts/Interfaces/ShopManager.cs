using NUnit.Framework.Interfaces;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopManager : MonoBehaviour
{
    [Header("Subpaneles de la Tienda")]
    [SerializeField] private GameObject mainView; // Contenedor del saludo y botones Buy/Sell
    [SerializeField] private GameObject listView; // Contenedor de la lista de ítems

    [Header("Elementos de la Lista")]
    [SerializeField] private TextMeshProUGUI listTitleText;
    [SerializeField] private TextMeshProUGUI playerMoneyText;
    [SerializeField] private Transform itemContainer;       // Objeto con Vertical Layout Group
    [SerializeField] private GameObject itemButtonPrefab;   // Botón template para instanciar

    [Header("Stock de la Tienda")]
    public static List<LootData> staticShopStock;
    private bool isSelling = false;

    private void Awake()
    {
        // Se genera una sola vez en memoria para toda la partida
        if (staticShopStock == null)
        {
            staticShopStock = new List<LootData>()
            {
                LootData.Create("Health Potion", "It lets you recover some health, or thats the idea on some later version", 25),
                LootData.Create("Weird Key", "It lets you open a door somewhere (this actually works)", 50),
                LootData.Create("Cool Sword", "A weapon, that you cant equip, deals a lot of damage so its a shame it doesnt work", 80),
                LootData.Create("Hat", "idk i dont have more ideas", 15)
            };
        }
    }


    private void OnEnable()
    {
        ShowMainView();
    }

    // Vuelve a la pantalla de saludo inicial
    public void ShowMainView()
    {
        mainView.SetActive(true);
        listView.SetActive(false);
    }

    // Llamado por el botón BUY
    public void OpenBuyMenu()
    {
        isSelling = false;
        mainView.SetActive(false);
        listView.SetActive(true);
        listTitleText.text = "Buy Objects";
        RefreshList();
    }

    // Llamado por el botón SELL
    public void OpenSellMenu()
    {
        isSelling = true;
        mainView.SetActive(false);
        listView.SetActive(true);
        listTitleText.text = "Sell Objects";
        RefreshList();
    }

    private void RefreshList()
    {
        playerMoneyText.text = $"Your Money: ${InventoryUI.money}";

        // Limpia botones anteriores
        foreach (Transform child in itemContainer)
        {
            Destroy(child.gameObject);
        }

        if (isSelling)
        {
            // Muestra los ítems que tiene el jugador en el inventario
            for (int i = 0; i < InventoryUI.inventory.Count; i++)
            {
                LootData item = InventoryUI.inventory[i];
                int index = i; // Captura de índice para la acción

                CreateItemButton($"{item.ItemName} | Sell: +${item.Value}", () => SellItem(index));
            }
        }
        else
        {
            // Muestra el catálogo de la tienda
            foreach (LootData item in staticShopStock)
            {
                LootData currentItem = item;
                CreateItemButton($"{currentItem.ItemName} | Buy: -${currentItem.Value}", () => BuyItem(currentItem));
            }
        }
    }

    private void CreateItemButton(string text, System.Action onClickAction)
    {
        GameObject btnObj = Instantiate(itemButtonPrefab, itemContainer);
        btnObj.GetComponentInChildren<TextMeshProUGUI>().text = text;
        btnObj.GetComponent<Button>().onClick.AddListener(() => onClickAction());
    }

    private void BuyItem(LootData item)
    {
        if (InventoryUI.money >= item.Value)
        {
            InventoryUI.money -= item.Value;
            InventoryUI.inventory.Add(item);
            Debug.Log($"Comprado: {item.ItemName}. Your Money: ${InventoryUI.money}");
            staticShopStock.Remove(item);
            RefreshList();
        }
        else
        {
            Debug.LogWarning("No tenés suficiente dinero.");
        }
    }

    private void SellItem(int itemIndex)
    {
        if (itemIndex >= 0 && itemIndex < InventoryUI.inventory.Count)
        {
            LootData item = InventoryUI.inventory[itemIndex];
            InventoryUI.money += item.Value;
            InventoryUI.inventory.RemoveAt(itemIndex);
            Debug.Log($"Vendido: {item.ItemName}. Your Money: ${InventoryUI.money}");
            RefreshList();
        }
    }
}