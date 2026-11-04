using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Tienda del Town. Va en el ShopPanel, que abre y cierra ShopTrigger.
//
// La primera vez que se abre arma su ventana por código, con el estilo del inventario (fondo oscuro
// y borde dorado), y esconde lo que el panel traía en la escena: así no hay que tocar el Canvas.
// Tiene dos pestañas, comprar y vender, con una fila por ítem (los repetidos van juntos, "x3"). Abre
// sin ninguna elegida, para dejar lugar a los diálogos del vendedor.
//
// El stock es static, como el inventario, así que dura toda la partida. Arranca con lo que dice
// Assets/Resources/ShopCatalog.asset. Lo que se compra sale del stock y lo que se vende entra, salvo
// el loot común de los enemigos (LootData.IsBasicLoot), para poder volver a comprarlo.
public class ShopManager : MonoBehaviour
{
    // Medidas en unidades del Canvas (referencia 1920x1080)
    private const float WindowWidth = 1320f;
    private const float WindowHeight = 900f;
    private const float Margin = 48f;          // margen interno de la ventana
    private const float ListTop = 206f;        // dónde empieza la lista, desde arriba de la ventana
    private const float ListBottom = 100f;     // y dónde termina, desde abajo
    private const float RowHeight = 114f;      // 5 filas entran justas en la lista (900 - 206 - 100 = 594)
    private const float RowSpacing = 6f;
    private const float RowActionWidth = 330f; // lo que ocupan el precio y el botón a la derecha de cada fila
    private const float FeedbackSeconds = 3f;  // cuánto queda el aviso de abajo ("Bought ...")
#if UNITY_EDITOR
    private const int EditorMoneyCheat = 100;  // F9 en el Editor, para probar sin juntar plata
#endif

