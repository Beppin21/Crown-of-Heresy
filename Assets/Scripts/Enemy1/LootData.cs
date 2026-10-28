using NUnit.Framework.Interfaces;
using UnityEngine;

[CreateAssetMenu(fileName = "LootData", menuName = "Scriptable Objects/LootData")]
public class LootData : ScriptableObject
{
    [SerializeField] private string itemName;
    [SerializeField] [TextArea] private string description;
    [SerializeField] private int value;

    public string ItemName => itemName;
    public string Description => description;
    public int Value => value;

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
