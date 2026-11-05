using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    // Colores del inventario (los de texto van como etiquetas <color> de TextMeshPro). La tienda
    // (ShopManager) usa los mismos.
    public static readonly Color PanelColor = new Color(0.07f, 0.06f, 0.07f, 0.93f);
    public static readonly Color BorderColor = new Color(0.6f, 0.47f, 0.24f, 0.9f);
    public static readonly Color TitleColor = new Color(0.93f, 0.87f, 0.73f, 1f);
    public static readonly Color GoldColor = new Color(0.91f, 0.77f, 0.42f, 1f);
    public const string HeaderColor = "#C9A45C";
    public const string NameColor = "#F2E6C8";
    public const string StatsColor = "#B5AC9C";
    public const string DescriptionColor = "#8F887B";
    public const string LinkColor = "#E8C46A";
    public const string InHandColor = "#86D27E";
    public const string MutedColor = "#6F6A62";

    [Header("Referencias de UI")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private TextMeshProUGUI itemListText;
    [SerializeField] private TextMeshProUGUI moneyDisplay;

    // Lista desplazable (ver SetupScrolling)
    private const float ScrollbarWidth = 8f;
    private const float PanelMargin = 24f;  // la lista no se acerca más que esto al borde del panel
    private const float ScrollStep = 90f;   // cuánto baja la lista por cada paso de la rueda
    private ScrollRect scroll;
    private RectTransform viewport;

    // Lista en memoria donde se guardan los datos de los objetos recogidos
    public static readonly List<LootData> inventory = new List<LootData>();
    public static int money = 0;
    private bool isOpen = false;

    public bool IsOpen => isOpen;

    // Si el inventario está abierto, sin tener que buscar el componente (la tienda lo usa para que
    // F9 lo atienda la ventana de arriba)
    public static bool OpenNow { get; private set; }

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

        SetupScrolling();
    }

    // Con muchos objetos el texto se salía del panel por abajo. Se lo mete en una ventana recortada
    // (lo que ocupaba el texto, sin pasarse del panel) que se desplaza con la rueda del mouse, con
    // una barra fina y dorada a la derecha que aparece solo cuando no entra todo. El texto crece
    // con lo que tiene escrito (ContentSizeFitter). Se arma por código para no tocar el Canvas.
    private void SetupScrolling()
    {
        if (itemListText == null || scroll != null) return;
        RectTransform panel = inventoryPanel.transform as RectTransform;
        RectTransform text = itemListText.rectTransform;
        if (panel == null || text.parent == null) return;

        // Lo que ocupa el texto, recortado al panel con un margen, en coordenadas del panel
        Canvas.ForceUpdateCanvases();
        Vector3[] corners = new Vector3[4];
        text.GetWorldCorners(corners);
        Rect area = panel.rect;
        Vector2 min = Vector2.Max(panel.InverseTransformPoint(corners[0]), area.min + Vector2.one * PanelMargin);
        Vector2 max = Vector2.Min(panel.InverseTransformPoint(corners[2]), area.max - Vector2.one * PanelMargin);
        if (max.x - min.x < 50f || max.y - min.y < 50f) return; // sin lugar razonable: queda como estaba

        GameObject viewObject = new GameObject("Lista", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        viewport = (RectTransform)viewObject.transform;
        viewport.SetParent(panel, false);
        viewport.SetSiblingIndex(text.GetSiblingIndex());
        viewport.anchorMin = new Vector2((min.x - area.xMin) / area.width, (min.y - area.yMin) / area.height);
        viewport.anchorMax = new Vector2((max.x - area.xMin) / area.width, (max.y - area.yMin) / area.height);
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewObject.GetComponent<Image>().color = Color.clear; // invisible: recibe el arrastre del mouse

        // El texto arriba de todo, a lo ancho (menos el lugar de la barra) y del alto de lo que tiene
        float gutter = ScrollbarWidth + 12f;
        text.SetParent(viewport, false);
        text.anchorMin = new Vector2(0f, 1f);
        text.anchorMax = new Vector2(1f, 1f);
        text.pivot = new Vector2(0.5f, 1f);
        text.sizeDelta = new Vector2(-gutter, text.sizeDelta.y);
        text.anchoredPosition = new Vector2(-gutter * 0.5f, 0f);
        ContentSizeFitter fitter = itemListText.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = viewObject.GetComponent<ScrollRect>();
        scroll.content = text;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = false;
        scroll.scrollSensitivity = 0f; // la rueda la lee Update (funciona aunque la escena no tenga EventSystem)

        GameObject barObject = new GameObject("Barra", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        RectTransform bar = (RectTransform)barObject.transform;
        bar.SetParent(viewport, false);
        bar.anchorMin = new Vector2(1f, 0f);
        bar.anchorMax = Vector2.one;
        bar.pivot = new Vector2(1f, 0.5f);
        bar.sizeDelta = new Vector2(ScrollbarWidth, 0f);
        bar.anchoredPosition = Vector2.zero;
        barObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);

        RectTransform track = new GameObject("Recorrido", typeof(RectTransform)).GetComponent<RectTransform>();
        track.SetParent(bar, false);
        track.anchorMin = Vector2.zero;
        track.anchorMax = Vector2.one;
        track.offsetMin = Vector2.zero;
        track.offsetMax = Vector2.zero;

        GameObject handleObject = new GameObject("Manija", typeof(RectTransform), typeof(Image));
        RectTransform handle = (RectTransform)handleObject.transform;
        handle.SetParent(track, false);
        handle.offsetMin = Vector2.zero;
        handle.offsetMax = Vector2.zero;
        Image handleImage = handleObject.GetComponent<Image>();
        handleImage.color = new Color(GoldColor.r, GoldColor.g, GoldColor.b, 0.85f);

        Scrollbar scrollbar = barObject.GetComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    // Un paso de la rueda: arriba (+) sube la lista, abajo (-) la baja
    private void ScrollList(float wheel)
    {
        if (scroll == null || wheel == 0f) return;
        float overflow = scroll.content.rect.height - viewport.rect.height;
        if (overflow <= 0f) return;
        scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + Mathf.Sign(wheel) * ScrollStep / overflow);
    }

    private void Update()
    {
        // En pausa Tab no hace nada: al cerrarse, el inventario traba el mouse y el menú de pausa
        // quedaría sin cursor
        if (!Pause.Pausado && InputSystem.actions.FindAction("Inventory").WasPressedThisFrame())
        {
            ToggleInventory();
        }

#if UNITY_EDITOR
        // F9 con el inventario abierto, solo en el Editor: todos los objetos del juego, para probar
        if (isOpen && Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
            GiveAllItemsForTesting();
#endif

        // Clic sobre "Equip" / "Unequip" de un arma (son links del texto de la lista)
        if (isOpen && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            HandleClick(Mouse.current.position.ReadValue());

        // Rueda del mouse: desplaza la lista cuando no entra en el panel
        if (isOpen && Mouse.current != null)
            ScrollList(Mouse.current.scroll.ReadValue().y);
    }

    // El toggle lo dispara PlayerController.OnInventary (acción "inventary" del Input System,
    // bindeada a Tab), no una lectura directa acá, para no togglear dos veces por el mismo Tab.
    public void ToggleInventory()
    {
        isOpen = !isOpen;
        OpenNow = isOpen;
        inventoryPanel.SetActive(isOpen);
        if (isOpen)
            inventoryPanel.transform.SetAsLastSibling(); // arriba de la tienda, si está abierta en el mismo Canvas

        // Control del cursor: si el inventario está abierto liberamos el mouse, si no lo bloqueamos
        Cursor.lockState = isOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = isOpen;

        if (isOpen)
        {
            UpdateUI();
            if (scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 1f; // arranca desde arriba
            }
        }
    }

    // Al cambiar de escena con el inventario abierto, que no quede marcado como abierto
    private void OnDisable()
    {
        if (isOpen) OpenNow = false;
    }

#if UNITY_EDITOR
    // Todas las armas que falten (una de cada) y uno más de cada objeto que no es arma (la llave, el
    // Magic Cube...). Busca los LootData del proyecto, así que entra cualquiera que se agregue después.
    private void GiveAllItemsForTesting()
    {
        List<string> added = new List<string>();
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:LootData", new[] { "Assets" }))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/_Recovery/")) continue;
            LootData item = UnityEditor.AssetDatabase.LoadAssetAtPath<LootData>(path);
            if (item == null || (item is WeaponSO && inventory.Contains(item))) continue;
            inventory.Add(item);
            added.Add(item.ItemName);
        }

        Debug.Log(added.Count > 0
            ? $"[InventoryUI] Editor (F9): {string.Join(", ", added)}."
            : "[InventoryUI] Editor (F9): no había nada nuevo para agregar.");
        UpdateUI();
    }
#endif

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
        // La parte de la lista que quedó fuera de la ventana no se ve: ahí no se puede hacer clic
        if (viewport != null && !RectTransformUtility.RectangleContainsScreenPoint(viewport, screenPosition, uiCamera)) return;
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
