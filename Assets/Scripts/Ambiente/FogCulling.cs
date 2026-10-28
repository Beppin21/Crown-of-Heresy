using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Optimización "estilo Silent Hill 2 original": la niebla tapa todo lo que está lejos, así que
// eso directamente no se dibuja. Se apagan los modelos, luces y partículas que quedan más allá
// de la distancia en la que la niebla ya es total, y se vuelven a prender al acercarse.
//
// Usa CullingGroup (la API de Unity para esto): Unity calcula qué objetos cruzan la distancia
// y avisa solo cuando alguno cambia, así que el costo por frame es casi nulo aunque haya
// cientos de árboles y casas.
//
// Solo maneja objetos QUIETOS (casas, árboles, cercos, fuegos). Los que se mueven (jugador,
// enemigos, cosas con Rigidbody o Animator) no se tocan.
public class FogCulling : MonoBehaviour
{
    [Tooltip("Tildado: la distancia se calcula sola a partir de la densidad de la niebla (Lighting > Fog).")]
    [SerializeField] private bool distanceFromFog = true;
    [Tooltip("Distancia a partir de la cual los objetos se apagan (si no se calcula desde la niebla).")]
    [SerializeField] private float cullDistance = 40f;
    [Tooltip("Metros de margen más allá de la niebla total, para que nunca se vea aparecer nada.")]
    [SerializeField] private float margin = 6f;
    [Tooltip("Tildado: la cámara tampoco dibuja nada más allá de esa distancia (Far Clip Plane).")]
    [SerializeField] private bool adjustCameraFarClip = true;
    [SerializeField] private bool debugLogs;

    private enum TargetType { Renderer, Light, Particles }

    private struct Target
    {
        public TargetType type;
        public Component component;
    }

    private CullingGroup group;
    private readonly List<Target> targets = new List<Target>();
    private BoundingSphere[] spheres;
    private UnityEngine.Camera cam;

    public float CullDistance => cullDistance;

    private IEnumerator Start()
    {
        cam = UnityEngine.Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[FogCulling] No hay cámara con el tag MainCamera: no se optimiza nada.", this);
            yield break;
        }

        if (distanceFromFog && RenderSettings.fog)
            cullDistance = FullFogDistance() + margin;
        if (adjustCameraFarClip)
            cam.farClipPlane = Mathf.Max(cam.nearClipPlane + 1f, cullDistance + 10f);

        CollectTargets();

        group = new CullingGroup { targetCamera = cam };
        group.SetBoundingSpheres(spheres);
        group.SetBoundingSphereCount(targets.Count);
        group.SetBoundingDistances(new[] { cullDistance });
        group.SetDistanceReferencePoint(cam.transform);
        group.onStateChanged = OnStateChanged;

        // Al primer frame ya están calculadas las distancias: se aplica el estado inicial de todos
        yield return null;
        int hidden = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            bool near = group.GetDistance(i) == 0;
            SetActive(targets[i], near);
            if (!near) hidden++;
        }
        if (debugLogs)
            Debug.Log($"[FogCulling] {targets.Count} objetos controlados, distancia {cullDistance:F0} m, {hidden} apagados al empezar.", this);
    }

    private void OnDestroy()
    {
        if (group != null)
        {
            group.Dispose();
            group = null;
        }
    }

    // Distancia en la que la niebla tapa el 99% (según el tipo de niebla de Lighting)
    private static float FullFogDistance()
    {
        float density = Mathf.Max(RenderSettings.fogDensity, 0.0001f);
        switch (RenderSettings.fogMode)
        {
            case FogMode.Exponential: return Mathf.Log(100f) / density;
            case FogMode.ExponentialSquared: return Mathf.Sqrt(Mathf.Log(100f)) / density;
            default: return RenderSettings.fogEndDistance;
        }
    }

    // Unity avisa solo cuando un objeto cruza la distancia (banda 0 = cerca, 1 = en la niebla)
    private void OnStateChanged(CullingGroupEvent e)
    {
        if (e.index >= targets.Count) return;
        if (e.currentDistance != e.previousDistance)
            SetActive(targets[e.index], e.currentDistance == 0);
    }

    private static void SetActive(Target target, bool active)
    {
        if (target.component == null) return;
        switch (target.type)
        {
            case TargetType.Renderer:
                ((Renderer)target.component).enabled = active;
                break;
            case TargetType.Light:
                ((Light)target.component).enabled = active;
                break;
            case TargetType.Particles:
                ParticleSystem ps = (ParticleSystem)target.component;
                // Lejos se pausa la simulación (no cuesta nada); cerca sigue desde donde estaba
                if (active) ps.Play(false);
                else ps.Pause(false);
                break;
        }
    }

    // ---------------------------------------------------------------
    // QUÉ SE CONTROLA
    // ---------------------------------------------------------------

    private void CollectTargets()
    {
        List<BoundingSphere> list = new List<BoundingSphere>();

        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!r.enabled || r is SkinnedMeshRenderer || IsExcluded(r.transform)) continue;
            Bounds b = r.bounds;
            Add(list, TargetType.Renderer, r, b.center, b.extents.magnitude);
        }

        foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!l.enabled || l.type == LightType.Directional || IsExcluded(l.transform)) continue;
            // La luz se apaga recién cuando ni siquiera su alcance llega a la zona visible
            Add(list, TargetType.Light, l, l.transform.position, l.range * 0.6f);
        }

        foreach (ParticleSystem ps in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (IsExcluded(ps.transform)) continue;
            ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
            Bounds b = psr != null ? psr.bounds : new Bounds(ps.transform.position, Vector3.one * 5f);
            Add(list, TargetType.Particles, ps, b.center, Mathf.Max(b.extents.magnitude, 3f));
        }

        spheres = list.ToArray();
    }

    private void Add(List<BoundingSphere> list, TargetType type, Component component, Vector3 center, float radius)
    {
        targets.Add(new Target { type = type, component = component });
        list.Add(new BoundingSphere(center, radius));
    }

    // No se tocan: el jugador, la cámara, la UI, la niebla que sigue al jugador, el terreno
    // (no es un Renderer) ni nada que se mueva.
    private bool IsExcluded(Transform t)
    {
        if (t.GetComponentInParent<PlayerController>() != null) return true;
        if (t.GetComponentInParent<UnityEngine.Camera>() != null) return true;
        if (t.GetComponentInParent<Canvas>() != null) return true;
        if (t.GetComponentInParent<AmbientFogFollower>() != null) return true;
        if (t.GetComponentInParent<Rigidbody>() != null) return true;

        // Los FBX traen un Animator vacío aunque no se animen (casas, árboles): solo cuenta como
        // "se mueve" si tiene un Animator Controller asignado
        Animator animator = t.GetComponentInParent<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null) return true;
        return false;
    }
}
