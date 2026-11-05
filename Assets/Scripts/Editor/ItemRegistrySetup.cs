using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Mantiene al día Assets/Resources/ItemRegistry.asset con todos los LootData del proyecto (armas
// incluidas), que es donde el guardado busca los ítems por nombre al cargar la partida. Se revisa al
// compilar y cada vez que se crea, cambia o borra un .asset.
[InitializeOnLoad]
public static class ItemRegistrySetup
{
    private const string RegistryPath = "Assets/Resources/" + ItemRegistry.ResourcePath + ".asset";

    static ItemRegistrySetup()
    {
        EditorApplication.delayCall += Refresh;
    }

    private class Watcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Concat(deleted).Concat(moved).Any(p => p.EndsWith(".asset") && p != RegistryPath))
                EditorApplication.delayCall += Refresh;
        }
    }

    private static void Refresh()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        List<LootData> items = AssetDatabase.FindAssets("t:LootData")
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path)
            .Select(AssetDatabase.LoadAssetAtPath<LootData>)
            .Where(item => item != null)
            .ToList();

        ItemRegistry registry = AssetDatabase.LoadAssetAtPath<ItemRegistry>(RegistryPath);
        if (registry != null && registry.Items.SequenceEqual(items)) return;

        bool created = registry == null;
        if (created) registry = ScriptableObject.CreateInstance<ItemRegistry>();
        registry.SetItems(items);
        if (created) AssetDatabase.CreateAsset(registry, RegistryPath);
        else EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();

        foreach (IGrouping<string, LootData> repeated in items.GroupBy(i => i.ItemName).Where(g => g.Count() > 1))
            Debug.LogWarning($"[Guardado] Hay {repeated.Count()} ítems llamados \"{repeated.Key}\": al cargar la partida se usa el primero.");
        Debug.Log($"[Guardado] Lista de ítems para el guardado al día: {items.Count} ({RegistryPath}).");
    }
}
