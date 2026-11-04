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
    [Tooltip("Ítems armados como asset que vende la tienda además de los fijos, por ejemplo armas " +
             "(WeaponSO). Se suman al stock la primera vez que se abre la tienda en la partida.")]
    [SerializeField] private List<LootData> itemsForSale = new List<LootData>();

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
                LootData.Create("Hat", "idk i dont have more ideas", 15)
            };

            foreach (LootData item in itemsForSale)
            {
                if (item != null)
                    staticShopStock.Add(item);
            }
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

        PlayerCombat combat = FindAnyObjectByType<PlayerCombat>();

        if (isSelling)
        {
            // Muestra los ítems que tiene el jugador en el inventario (las armas equipadas no se
            // venden: primero hay que desequiparlas desde el inventario)
            for (int i = 0; i < InventoryUI.inventory.Count; i++)
            {
                LootData item = InventoryUI.inventory[i];
                int index = i; // Captura de índice para la acción
                if (item is WeaponSO weapon && combat != null && combat.IsEquipped(weapon)) continue;

                CreateItemButton($"{GetItemLabel(item)} | Sell: +${item.Value}", () => SellItem(index));
            }
        }
        else
        {
            // Muestra el catálogo de la tienda, sin las armas que el jugador ya tiene
            foreach (LootData item in staticShopStock)
            {
                LootData currentItem = item;
                if (currentItem is WeaponSO && InventoryUI.inventory.Contains(currentItem)) continue;

                CreateItemButton($"{GetItemLabel(currentItem)} | Buy: -${currentItem.Value}", () => BuyItem(currentItem));
            }
        }
    }

    // Las armas muestran también su daño y su peso
    private static string GetItemLabel(LootData item)
    {
        if (item is WeaponSO weapon)
            return $"{weapon.ItemName} (Dmg {weapon.Damage}, Wgt {weapon.Weight})";
        return item.ItemName;
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

            // Un arma comprada se equipa si queda un lugar libre (si no, se equipa desde el inventario)
            if (item is WeaponSO weapon)
            {
                PlayerCombat combat = FindAnyObjectByType<PlayerCombat>();
                if (combat != null && combat.HasFreeSlot)
                    combat.EquipToSlot(weapon);
            }

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
