using System.Collections.Generic;
using UnityEngine;

// Todos los ítems del juego (LootData, armas incluidas), para que el guardado los vuelva a encontrar
// por nombre al cargar la partida. Vive en Resources (Assets/Resources/ItemRegistry.asset) y lo
// mantiene al día solo Scripts/Editor/ItemRegistrySetup.cs: no hace falta tocarlo a mano.
public class ItemRegistry : ScriptableObject
{
    public const string ResourcePath = "ItemRegistry";

    [SerializeField] private List<LootData> items = new List<LootData>();

    private static ItemRegistry instance;

    public IReadOnlyList<LootData> Items => items;

    // El ítem con ese nombre, o null si no está (por ejemplo, uno creado en el juego con LootData.Create)
    public static LootData Find(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return null;
        if (instance == null) instance = Resources.Load<ItemRegistry>(ResourcePath);
        if (instance == null)
        {
            Debug.LogWarning($"[Guardado] No está Resources/{ResourcePath}: los ítems guardados no se pueden recuperar.");
            return null;
        }
        foreach (LootData item in instance.items)
            if (item != null && item.ItemName == itemName) return item;
        return null;
    }

#if UNITY_EDITOR
    public void SetItems(List<LootData> list) => items = list;
#endif
}
