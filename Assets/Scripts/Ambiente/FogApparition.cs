using UnityEngine;

// Una silueta oscura que aparece a lo lejos, en el borde de la niebla, quieta y mirando al
// jugador... y desaparece cuando el jugador se acerca. Después de un rato vuelve a aparecer en
// otro lugar. Es solo un susto visual: no ataca ni bloquea el paso (no tiene collider).
//
// Para que no se la vea "aparecer de la nada": solo se materializa en un lugar que en ese momento
// no está en pantalla, o que está tan lejos que la niebla lo tapa.
public class FogApparition : MonoBehaviour
{
    [Tooltip("A qué distancia del jugador aparece (metros, al azar entre estos dos valores). " +
             "Conviene que sea donde la niebla ya es espesa.")]
    [SerializeField] private Vector2 appearDistance = new Vector2(17f, 24f);
    [Tooltip("Si el jugador se acerca a menos de esta distancia, desaparece.")]
    [SerializeField] private float vanishDistance = 12f;
    [Tooltip("Segundos que tarda en volver a aparecer después de desaparecer (al azar entre estos dos valores).")]
    [SerializeField] private Vector2 hiddenTime = new Vector2(20f, 45f);
    [Tooltip("Segundos como máximo que se queda visible, aunque el jugador no se acerque.")]
    [SerializeField] private float maxVisibleTime = 25f;
    [Tooltip("Probabilidad de aparecer delante del jugador (en la dirección en la que camina/mira), " +
             "en vez de en cualquier lado. Delante asusta más.")]
    [Range(0f, 1f)] [SerializeField] private float aheadChance = 0.7f;
    [Tooltip("Capas que cuentan como piso para apoyarla.")]
    [SerializeField] private LayerMask groundMask = ~0;

    private Renderer[] renderers;
    private Animator animator;
    private Transform player;
    private bool visible;
    private float nextChange;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        animator = GetComponentInChildren<Animator>();
        SetVisible(false);
        nextChange = Time.time + Random.Range(hiddenTime.x * 0.3f, hiddenTime.x); // la primera vez, antes
    }

    private void Start()
    {
        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject != null) player = playerObject.transform;
    }

    private void Update()
    {
        if (player == null) return;

        if (visible)
        {
            // Siempre de frente al jugador
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toPlayer), Time.deltaTime * 2f);

            if (toPlayer.magnitude < vanishDistance || Time.time >= nextChange)
                Hide();
        }
        else if (Time.time >= nextChange)
        {
            TryAppear();
        }
    }

    private void Hide()
    {
        SetVisible(false);
        nextChange = Time.time + Random.Range(hiddenTime.x, hiddenTime.y);
    }

    private void TryAppear()
    {
        UnityEngine.Camera cam = UnityEngine.Camera.main;
        Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : player.forward;

        // Se prueban algunas posiciones; si ninguna sirve, se reintenta en un rato
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = Random.value < aheadChance ? Random.Range(-55f, 55f) : Random.Range(0f, 360f);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * forward;
            float distance = Random.Range(appearDistance.x, appearDistance.y);
            Vector3 candidate = player.position + direction * distance;

            // Apoyada en el piso (terreno u otra cosa)
            if (!Physics.Raycast(candidate + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f, groundMask, QueryTriggerInteraction.Ignore))
                continue;
            if (hit.transform.IsChildOf(player.root)) continue;
            candidate = hit.point;

            // No aparecer "de la nada" frente a la cámara salvo que la niebla ya la tape
            if (cam != null && IsOnScreen(cam, candidate + Vector3.up) && !FullyFogged(cam, candidate))
                continue;

            transform.position = candidate;
            Vector3 toPlayer = player.position - candidate;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(toPlayer);

            SetVisible(true);
            if (animator != null) animator.Play(0, 0, Random.value); // cada aparición arranca la animación en otro punto
            nextChange = Time.time + maxVisibleTime;
            return;
        }
        nextChange = Time.time + 3f;
    }

    private static bool IsOnScreen(UnityEngine.Camera cam, Vector3 point)
    {
        Vector3 v = cam.WorldToViewportPoint(point);
        return v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
    }

    // ¿La niebla del Lighting tapa más del 85% a esa distancia?
    private static bool FullyFogged(UnityEngine.Camera cam, Vector3 point)
    {
        if (!RenderSettings.fog) return false;
        float d = Vector3.Distance(cam.transform.position, point);
        float density = RenderSettings.fogDensity;
        float visibility = RenderSettings.fogMode == FogMode.ExponentialSquared
            ? Mathf.Exp(-(d * density) * (d * density))
            : RenderSettings.fogMode == FogMode.Exponential
                ? Mathf.Exp(-d * density)
                : 1f - Mathf.InverseLerp(RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, d);
        return visibility < 0.15f;
    }

    private void SetVisible(bool value)
    {
        visible = value;
        foreach (Renderer r in renderers)
        {
            if (r != null) r.enabled = value;
        }
        if (animator != null) animator.enabled = value; // escondida no gasta en animarse
    }
}
