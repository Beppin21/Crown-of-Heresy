#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Atajo de prueba: Ctrl + Shift + J lleva al jugador a la puerta del jefe (del lado del pasillo,
// mirándola). Si no está en el Dungeon, carga el Dungeon y lo pone ahí. Solo existe en el Editor y
// en los builds de desarrollo (Development Build), no en el juego final. Se crea solo al arrancar,
// no hay que ponerlo en ninguna escena.
public class AtajoPuertaJefe : MonoBehaviour
{
    private const string EscenaDelJefe = "Dungeon";
    private const float DistanciaDeLaPuerta = 3f; // metros delante de la puerta, en el pasillo

    private static bool irAlCargar;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Crear()
    {
        var go = new GameObject("Atajo a la puerta del jefe") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<AtajoPuertaJefe>();
        SceneManager.sceneLoaded -= AlCargarEscena;
        SceneManager.sceneLoaded += AlCargarEscena;
    }

    private void Update()
    {
        Keyboard k = Keyboard.current;
        if (k == null || !k.jKey.wasPressedThisFrame) return;
        if (!k.ctrlKey.isPressed || !k.shiftKey.isPressed) return;

        if (FindAnyObjectByType<BossDoor>() != null)
        {
            IrALaPuerta();
        }
        else
        {
            Debug.Log($"[Prueba] Cargando {EscenaDelJefe} para ir a la puerta del jefe.");
            irAlCargar = true;
            SceneManager.LoadScene(EscenaDelJefe);
        }
    }

    private static void AlCargarEscena(Scene escena, LoadSceneMode modo)
    {
        if (!irAlCargar || escena.name != EscenaDelJefe) return;
        irAlCargar = false;
        IrALaPuerta();
    }

    private static void IrALaPuerta()
    {
        BossDoor puerta = FindAnyObjectByType<BossDoor>();
        GameObject jugador = GameObject.FindWithTag("Player");
        if (puerta == null || jugador == null)
        {
            Debug.LogWarning("[Prueba] No encontré la puerta del jefe (BossDoor) o al jugador (tag Player).");
            return;
        }

        // El frente de la puerta (transform.forward) mira al pasillo
        Vector3 frente = puerta.transform.forward;
        frente.y = 0f;
        frente = frente.sqrMagnitude > 0.001f ? frente.normalized : Vector3.forward;
        Vector3 destino = puerta.transform.position + frente * DistanciaDeLaPuerta;

        // Al piso que haya ahí
        if (Physics.Raycast(destino + Vector3.up * 2f, Vector3.down, out RaycastHit piso, 10f, ~0, QueryTriggerInteraction.Ignore))
            destino = piso.point;

        SaveSystem.MoverJugador(jugador.transform, destino, Quaternion.LookRotation(-frente));
        Debug.Log("[Prueba] Te llevé a la puerta del jefe (Ctrl + Shift + J).");
    }
}
#endif