    // Colores: los del inventario (InventoryUI) y algunos propios
    private static readonly Color VeilColor = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color DividerColor = new Color(0.6f, 0.47f, 0.24f, 0.45f);
    private static readonly Color FaintDividerColor = new Color(0.6f, 0.47f, 0.24f, 0.2f);
    private static readonly Color FrameColor = new Color(0.6f, 0.47f, 0.24f, 0.3f);
    private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.035f);
    private static readonly Color RowHoverColor = new Color(0.91f, 0.77f, 0.42f, 0.1f);
    private static readonly Color ButtonColor = new Color(0.16f, 0.12f, 0.08f, 1f);
    private static readonly Color ButtonHoverColor = new Color(0.33f, 0.25f, 0.12f, 1f);
    private static readonly Color ButtonPressedColor = new Color(0.46f, 0.35f, 0.16f, 1f);
    private static readonly Color ButtonDisabledColor = new Color(0.1f, 0.09f, 0.09f, 1f);
    private static readonly Color DisabledBorderColor = new Color(0.32f, 0.27f, 0.2f, 0.7f);
    private static readonly Color NameColor = new Color(0.95f, 0.9f, 0.78f, 1f);        // InventoryUI.NameColor
    private static readonly Color TextColor = new Color(0.71f, 0.67f, 0.61f, 1f);        // InventoryUI.StatsColor
    private static readonly Color DescriptionColor = new Color(0.56f, 0.53f, 0.48f, 1f); // InventoryUI.DescriptionColor
    private static readonly Color MutedColor = new Color(0.44f, 0.42f, 0.38f, 1f);       // InventoryUI.MutedColor
    private static readonly Color SuccessColor = new Color(0.53f, 0.82f, 0.49f, 1f);     // InventoryUI.InHandColor
    private static readonly Color ErrorColor = new Color(0.9f, 0.45f, 0.38f, 1f);
    private static readonly Color SpecialItemColor = new Color(0.64f, 0.53f, 0.88f, 1f); // la llave y lo que no es loot
    private const string SpecialItemHex = "#A387E0";

    public static List<LootData> staticShopStock;

    private TMP_FontAsset titleFont;
    private PlayerCombat combat;
    private View view;
    private int shownState;    // para notar si el inventario cambió con la tienda abierta
    private float feedbackUntil;

    // Partes de la ventana
    private RectTransform window;
    private RectTransform rows;
    private TextMeshProUGUI moneyText;
    private TextMeshProUGUI emptyText;
    private TextMeshProUGUI feedbackText;
    private Tab buyTab;
    private Tab sellTab;

    // Qué muestra la ventana: solo las pestañas (recién abierta) o una de las dos listas
    private enum View { Main, Buy, Sell }

    private sealed class Tab
    {
        public TextMeshProUGUI label;
        public Image underline;
    }

    private void Awake()
    {
        ShopCatalog catalog = ShopCatalog.Load();
        titleFont = catalog != null ? catalog.TitleFont : null;

        // El stock se arma una sola vez por partida
        if (staticShopStock == null)
        {
            staticShopStock = new List<LootData>();
            if (catalog != null)
            {
                foreach (LootData item in catalog.StartingStock)
                {
                    if (item != null)
                        staticShopStock.Add(item);
                }
            }
        }

        BuildWindow();
    }

    private void OnEnable()
    {
        combat = FindAnyObjectByType<PlayerCombat>();
        transform.SetAsLastSibling(); // arriba del resto del Canvas (las barras de vida y estamina)
        FitWindow();
        feedbackText.text = "";
        ShowView(View.Main); // sin pestaña elegida
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
        {
            InventoryUI.money += EditorMoneyCheat;
            ShowFeedback($"Editor: +${EditorMoneyCheat}", InventoryUI.GoldColor);
        }
#endif

        // Si algo cambió con la tienda abierta (por ejemplo, se desequipó un arma desde el
        // inventario), se rearma la lista
        if (InventoryState() != shownState)
            Refresh();

        // El aviso de abajo se desvanece solo
        if (feedbackText.text.Length > 0)
        {
            float left = feedbackUntil - Time.unscaledTime;
            if (left <= 0f)
                feedbackText.text = "";
            else
                feedbackText.alpha = Mathf.Clamp01(left / 0.6f);
        }
    }

    // Las pestañas BUY y SELL llaman a OpenBuyMenu y OpenSellMenu: los diálogos del vendedor se pueden
    // enganchar acá. ShowMainView vuelve a la ventana sin pestaña (lo usaba el botón BACK viejo).
    public void ShowMainView() => ShowView(View.Main);
    public void OpenBuyMenu() => ShowView(View.Buy);
    public void OpenSellMenu() => ShowView(View.Sell);

    private void ShowView(View newView)
    {
        view = newView;
        StyleTab(buyTab, view == View.Buy);
        StyleTab(sellTab, view == View.Sell);
        rows.anchoredPosition = Vector2.zero; // la lista arranca desde arriba
        Refresh();
    }

    // La pestaña elegida va en dorado y subrayada. Sin ninguna elegida las dos se ven claras, porque son
    // el menú; con una elegida, la otra se apaga.
    private void StyleTab(Tab tab, bool active)
    {
        tab.label.color = active ? InventoryUI.GoldColor : view == View.Main ? TextColor : MutedColor;
        tab.underline.enabled = active;
    }

    // ---------------------------------------------------------------
    // Lista
    // ---------------------------------------------------------------

    private void Refresh()
    {
        shownState = InventoryState();
        moneyText.text = $"${InventoryUI.money}";

        foreach (Transform row in rows)
        {
            row.gameObject.SetActive(false); // que no ocupe lugar hasta que se borre
            Destroy(row.gameObject);
        }

        int count = view == View.Buy ? AddBuyRows() : view == View.Sell ? AddSellRows() : 0;
        emptyText.text = view == View.Sell ? "You have nothing to sell." : "Sold out. Come back when you have something to sell me.";
        emptyText.gameObject.SetActive(view != View.Main && count == 0);
    }

    // Lo que vende la tienda, sin las armas que el jugador ya tiene
    private int AddBuyRows()
    {
        int count = 0;
        foreach (KeyValuePair<LootData, int> group in GroupByName(staticShopStock, item => item is WeaponSO && InventoryUI.inventory.Contains(item)))
        {
            LootData item = group.Key;
            bool affordable = InventoryUI.money >= item.Value;
            AddRow(item, group.Value, Info(item, false), $"${item.Value}", affordable ? InventoryUI.GoldColor : ErrorColor,
                   "BUY", true, () => Buy(item));
            count++;
        }
        return count;
    }

    // Lo que tiene el jugador: primero las armas (las equipadas se ven pero no se venden) y después
    // los objetos
    private int AddSellRows()
    {
        int count = 0;
        foreach (bool weapons in new[] { true, false })
        {
            foreach (KeyValuePair<LootData, int> group in GroupByName(InventoryUI.inventory, item => (item is WeaponSO) != weapons))
            {
                LootData item = group.Key;
                bool equipped = IsEquipped(item);
                AddRow(item, group.Value, Info(item, true), $"+${item.Value}", equipped ? MutedColor : InventoryUI.GoldColor,
                       "SELL", !equipped, () => Sell(item));
                count++;
            }
        }
        return count;
    }

    // Junta los ítems repetidos por nombre, en el orden en que aparecen: (el primero, cuántos hay)
    private static List<KeyValuePair<LootData, int>> GroupByName(List<LootData> items, System.Predicate<LootData> skip)
    {
        List<KeyValuePair<LootData, int>> groups = new List<KeyValuePair<LootData, int>>();
        Dictionary<string, int> indexByName = new Dictionary<string, int>();
        foreach (LootData item in items)
        {
            if (item == null || skip(item)) continue;

            string key = item.ItemName ?? "";
            if (indexByName.TryGetValue(key, out int index))
            {
                groups[index] = new KeyValuePair<LootData, int>(groups[index].Key, groups[index].Value + 1);
                continue;
            }
            indexByName[key] = groups.Count;
            groups.Add(new KeyValuePair<LootData, int>(item, 1));
        }
        return groups;
    }

    private bool IsEquipped(LootData item) => item is WeaponSO weapon && combat != null && combat.IsEquipped(weapon);

    // Línea de datos de la fila: los números del arma o qué clase de objeto es
    private string Info(LootData item, bool selling)
    {
        if (item is WeaponSO weapon)
        {
            if (selling && IsEquipped(item))
                return $"<color={InventoryUI.InHandColor}>EQUIPPED</color>     " +
                       $"<color={InventoryUI.MutedColor}>unequip it in the inventory (Tab) to sell it</color>";

            string type = weapon.WeaponType != null && !string.IsNullOrEmpty(weapon.WeaponType.TypeName)
                ? weapon.WeaponType.TypeName.ToUpperInvariant()
                : "WEAPON";
            return $"<color={InventoryUI.HeaderColor}>{type}</color>     {Stat("Damage", Number(weapon.Damage))}     " +
                   $"{Stat("Weight", Number(weapon.Weight))}     {Stat("Speed", "x" + Number(weapon.AttackSpeed))}     " +
                   $"{Stat("Stamina", "x" + Number(weapon.StaminaMultiplier))}";
        }

        if (item.IsBasicLoot)
            return $"<color={InventoryUI.MutedColor}>ENEMY LOOT</color>";
        return selling
            ? $"<color={SpecialItemHex}>SPECIAL ITEM</color>     <color={InventoryUI.MutedColor}>you can buy it back later</color>"
            : $"<color={SpecialItemHex}>SPECIAL ITEM</color>";
    }

    private static string Stat(string label, string value) => $"<color={InventoryUI.MutedColor}>{label}</color> {value}";

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // Una marca de color a la izquierda de cada fila dice qué es: dorado las armas, violeta los
    // objetos especiales (la llave) y gris el loot común
    private static Color AccentColor(LootData item)
    {
        if (item is WeaponSO) return InventoryUI.GoldColor;
        return item.IsBasicLoot ? MutedColor : SpecialItemColor;
    }

    // ---------------------------------------------------------------
    // Comprar y vender
    // ---------------------------------------------------------------

    private void Buy(LootData item)
    {
        if (!staticShopStock.Contains(item)) return;
        if (InventoryUI.money < item.Value)
        {
            ShowFeedback($"Not enough money: you need ${item.Value - InventoryUI.money} more.", ErrorColor);
            return;
        }

        InventoryUI.money -= item.Value;
        staticShopStock.Remove(item);
        InventoryUI.inventory.Add(item);
        Debug.Log($"[Tienda] Compraste '{item.ItemName}' por ${item.Value}. Te quedan ${InventoryUI.money}.");

        // Un arma comprada se equipa si queda un lugar libre (si no, se equipa desde el inventario)
        string message = $"Bought {item.ItemName}.";
        if (item is WeaponSO weapon && combat != null)
        {
            message += combat.HasFreeSlot && combat.EquipToSlot(weapon)
                ? $" Equipped on key {combat.GetSlotKey(weapon)}."
                : " Equip it from the inventory (Tab).";
        }
        ShowFeedback(message, SuccessColor);
        RefreshInventoryPanel();
        Refresh();
    }

    private void Sell(LootData item)
    {
        // De los repetidos se vende uno solo, el último que se consiguió. Las armas equipadas no se venden.
        string itemName = item.ItemName;
        int index = InventoryUI.inventory.FindLastIndex(owned => owned != null && owned.ItemName == itemName && !IsEquipped(owned));
        if (index < 0)
        {
            ShowFeedback("Unequip it in the inventory (Tab) to sell it.", ErrorColor);
            return;
        }

        LootData sold = InventoryUI.inventory[index];
        InventoryUI.inventory.RemoveAt(index);
        InventoryUI.money += sold.Value;

        // Lo que no es loot común vuelve al stock para poder recomprarlo (cada arma, una sola vez)
        bool restocked = !sold.IsBasicLoot && !(sold is WeaponSO && staticShopStock.Contains(sold));
        if (restocked)
            staticShopStock.Add(sold);

        Debug.Log($"[Tienda] Vendiste '{sold.ItemName}' por ${sold.Value}. Tenés ${InventoryUI.money}." +
                  (restocked ? " Pasó al stock de la tienda." : ""));
        ShowFeedback($"Sold {sold.ItemName} for ${sold.Value}." + (restocked ? " You can buy it back." : ""), InventoryUI.GoldColor);
        RefreshInventoryPanel();
        Refresh();
    }

    // Si el inventario (Tab) está abierto, que muestre lo que se compró o vendió
    private void RefreshInventoryPanel()
    {
        InventoryUI inventory = combat != null ? combat.GetComponent<InventoryUI>() : FindAnyObjectByType<InventoryUI>();
        if (inventory != null)
            inventory.RefreshIfOpen();
    }

    // Plata, cantidad de ítems, stock y armas equipadas: si algo de eso cambia, hay que rearmar la lista
    private int InventoryState()
    {
        unchecked
        {
            int state = InventoryUI.money;
            state = state * 31 + InventoryUI.inventory.Count;
            state = state * 31 + staticShopStock.Count;
            for (int i = 0; i < InventoryUI.inventory.Count; i++)
            {
                if (IsEquipped(InventoryUI.inventory[i]))
                    state = state * 31 + i * 4 + combat.GetSlotKey((WeaponSO)InventoryUI.inventory[i]);
            }
            return state;
        }
    }

    private void ShowFeedback(string message, Color color)
    {
        feedbackText.text = message;
        feedbackText.color = color;
        feedbackUntil = Time.unscaledTime + FeedbackSeconds;
    }

    // Botón "Leave": cierra igual que Escape
    private void Close()
    {
        if (ShopTrigger.Current != null)
        {
            ShopTrigger.Current.CloseShop();
            return;
        }
        gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // ---------------------------------------------------------------
    // Armado de la ventana
    // ---------------------------------------------------------------

    private void BuildWindow()
    {
        // Lo que el panel traía en la escena (saludo, botones BUY/SELL/BACK y la lista vieja) queda escondido
        foreach (Transform child in transform)
            child.gameObject.SetActive(false);

        // El panel pasa a ser un velo oscuro sobre toda la pantalla, con la ventana en el medio
        Stretch((RectTransform)transform, 0f, 0f, 0f, 0f);
        Image veil = GetComponent<Image>();
        if (veil == null)
            veil = gameObject.AddComponent<Image>();
        veil.sprite = null;
        veil.color = VeilColor;
        veil.raycastTarget = true; // los clics fuera de la ventana no pasan al juego

        Image background = CreateImage("Ventana", transform, InventoryUI.PanelColor);
        window = background.rectTransform;
        Place(window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
              new Vector2(WindowWidth, WindowHeight));
        CreateFrame(window, "Borde", 0f, 3f, InventoryUI.BorderColor, false);
        CreateFrame(window, "Filete", 10f, 1f, FrameColor, true);

        BuildHeader();
        BuildTabs();
        BuildList();
        BuildFooter();
    }

    // La ventana mide WindowWidth x WindowHeight y se achica si la pantalla no da (por ejemplo, una ultra ancha)
    private void FitWindow()
    {
        Rect area = ((RectTransform)transform).rect;
        float width = area.width > 0f ? Mathf.Min(WindowWidth, area.width - 60f) : WindowWidth;
        float height = area.height > 0f ? Mathf.Min(WindowHeight, area.height - 60f) : WindowHeight;
        window.sizeDelta = new Vector2(width, height);
    }

    // El título a la izquierda y la plata a la derecha, centrados en la franja de arriba
    private void BuildHeader()
    {
        TextMeshProUGUI title = CreateText("Título", window, "SHOP", 62f, InventoryUI.TitleColor, true, TextAlignmentOptions.Left);
        title.characterSpacing = 14f;
        PlaceTop(title.rectTransform, Margin, -36f, 460f, 72f);

        TextMeshProUGUI moneyLabel = CreateText("Plata (título)", window, "YOUR MONEY", 17f, MutedColor, false, TextAlignmentOptions.Right);
        moneyLabel.characterSpacing = 6f;
        PlaceTopRight(moneyLabel.rectTransform, Margin, -40f, 400f, 22f);

        moneyText = CreateText("Plata", window, "", 54f, InventoryUI.GoldColor, true, TextAlignmentOptions.Right);
        PlaceTopRight(moneyText.rectTransform, Margin, -60f, 400f, 62f);

        CreateDivider(window, -138f, true, 2f, DividerColor);
    }

    private void BuildTabs()
    {
        buyTab = CreateTab("BUY", Margin, OpenBuyMenu);
        sellTab = CreateTab("SELL", Margin + 170f, OpenSellMenu);
        CreateDivider(window, -198f, true, 1f, FaintDividerColor);
    }

    private Tab CreateTab(string text, float x, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = CreateRect("Pestaña " + text, window);
        Place(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -144f), new Vector2(160f, 54f));
        Image hitArea = rect.gameObject.AddComponent<Image>();
        hitArea.color = Color.clear; // recibe el clic

        TextMeshProUGUI label = CreateText("Texto", rect, text, 30f, MutedColor, true, TextAlignmentOptions.Center);
        label.characterSpacing = 10f;
        Stretch(label.rectTransform, 0f, 0f, 0f, 6f);

        Image underline = CreateImage("Subrayado", rect, InventoryUI.GoldColor);
        underline.raycastTarget = false;
        Place(underline.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(-24f, 3f));

        // Con el mouse encima el texto se aclara un poco
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = label;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = colors.normalColor;
        colors.colorMultiplier = 1.2f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);

        return new Tab { label = label, underline = underline };
    }

    private void BuildList()
    {
        RectTransform viewport = CreateRect("Lista", window);
        Stretch(viewport, Margin, Margin, ListTop, ListBottom);
        Image wheelArea = viewport.gameObject.AddComponent<Image>();
        wheelArea.color = Color.clear; // la rueda del mouse funciona también entre las filas
        viewport.gameObject.AddComponent<RectMask2D>();

        rows = CreateRect("Filas", viewport);
        rows.anchorMin = new Vector2(0f, 1f);
        rows.anchorMax = new Vector2(1f, 1f);
        rows.pivot = new Vector2(0.5f, 1f);
        rows.anchoredPosition = Vector2.zero;
        rows.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = RowSpacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        rows.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = rows;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        scroll.inertia = false;
        scroll.verticalScrollbar = CreateScrollbar();
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        emptyText = CreateText("Vacío", viewport, "", 24f, MutedColor, false, TextAlignmentOptions.Center);
        emptyText.fontStyle = FontStyles.Italic;
        Stretch(emptyText.rectTransform, 0f, 0f, 0f, 0f);
    }

    // Barra finita en el margen derecho de la lista; se ve solo cuando las filas no entran
    private Scrollbar CreateScrollbar()
    {
        Image track = CreateImage("Barra", window, new Color(1f, 1f, 1f, 0.05f));
        RectTransform rect = track.rectTransform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.offsetMin = new Vector2(-(Margin - 16f), ListBottom);
        rect.offsetMax = new Vector2(-(Margin - 22f), -ListTop);

        RectTransform slidingArea = CreateRect("Zona", rect);
        Stretch(slidingArea, 0f, 0f, 0f, 0f);
        Image handle = CreateImage("Agarre", slidingArea, InventoryUI.BorderColor);
        Stretch(handle.rectTransform, 0f, 0f, 0f, 0f);

        Scrollbar bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.transition = Selectable.Transition.None;
        bar.navigation = new Navigation { mode = Navigation.Mode.None };
        return bar;
    }

    // Abajo: el aviso de lo que pasó a la izquierda y el botón para salir a la derecha
    private void BuildFooter()
    {
        CreateDivider(window, ListBottom - 12f, false, 1f, FaintDividerColor);

        feedbackText = CreateText("Aviso", window, "", 23f, TextColor, false, TextAlignmentOptions.Left);
        feedbackText.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform rect = feedbackText.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.offsetMin = new Vector2(Margin, 22f);
        rect.offsetMax = new Vector2(-(Margin + 250f), 72f);

        Button leave = CreateButton("Salir", window, $"LEAVE  <size=60%><color={InventoryUI.MutedColor}>ESC</color></size>", 26f, out _);
        Place((RectTransform)leave.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, 22f),
              new Vector2(220f, 50f));
        leave.onClick.AddListener(Close);
    }

    // Una fila: marca de color, nombre, datos y descripción a la izquierda; precio y botón a la derecha
    private void AddRow(LootData item, int count, string info, string price, Color priceColor, string action, bool canAct,
                        System.Action onClick)
    {
        Image background = CreateImage("Fila " + item.ItemName, rows, Color.white);
        LayoutElement layout = background.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = RowHeight;
        layout.preferredHeight = RowHeight;

        // La fila se ilumina con el mouse encima (el Selectable está solo para eso)
        Selectable hover = background.gameObject.AddComponent<Selectable>();
        hover.targetGraphic = background;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = RowColor;
        colors.highlightedColor = RowHoverColor;
        colors.pressedColor = RowHoverColor;
        colors.selectedColor = RowColor;
        colors.disabledColor = RowColor;
        colors.fadeDuration = 0.08f;
        hover.colors = colors;
        hover.navigation = new Navigation { mode = Navigation.Mode.None };

        Transform row = background.transform;
        Image accent = CreateImage("Marca", row, AccentColor(item));
        accent.raycastTarget = false;
        Place(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(4f, 0f));

        // Ojo con el alto de las cajas: con Ellipsis, si una línea no entra de alto TextMeshPro no la
        // dibuja. La línea mide (ascender - descender) x tamaño: 1.21 x 31 = 37.5 en Cormorant (los
        // nombres no se veían en una caja de 36) y 1.12 x 19 = 21.2 en LiberationSans.
        string title = count > 1 ? $"{item.ItemName}  <size=70%><color={InventoryUI.LinkColor}>x{count}</color></size>" : item.ItemName;
        TextMeshProUGUI nameText = CreateText("Nombre", row, title, 31f, NameColor, true, TextAlignmentOptions.Left);
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        PlaceTop(nameText.rectTransform, 26f, 0f, RowActionWidth, 42f);

        TextMeshProUGUI infoText = CreateText("Datos", row, info, 19f, TextColor, false, TextAlignmentOptions.Left);
        infoText.overflowMode = TextOverflowModes.Ellipsis;
        PlaceTop(infoText.rectTransform, 27f, -37f, RowActionWidth, 26f);

        // Hasta dos líneas (40.8 de alto); si la descripción es más larga, la segunda termina en "..."
        TextMeshProUGUI description = CreateText("Descripción", row, item.Description, 18f, DescriptionColor, false,
                                                 TextAlignmentOptions.TopLeft);
        description.fontStyle = FontStyles.Italic;
        description.textWrappingMode = TextWrappingModes.Normal;
        description.overflowMode = TextOverflowModes.Ellipsis;
        PlaceTop(description.rectTransform, 27f, -63f, RowActionWidth, 46f);

        TextMeshProUGUI priceText = CreateText("Precio", row, price, 36f, priceColor, true, TextAlignmentOptions.Right);
        Place(priceText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-196f, 0f),
              new Vector2(130f, 48f));

        Button button = CreateButton("Botón", row, action, 24f, out TextMeshProUGUI label);
        Place((RectTransform)button.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f),
              new Vector2(156f, 52f));
        button.onClick.AddListener(() => onClick());
        if (!canAct)
        {
            button.interactable = false;
            button.GetComponent<Image>().color = DisabledBorderColor;
            label.color = MutedColor;
        }
    }

    // ---------------------------------------------------------------
    // Piezas de UI
    // ---------------------------------------------------------------

    // Botón con borde dorado. El borde es la imagen de abajo y el relleno, el que cambia de color con
    // el mouse, va adentro: así el tinte no oscurece el borde.
    private Button CreateButton(string name, Transform parent, string text, float fontSize, out TextMeshProUGUI label)
    {
        Image border = CreateImage(name, parent, InventoryUI.BorderColor);
        Image fill = CreateImage("Relleno", border.transform, Color.white);
        Stretch(fill.rectTransform, 2f, 2f, 2f, 2f);

        Button button = border.gameObject.AddComponent<Button>();
        button.targetGraphic = fill;
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = ButtonColor;
        colors.highlightedColor = ButtonHoverColor;
        colors.pressedColor = ButtonPressedColor;
        colors.selectedColor = ButtonColor;
        colors.disabledColor = ButtonDisabledColor;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        label = CreateText("Texto", border.transform, text, fontSize, InventoryUI.TitleColor, true, TextAlignmentOptions.Center);
        label.characterSpacing = 6f;
        Stretch(label.rectTransform, 0f, 0f, 0f, 0f);
        return button;
    }

    // Texto de una línea. Los de estilo título usan la fuente del catálogo (Cormorant).
    private TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color, bool titleStyle,
                                       TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (titleStyle && titleFont != null)
            label.font = titleFont;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }

    // Marco de cuatro líneas a 'inset' del borde del padre; con 'corners', un rombito en cada esquina
    private static void CreateFrame(Transform parent, string name, float inset, float thickness, Color color, bool corners)
    {
        RectTransform frame = CreateRect(name, parent);
        Stretch(frame, inset, inset, inset, inset);
        AddLine(frame, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, thickness), color);
        AddLine(frame, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, thickness), color);
        AddLine(frame, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(thickness, 0f), color);
        AddLine(frame, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(thickness, 0f), color);
        if (!corners) return;

        foreach (Vector2 corner in new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one })
        {
            Image diamond = CreateImage("Rombo", frame, InventoryUI.BorderColor);
            diamond.raycastTarget = false;
            Place(diamond.rectTransform, corner, corner, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(7f, 7f));
            diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        }
    }

    private static void AddLine(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Color color)
    {
        Image line = CreateImage("Línea", parent, color);
        line.raycastTarget = false;
        Place(line.rectTransform, anchorMin, anchorMax, pivot, Vector2.zero, size);
    }

    // Línea horizontal de lado a lado (entre los márgenes), a 'y' de arriba o de abajo de la ventana
    private static void CreateDivider(Transform parent, float y, bool fromTop, float thickness, Color color)
    {
        Image line = CreateImage("Separador", parent, color);
        line.raycastTarget = false;
        float anchorY = fromTop ? 1f : 0f;
        Place(line.rectTransform, new Vector2(0f, anchorY), new Vector2(1f, anchorY), new Vector2(0.5f, 0.5f), new Vector2(0f, y),
              new Vector2(-2f * Margin, thickness));
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        RectTransform rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    // Ocupa todo el padre menos los márgenes indicados
    private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    // De lado a lado del padre (dejando 'left' y 'right'), con el borde de arriba a 'top' (negativo)
    private static void PlaceTop(RectTransform rect, float left, float top, float right, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.offsetMin = new Vector2(left, top - height);
        rect.offsetMax = new Vector2(-right, top);
    }

    // Pegado arriba a la derecha, a 'right' del borde derecho y con el borde de arriba a 'top' (negativo)
    private static void PlaceTopRight(RectTransform rect, float right, float top, float width, float height)
    {
        Place(rect, Vector2.one, Vector2.one, Vector2.one, new Vector2(-right, top), new Vector2(width, height));
    }
}
