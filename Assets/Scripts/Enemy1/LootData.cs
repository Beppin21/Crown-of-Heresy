using UnityEngine;

// Ítem del juego (se compra, se vende, se junta como loot y va al inventario).
// Las armas (WeaponSO) también son LootData.
[CreateAssetMenu(fileName = "LootData", menuName = "Scriptable Objects/LootData")]
public class LootData : ScriptableObject
{
    [SerializeField] private string itemName;
    [SerializeField] [TextArea] private string description;
    [SerializeField] private int value;
    [Tooltip("Loot común de los enemigos (por ejemplo el Magic Cube). Al venderlo no pasa al stock " +
             "de la tienda; todo lo demás que se vende se puede volver a comprar.")]
    [SerializeField] private bool basicLoot;

    public string ItemName => itemName;
    public string Description => description;
    public int Value => value;
    public bool IsBasicLoot => basicLoot;

    // Permite instanciar un ítem rápido en memoria desde cualquier script
    public static LootData Create(string name, string desc, int val)
    {
        LootData item = CreateInstance<LootData>();
        item.itemName = name;
        item.description = desc;
        item.value = val;
        return item;
    }

}
