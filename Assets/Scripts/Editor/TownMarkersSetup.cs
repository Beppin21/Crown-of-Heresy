using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Pone los indicadores flotantes (FloatingMarker) de Town: amarillo arriba de la tienda (ShopTrigger) y
// rojo arriba de la entrada a la mazmorra (SceneTeleportDungeon, el objeto "Door"). Cada uno es un hijo
// "Indicador" de ese objeto, sobre lo más alto que se ve de él. Se aplica solo, al compilar, al abrir la
// escena o al salir de Play, donde todavía no hay indicador: queda la escena marcada como modificada y
// hay que guardarla. Para moverlo, mover el objeto "Indicador".
[InitializeOnLoad]
public static class TownMarkersSetup
{
    private const string MarkerName = "Indicador";
    private const string ShaderName = "Crown/Indicador (sin niebla)";
    private static readonly Color ShopColor = new Color(1f, 0.82f, 0.1f);
    private static readonly Color DungeonColor = new Color(0.95f, 0.12f, 0.08f);
    private const float AboveShop = 1.4f;     // metros sobre el techo del puesto
    private const float AboveEntrance = 1.2f; // metros sobre la entrada

    static TownMarkersSetup()
    {
        EditorApplication.delayCall += AutoRun;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += AutoRun;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += AutoRun;
        };
    }

    private static void AutoRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        foreach (ShopTrigger shop in InScenes<ShopTrigger>())
            Ensure(shop.transform, ShopColor, "amarillo", AboveShop, "la tienda");
        foreach (SceneTeleportDungeon entrance in InScenes<SceneTeleportDungeon>())
            Ensure(entrance.transform, DungeonColor, "rojo", AboveEntrance, "la entrada a la mazmorra");
    }

    private static T[] InScenes<T>() where T : Component
    {
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                     .Where(c => c.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(c.gameObject.scene))
                     .ToArray();
    }

    private static void Ensure(Transform target, Color color, string colorName, float above, string what)
    {
        if (target.GetComponentsInChildren<FloatingMarker>(true).Length > 0) return;

        Vector3 top = target.position + Vector3.up * 2f;
        if (VisibleBounds(target, out Bounds bounds)) top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);

        var marker = new GameObject(MarkerName);
        marker.transform.SetParent(target, false);
        marker.transform.position = top + Vector3.up * above;
        FloatingMarker floating = marker.AddComponent<FloatingMarker>();
        var so = new SerializedObject(floating);
        so.FindProperty("color").colorValue = color;
        so.FindProperty("shader").objectReferenceValue = Shader.Find(ShaderName);
        so.ApplyModifiedPropertiesWithoutUndo();
        floating.enabled = false; // se rearma con el color y el shader nuevos
        floating.enabled = true;

        EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        Debug.Log($"[TownMarkersSetup] Indicador {colorName} arriba de {what} ({target.name}), a {marker.transform.position.y:0.0} m de altura" +
                  (Shader.Find(ShaderName) == null ? $". OJO: no encontré el shader {ShaderName}: se va a ver con niebla." : ". Guardá la escena."));
    }

    // Lo que se ve del objeto (mallas activas); si no tiene, sus colliders
    private static bool VisibleBounds(Transform target, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        if (any) return true;
        foreach (Collider c in target.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled) continue;
            if (!any) { bounds = c.bounds; any = true; }
            else bounds.Encapsulate(c.bounds);
        }
        return any;
    }
}
