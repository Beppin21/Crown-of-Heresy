using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    // Colores del inventario (los de texto van como etiquetas <color> de TextMeshPro)
    private static readonly Color PanelColor = new Color(0.07f, 0.06f, 0.07f, 0.93f);
    private static readonly Color BorderColor = new Color(0.6f, 0.47f, 0.24f, 0.9f);
    private static readonly Color TitleColor = new Color(0.93f, 0.87f, 0.73f, 1f);
    private static readonly Color GoldColor = new Color(0.91f, 0.77f, 0.42f, 1f);
    private const string HeaderColor = "#C9A45C";
    private const string NameColor = "#F2E6C8";
    private const string StatsColor = "#B5AC9C";
    private const string DescriptionColor = "#8F887B";
    private const string LinkColor = "#E8C46A";
    private const string InHandColor = "#86D27E";
    private const string MutedColor = "#6F6A62";

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
        {
            StylePanel();
            inventoryPanel.SetActive(false);
        }

        // Si venimos de otra escena y ya teníamos cosas, actualiza el texto
        UpdateUI();
    }

    // Le da al panel del Canvas un estilo oscuro con borde dorado y tiñe sus textos (título y
    // plata) con colores que combinan. Se hace por código para no tener que tocar el Canvas.
    private void StylePanel()
    {
        Image background = inventoryPanel.GetComponent<Image>();
        if (background != null)
        {
            background.color = PanelColor;
            Outline outline = inventoryPanel.GetComponent<Outline>();
            if (outline == null)
                outline = inventoryPanel.AddComponent<Outline>();
            outline.effectColor = BorderColor;
            outline.effectDistance = new Vector2(3f, -3f);
        }

        foreach (TextMeshProUGUI text in inventoryPanel.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text == itemListText) continue;
            text.color = text == moneyDisplay ? GoldColor : TitleColor;
        }
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
            itemListText.text = $"<color={DescriptionColor}><i>The inventory is empty</i></color>";
            return;
        }

        PlayerCombat combat = GetComponent<PlayerCombat>();
        StringBuilder text = new StringBuilder();

        // Armas: una por línea, con su tecla, si está en la mano y el link para equipar/desequipar
        // (el id del link es su índice en el inventario)
        bool hasWeapons = false;
        for (int i = 0; i < inventory.Count; i++)
        {
            if (!(inventory[i] is WeaponSO weapon) || combat == null) continue;
            if (!hasWeapons)
            {
                text.Append(Header("WEAPONS", $"keys 1-{PlayerCombat.MaxEquipped} to switch"));
                hasWeapons = true;
            }
            AppendWeapon(text, combat, weapon, i);
        }

        // Objetos: los repetidos van juntos ("Magic Cube x2"), en el orden en que se consiguieron
        List<LootData> items = new List<LootData>();
        Dictionary<string, int> counts = new Dictionary<string, int>();
        foreach (LootData item in inventory)
        {
            if (item is WeaponSO && combat != null) continue;
            if (counts.ContainsKey(item.ItemName))
            {
                counts[item.ItemName]++;
                continue;
            }
            counts[item.ItemName] = 1;
            items.Add(item);
        }

        if (items.Count > 0)
        {
            if (hasWeapons) text.Append('\n');
            text.Append(Header("ITEMS", null));
            foreach (LootData item in items)
            {
                int count = counts[item.ItemName];
                text.Append($"<b><color={NameColor}>{item.ItemName}</color></b>");
                if (count > 1)
                    text.Append($" <color={LinkColor}>x{count}</color>");
                text.Append($"\n<indent=3%><size=85%><color={StatsColor}>${item.Value}{(count > 1 ? " each" : "")}</color>\n");
                text.Append($"<color={DescriptionColor}><i>{item.Description}</i></color></size></indent>\n\n");
            }
        }

        itemListText.text = text.ToString();
    }

    // Título de sección, con una nota más chica y apagada al lado
    private static string Header(string title, string note)
    {
        string header = $"<size=90%><b><color={HeaderColor}>{title}</color></b></size>";
        if (note != null)
            header += $"   <size=75%><color={MutedColor}>{note}</color></size>";
        return header + "\n";
    }

    // Un arma: "[1] Heavy Sword  in hand   Unequip", y abajo sus números y su descripción
    private static void AppendWeapon(StringBuilder text, PlayerCombat combat, WeaponSO weapon, int index)
    {
        int key = combat.GetSlotKey(weapon);
        text.Append(key > 0 ? $"<color={LinkColor}>[{key}]</color> " : $"<color={MutedColor}>[ ]</color> ");
        text.Append($"<b><color={NameColor}>{weapon.ItemName}</color></b>");
        if (combat.EquippedWeapon == weapon)
            text.Append($"  <size=80%><color={InHandColor}>in hand</color></size>");

        if (key > 0)
            text.Append($"   <size=85%><link=\"{index}\"><color={LinkColor}><u>Unequip</u></color></link></size>");
        else if (combat.HasFreeSlot)
            text.Append($"   <size=85%><link=\"{index}\"><color={LinkColor}><u>Equip</u></color></link></size>");
        else
            text.Append($"   <size=80%><color={MutedColor}>slots full</color></size>");

        text.Append($"\n<indent=3%><size=85%><color={StatsColor}>Damage {weapon.Damage}   Weight {weapon.Weight}   ${weapon.Value}</color>\n");
        text.Append($"<color={DescriptionColor}><i>{weapon.Description}</i></color></size></indent>\n\n");
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
