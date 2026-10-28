using UnityEngine;
using UnityEngine.Rendering.Universal;

// Ajustes de cámara que dependen de la escena (por eso no van en el prefab del jugador, que
// también se usa en Dungeon):
//   - el fondo de la cámara pasa a ser del mismo color que la niebla, así el cielo se "pierde"
//     en la niebla en vez de verse un corte;
//   - se prende el post-processing (bloom del fuego, viñeta, grano, colores fríos).
public class NightAtmosphere : MonoBehaviour
{
    [Tooltip("Tildado: el fondo de la cámara usa el color de la niebla (Lighting > Environment > Fog).")]
    [SerializeField] private bool backgroundMatchesFog = true;
    [SerializeField] private bool enablePostProcessing = true;

    private void Start()
    {
        UnityEngine.Camera cam = UnityEngine.Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[NightAtmosphere] No hay cámara con el tag MainCamera en la escena.", this);
            return;
        }

        if (backgroundMatchesFog)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
        }

        if (enablePostProcessing && cam.TryGetComponent(out UniversalAdditionalCameraData data))
            data.renderPostProcessing = true;
    }
}
