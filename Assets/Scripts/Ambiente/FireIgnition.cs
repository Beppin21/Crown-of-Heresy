using System.Collections.Generic;
using UnityEngine;

// Un incendio que todavía no empezó (va en el objeto "Incendio" de una casa).
// Arranca apagado y se prende:
//   - después de unos segundos de empezada la escena, y/o
//   - cuando el jugador se acerca a cierta distancia (y se aleja, o pasa por ahí).
// Al prenderse no aparece de golpe: empieza como un fuego chico y va creciendo hasta el máximo
// (más llamas, más humo, más luz).
public class FireIgnition : MonoBehaviour
{
    [Tooltip("Segundos desde que empieza la escena hasta que se prende. 0 = no se prende por tiempo.")]
    [SerializeField] private float delaySeconds = 90f;
    [Tooltip("Se prende cuando el jugador está a menos de esta distancia (metros). 0 = no se prende por cercanía.")]
    [SerializeField] private float playerDistance = 0f;
    [Tooltip("Segundos que tarda en pasar de un fuego chico a un incendio a pleno.")]
    [SerializeField] private float growTime = 25f;

    private struct Emitter
    {
        public ParticleSystem system;
        public float rate;
    }

    private readonly List<Emitter> emitters = new List<Emitter>();
    private FireFlicker[] lights;
    private Transform player;
    private bool ignited;
    private float ignitionTime;

    public bool IsBurning => ignited;

    private void Awake()
    {
        foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
            emitters.Add(new Emitter { system = ps, rate = ps.emission.rateOverTimeMultiplier });
        lights = GetComponentsInChildren<FireFlicker>(true);

        SetChildrenActive(false); // apagado hasta que se prenda
    }

    private void Start()
    {
        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject != null) player = playerObject.transform;
    }

    private void Update()
    {
        if (!ignited)
        {
            bool byTime = delaySeconds > 0f && Time.timeSinceLevelLoad >= delaySeconds;
            bool byDistance = playerDistance > 0f && player != null &&
                              Vector3.Distance(player.position, transform.position) <= playerDistance;
            if (byTime || byDistance) Ignite();
            return;
        }

        // Crece de a poco (curva suave: arranca lento, se acelera y llega al máximo)
        float grow = growTime <= 0f ? 1f : Mathf.Clamp01((Time.time - ignitionTime) / growTime);
        float strength = Mathf.SmoothStep(0.08f, 1f, grow);
        foreach (Emitter e in emitters)
        {
            if (e.system == null) continue;
            var emission = e.system.emission;
            emission.rateOverTimeMultiplier = e.rate * strength;
        }
        foreach (FireFlicker light in lights)
        {
            if (light != null) light.Strength = strength;
        }

        if (grow >= 1f) enabled = false; // ya está a pleno: no hace falta seguir calculando
    }

    // Se puede llamar desde otro script (por ejemplo un trigger de evento)
    public void Ignite()
    {
        if (ignited) return;
        ignited = true;
        ignitionTime = Time.time;
        SetChildrenActive(true);
        foreach (Emitter e in emitters)
        {
            if (e.system == null) continue;
            var emission = e.system.emission;
            emission.rateOverTimeMultiplier = e.rate * 0.08f;
            e.system.Play(true);
        }
        foreach (FireFlicker light in lights)
        {
            if (light != null) light.Strength = 0.08f;
        }
    }

    private void SetChildrenActive(bool active)
    {
        foreach (Transform child in transform)
            child.gameObject.SetActive(active);
    }
}
