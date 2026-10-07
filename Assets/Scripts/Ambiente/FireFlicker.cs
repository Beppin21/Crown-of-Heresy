using UnityEngine;

// Hace que una luz parpadee como fuego: la intensidad, el alcance y el color varían de forma
// irregular (ruido de Perlin, no al azar puro, para que no "titile" como una lamparita rota),
// y la luz se mueve apenas para que las sombras bailen.
[RequireComponent(typeof(Light))]
public class FireFlicker : MonoBehaviour
{
    [Tooltip("Intensidad promedio de la luz.")]
    [SerializeField] private float baseIntensity = 6f;
    [Tooltip("Cuánto sube y baja la intensidad (0 = fija, 0.5 = varía ±50%).")]
    [Range(0f, 1f)] [SerializeField] private float intensityVariation = 0.35f;
    [Tooltip("Qué tan rápido parpadea.")]
    [SerializeField] private float speed = 3f;
    [Tooltip("Color más caliente (llama fuerte) y más frío (llama baja).")]
    [SerializeField] private Color hotColor = new Color(1f, 0.62f, 0.25f);
    [SerializeField] private Color coolColor = new Color(1f, 0.32f, 0.08f);
    [Tooltip("Cuánto se mueve la luz (metros), para que las sombras bailen.")]
    [SerializeField] private float jitter = 0.15f;

    private Light fireLight;
    private float baseRange;
    private Vector3 basePosition;
    private float seed;

    // Qué tan grande es el fuego (0 = apagado, 1 = a pleno). Lo usa FireIgnition para que un
    // incendio que recién empieza ilumine poco y vaya creciendo.
    public float Strength { get; set; } = 1f;

    private void Awake()
    {
        fireLight = GetComponent<Light>();
        baseRange = fireLight.range;
        basePosition = transform.localPosition;
        seed = Random.value * 100f; // cada fuego parpadea distinto
    }

    private void Update()
    {
        // Con las ráfagas de viento el fuego parpadea más rápido y más fuerte
        float gust = WindGusts.Gust;
        float t = Time.time * speed * (1f + gust * 0.8f);

        // Dos capas de ruido: una lenta (la llama crece y baja) y una rápida (chisporroteo)
        float slow = Mathf.PerlinNoise(seed, t * 0.35f);
        float fast = Mathf.PerlinNoise(seed + 13.7f, t * 1.7f);
        float flicker = Mathf.Clamp01(slow * 0.65f + fast * 0.35f); // 0..1
        float variation = intensityVariation * (1f + gust * 0.6f);

        fireLight.intensity = baseIntensity * Strength * (1f + (flicker - 0.5f) * 2f * variation);
        fireLight.range = baseRange * Mathf.Lerp(0.4f, 1f, Strength) * (0.9f + flicker * 0.2f);
        fireLight.color = Color.Lerp(coolColor, hotColor, flicker);

        transform.localPosition = basePosition + new Vector3(
            (Mathf.PerlinNoise(seed + 3.1f, t) - 0.5f) * 2f * jitter,
            (Mathf.PerlinNoise(seed + 5.3f, t) - 0.5f) * 2f * jitter,
            (Mathf.PerlinNoise(seed + 7.9f, t) - 0.5f) * 2f * jitter);
    }
}
