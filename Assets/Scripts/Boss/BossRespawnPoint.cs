using UnityEngine;
using UnityEngine.SceneManagement;

// Punto de reaparición en la entrada de la sala del jefe. YA NO SE USA: al morir se va a la pantalla
// de derrota y Try Again carga la partida guardada en la última hoguera (hay una afuera de la sala).
// BossController ya no llama a Arm, así que este punto nunca mueve al jugador. Si se volviera a
// armar (Arm), al recargar la escena pondría al jugador acá (mirando hacia la flecha azul); se
// desarma al ganarle al jefe o al cargar cualquier otra escena.
[DefaultExecutionOrder(-500)]
public class BossRespawnPoint : MonoBehaviour
{
    // Estáticos: tienen que sobrevivir a la recarga de la escena
    private static bool armed;
    private static string armedScene;

    public static void Arm(string sceneName)
    {
        armed = true;
        armedScene = sceneName;
    }

    public static void Disarm()
    {
        armed = false;
        armedScene = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Disarm();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // sceneLoaded llega después de los Awake de la escena nueva: la recarga del Dungeon ya usó el
    // punto, y cualquier otra escena desarma
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (armed && scene.name != armedScene) Disarm();
    }

    private void Awake()
    {
        if (!armed || gameObject.scene.name != armedScene) return;

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;

        // Se mueve todo el prefab del jugador (cámara incluida), no solo el cuerpo
        Transform body = player.transform;
        body.root.position += transform.position - body.position;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f)
            body.rotation = Quaternion.LookRotation(forward);

        Rigidbody rb = body.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = body.position;
            rb.rotation = body.rotation;
            rb.linearVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
        Debug.Log("[Jefe] Reaparecés en la entrada de la sala del jefe.", this);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.5f);
        Gizmos.DrawLine(transform.position + Vector3.up * 0.9f, transform.position + Vector3.up * 0.9f + transform.forward * 1.5f);
    }
}
