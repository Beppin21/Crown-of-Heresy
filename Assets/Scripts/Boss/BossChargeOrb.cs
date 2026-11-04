using UnityEngine;

// Aviso de "va a atacar": una esfera brillante del color del ataque que crece en la cabeza del
// jefe, con una luz fuerte que late cada vez más rápido hasta el ataque. Como esa luz se apaga con
// la distancia (y la cabeza está alta y lejos), además prende una luz direccional del mismo color
// que tiñe toda la sala parejo, jugador incluido (roomTint). La crea el ataque al empezar a
// cargar, sigue sola a la cabeza (follow: la cabeza se mueve con la animación) y se apaga con
// Release cuando termina.
public class BossChargeOrb : MonoBehaviour
{
    private System.Func<Vector3> follow;
    private Light orbLight;
    private Light tintLight;
    private float size;
    private float intensity;
    private float tint;
    private float duration;
    private float elapsed;
    private float releaseDuration = -1f; // menor que 0: todavía cargando o sostenida
    private float releaseElapsed;
    private float scaleAtRelease;
    private float levelAtRelease;        // 0..1 de luz al soltar

    public static BossChargeOrb Spawn(BossController boss, System.Func<Vector3> follow, Material material, Color color,
                                      float size, float intensity, float range, float duration, float roomTint = 0f)
    {
        GameObject sphere = BossFx.GlowSphere("Aviso del ataque", material, 0f);
        sphere.transform.position = follow();
        // La esfera toma el color del aviso (más de 1 para que brille con el bloom), sea cual sea
        // el material
        BossFx.SetColor(sphere.GetComponent<Renderer>(), new MaterialPropertyBlock(), new Color(color.r * 4f, color.g * 4f, color.b * 4f, 1f));
        Color lightColor = new Color(color.r, color.g, color.b);

        Light light = sphere.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = lightColor;
        light.range = range;
        light.intensity = 0f;
        light.shadows = LightShadows.None;

        BossChargeOrb orb = sphere.AddComponent<BossChargeOrb>();
        orb.follow = follow;
        orb.orbLight = light;
        orb.size = size;
        orb.intensity = intensity;
        orb.duration = Mathf.Max(0.05f, duration);

        if (roomTint > 0f)
        {
            // Direccional: no pierde fuerza con la distancia, ilumina todo desde arriba y de costado
            GameObject tintObject = new GameObject("Tinte del aviso");
            tintObject.transform.SetParent(sphere.transform, false);
            tintObject.transform.rotation = Quaternion.Euler(65f, 30f, 0f);
            Light tintLight = tintObject.AddComponent<Light>();
            tintLight.type = LightType.Directional;
            tintLight.color = lightColor;
            tintLight.intensity = 0f;
            tintLight.shadows = LightShadows.None;
            orb.tintLight = tintLight;
            orb.tint = roomTint;
        }

        boss.Track(sphere);
        return orb;
    }

    // Se achica, se apaga en "seconds" y se borra
    public void Release(float seconds)
    {
        if (releaseDuration >= 0f) return;
        releaseDuration = Mathf.Max(0.01f, seconds);
        scaleAtRelease = transform.localScale.x;
        levelAtRelease = intensity > 0f ? orbLight.intensity / intensity : 0f;
    }

    private void Update()
    {
        if (follow != null) transform.position = follow();

        float dt = Time.deltaTime;
        if (releaseDuration >= 0f)
        {
            releaseElapsed += dt;
            float fade = 1f - Mathf.Clamp01(releaseElapsed / releaseDuration);
            transform.localScale = Vector3.one * (scaleAtRelease * fade);
            SetLevel(levelAtRelease * fade);
            if (fade <= 0f) Destroy(gameObject);
            return;
        }

        // Arranca ya visible (40% del tamaño), crece rápido al principio y late cada vez más rápido
        // a medida que se acerca el ataque
        elapsed += dt;
        float k = Mathf.Clamp01(elapsed / duration);
        float grow = Mathf.Lerp(0.4f, 1f, 1f - (1f - k) * (1f - k));
        float pulse = 1f + 0.18f * Mathf.Sin(elapsed * Mathf.Lerp(8f, 30f, k));
        transform.localScale = Vector3.one * (size * grow * pulse);
        SetLevel(grow * pulse);
    }

    private void SetLevel(float level)
    {
        orbLight.intensity = intensity * level;
        if (tintLight != null) tintLight.intensity = tint * level;
    }
}
