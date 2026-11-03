using UnityEngine;

// Viento con ráfagas: un viento suave y constante, y cada tanto una ráfaga que sube, dura unos
// segundos y se calma. Mueve a la vez todo lo que reacciona al viento:
//   - las partículas (niebla, humo, ceniza, fuego) a través del WindZone de Unity (módulo
//     "External Forces" de cada sistema de partículas);
//   - la lámpara del jugador y la luz del fuego, leyendo WindGusts.Current / WindGusts.Gust.
[RequireComponent(typeof(WindZone))]
public class WindGusts : MonoBehaviour
{
    [Tooltip("Fuerza del viento cuando está calmo.")]
    [SerializeField] private float baseStrength = 0.25f;
    [Tooltip("Fuerza extra en el pico de una ráfaga.")]
    [SerializeField] private float gustStrength = 1.4f;
    [Tooltip("Segundos entre ráfagas (al azar entre estos dos valores).")]
    [SerializeField] private Vector2 gustInterval = new Vector2(7f, 16f);
    [Tooltip("Cuánto dura cada ráfaga (segundos, al azar entre estos dos valores).")]
    [SerializeField] private Vector2 gustDuration = new Vector2(2f, 4.5f);
    [Tooltip("Cuánto cambia de dirección el viento (grados) a lo largo del tiempo.")]
    [SerializeField] private float directionWander = 25f;

    private WindZone zone;
    private float baseYaw;
    private float nextGustTime;
    private float gustStart = -100f;
    private float gustLength = 1f;

    // Viento actual en el mundo (dirección * fuerza) y qué tan fuerte es la ráfaga (0..1)
    public static Vector3 Current { get; private set; }
    public static float Gust { get; private set; }

    private void Awake()
    {
        zone = GetComponent<WindZone>();
        zone.mode = WindZoneMode.Directional;
        baseYaw = transform.eulerAngles.y;
        nextGustTime = Time.time + Random.Range(gustInterval.x, gustInterval.y);
    }

    private void OnDisable()
    {
        Current = Vector3.zero;
        Gust = 0f;
    }

    private void Update()
    {
        if (Time.time >= nextGustTime)
        {
            gustStart = Time.time;
            gustLength = Random.Range(gustDuration.x, gustDuration.y);
            nextGustTime = Time.time + gustLength + Random.Range(gustInterval.x, gustInterval.y);
        }

        // Forma de la ráfaga: sube rápido, se mantiene y se calma despacio
        float t = (Time.time - gustStart) / gustLength;
        float gust = t < 0f || t > 1f ? 0f : Mathf.Sin(Mathf.Pow(t, 0.6f) * Mathf.PI);
        Gust = gust;

        // La dirección se va moviendo de a poco
        float yaw = baseYaw + (Mathf.PerlinNoise(Time.time * 0.03f, 4.2f) - 0.5f) * 2f * directionWander;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        float strength = baseStrength + gust * gustStrength;
        zone.windMain = strength;
        zone.windTurbulence = 0.2f + gust * 0.8f;
        Current = transform.forward * strength;
    }
}
