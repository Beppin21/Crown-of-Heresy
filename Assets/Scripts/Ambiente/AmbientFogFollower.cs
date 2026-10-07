using UnityEngine;

// Mantiene el emisor de niebla/humo alrededor del jugador. Así alcanza con una nube de
// partículas de ~50 m que lo acompaña, en vez de llenar todo el mapa de partículas.
// Las partículas se simulan en espacio "World": las que ya salieron se quedan donde están
// (el jugador camina a través de la niebla), solo las nuevas nacen alrededor de él.
public class AmbientFogFollower : MonoBehaviour
{
    [Tooltip("A quién seguir. Si se deja vacío, busca el objeto con el tag Player.")]
    [SerializeField] private Transform target;
    [Tooltip("Altura fija del emisor sobre el jugador (la niebla queda pegada al piso).")]
    [SerializeField] private float heightOffset = 0.5f;

    private void Start()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) target = player.transform;
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;
        transform.position = target.position + Vector3.up * heightOffset;
    }
}
