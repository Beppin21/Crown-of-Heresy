using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Lo que vende la tienda al empezar la partida y la fuente de sus títulos. ShopManager lo carga de
// Resources (Assets/Resources/ShopCatalog.asset), así el stock se cambia en este asset sin tocar la
// escena del Town.
[CreateAssetMenu(fileName = "ShopCatalog", menuName = "Scriptable Objects/Shop Catalog")]
public class ShopCatalog : ScriptableObject
{
    private const string ResourcePath = "ShopCatalog";

    [Tooltip("Ítems a la venta al empezar la partida. Lo que el jugador vende después (salvo el loot " +
             "común de los enemigos) se suma al stock para poder recomprarlo.")]
    [SerializeField] private List<LootData> startingStock = new List<LootData>();

    [Tooltip("Fuente del título, los nombres, los precios y los botones. Vacía, usa la de TextMeshPro.")]
    [SerializeField] private TMP_FontAsset titleFont;

    public IReadOnlyList<LootData> StartingStock => startingStock;
    public TMP_FontAsset TitleFont => titleFont;

    public static ShopCatalog Load()
    {
        ShopCatalog catalog = Resources.Load<ShopCatalog>(ResourcePath);
        if (catalog == null)
            Debug.LogWarning($"[Tienda] No está Resources/{ResourcePath}: la tienda arranca vacía.");
        return catalog;
    }
}
