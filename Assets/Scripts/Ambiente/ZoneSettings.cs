using UnityEngine;

// Configuración de la zona (una por escena): cómo se comporta el jugador acá.
//   - Town: modo lámpara (sin combate, se explora con la lámpara y el clic derecho la acerca).
//   - Dungeon: sin este componente, o con el modo lámpara apagado (combate normal).
// Los scripts del jugador la consultan con ZoneSettings.Current.
public class ZoneSettings : MonoBehaviour
{
    [Tooltip("Modo lámpara: el jugador explora con la lámpara en la mano. La cámara es solo de exploración " +
             "y con clic derecho se acerca a la lámpara.")]
    public bool lanternMode = true;

    // La zona de la escena actual (null si la escena no tiene una)
    public static ZoneSettings Current { get; private set; }

    public static bool LanternMode => Current != null && Current.lanternMode;

    private void Awake()
    {
        Current = this;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }
}
