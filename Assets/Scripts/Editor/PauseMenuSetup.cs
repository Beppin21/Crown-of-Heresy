using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Tools > UI > Armar menú de pausa: deja guardados en el Canvas de la escena abierta el botón de
// pausa (||) y un panel "Menu Pausa" vacío, para agregarle los botones (Reanudar, Salir) a mano.
// El componente Pause va en el Canvas con los dos ya asignados. Después hay que guardar la escena
public static class PauseMenuSetup
{
    [MenuItem("Tools/UI/Armar menú de pausa")]
    public static void Armar()
    {
        var canvas = BuscarCanvas();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Menú de pausa", "No hay ningún Canvas en la escena abierta.", "OK");
            return;
        }

        var pausa = canvas.GetComponent<Pause>();
        if (pausa == null) pausa = Undo.AddComponent<Pause>(canvas.gameObject);
        Undo.RecordObject(pausa, "Armar menú de pausa");

        // Si ya están en el Canvas (por ejemplo, copiados de otra escena), se usan esos
        if (pausa.botonPausa == null) pausa.botonPausa = BuscarHijo(canvas.transform, "Boton Pausa");
        if (pausa.menuPausa == null) pausa.menuPausa = BuscarHijo(canvas.transform, "Menu Pausa");

        // Botón ||: llama a Pause.Pausa() al hacer clic
        if (pausa.botonPausa == null)
        {
            var boton = Pause.ArmarBoton(canvas.transform);
            Undo.RegisterCreatedObjectUndo(boton, "Armar menú de pausa");
            pausa.botonPausa = boton;
        }
        ConectarBoton(pausa);

        // Panel del menú: fondo oscuro a pantalla completa, apagado (aparece al pausar)
        if (pausa.menuPausa == null)
        {
            var menu = new GameObject("Menu Pausa", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(menu, "Armar menú de pausa");
            menu.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)menu.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            menu.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            menu.SetActive(false);
            pausa.menuPausa = menu;
        }

        // Al final del Canvas, para que se dibujen encima del resto
        pausa.menuPausa.transform.SetAsLastSibling();
        pausa.botonPausa.transform.SetAsLastSibling();

        EditorUtility.SetDirty(pausa);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = pausa.menuPausa;
        Debug.Log($"[Pausa] Botón y menú de pausa armados en \"{canvas.name}\". Guardá la escena.");
    }

    static GameObject BuscarHijo(Transform padre, string nombre)
    {
        var hijo = padre.Find(nombre);
        return hijo != null ? hijo.gameObject : null;
    }

    // El OnClick del botón || llama a Pausa() del Pause de esta escena. Saca las llamadas que
    // quedaron rotas o apuntando a otro objeto (al copiar el botón desde otra escena)
    static void ConectarBoton(Pause pausa)
    {
        var boton = pausa.botonPausa.GetComponent<Button>();
        if (boton == null) return;
        Undo.RecordObject(boton, "Armar menú de pausa");

        bool conectado = false;
        for (int i = boton.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            var destino = boton.onClick.GetPersistentTarget(i);
            if (destino == pausa && boton.onClick.GetPersistentMethodName(i) == nameof(Pause.Pausa)) conectado = true;
            else if (destino == null || destino is Pause) UnityEventTools.RemovePersistentListener(boton.onClick, i);
        }
        if (!conectado) UnityEventTools.AddPersistentListener(boton.onClick, pausa.Pausa);
        EditorUtility.SetDirty(boton);
    }

    // El Canvas de la interfaz del juego (el que se llama "Canvas"), o el primero que haya
    static Canvas BuscarCanvas()
    {
        Canvas primero = null;
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || EditorUtility.IsPersistent(c)) continue;
            if (c.name == "Canvas") return c;
            if (primero == null) primero = c;
        }
        return primero;
    }
}
