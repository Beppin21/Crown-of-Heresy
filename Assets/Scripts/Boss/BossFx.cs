using UnityEngine;

// Utilidades que comparten el jefe y sus ataques: encontrar al jugador y pegarle, encontrar el
// piso debajo de un punto, las mallas de las señales (disco y aro) y los destellos de luz.
public static class BossFx
{
    private static Transform player;
    private static PlayerStats playerStats;
    private static PlayerCombat playerCombat;
    private static readonly RaycastHit[] groundHits = new RaycastHit[16];
    private static Mesh disc;
    private static Mesh ring;
    private static Mesh sphereMesh;

    // Altura del pecho del caballero sobre sus pies (a dónde apuntan los proyectiles)
    public const float PlayerChestHeight = 1.1f;
    // Radio aproximado del cuerpo del caballero, para que un golpe que lo roza también cuente
    public const float PlayerRadius = 0.4f;

    // El objeto con el tag "Player" (el que tiene PlayerStats). Se busca de nuevo si cambió la escena.
    public static Transform Player
    {
        get
        {
            if (player == null)
            {
                GameObject found = GameObject.FindWithTag("Player");
                player = found != null ? found.transform : null;
                playerStats = found != null ? found.transform.root.GetComponentInChildren<PlayerStats>() : null;
                playerCombat = found != null ? found.transform.root.GetComponentInChildren<PlayerCombat>() : null;
            }
            return player;
        }
    }

    public static Vector3 PlayerChest => Player != null ? Player.position + Vector3.up * PlayerChestHeight : Vector3.zero;

    // ¿El jugador está dentro del círculo (en el piso) de centro "point"? La altura evita que un
    // golpe en el piso le pegue si está muy arriba o muy abajo.
    public static bool PlayerWithin(Vector3 point, float radius, float maxHeightDifference = 2.5f)
    {
        if (Player == null) return false;
        Vector3 offset = Player.position - point;
        if (Mathf.Abs(offset.y) > maxHeightDifference) return false;
        offset.y = 0f;
        return offset.magnitude <= radius + PlayerRadius;
    }

    // Le pega al jugador. Si el golpe se puede bloquear, pasa antes por el bloqueo y el parry de
    // PlayerCombat. Los i-frames de la rodada los respeta PlayerStats (ignora el daño).
    public static void HitPlayer(GameObject attacker, float damage, bool blockable)
    {
        if (Player == null || playerStats == null) return;

        float finalDamage = damage;
        if (blockable && playerCombat != null)
            finalDamage = playerCombat.TryDefend(attacker, damage, 0f);
        if (finalDamage > 0f)
            playerStats.TakeDamage(finalDamage);
    }

    public static bool IsPlayerOrBoss(Collider collider)
    {
        if (collider == null) return false;
        if (Player != null && collider.transform.root == Player.root) return true;
        return collider.GetComponentInParent<BossController>() != null;
    }

    // Punto del piso justo debajo de "around" (sin contar al jugador ni al jefe). Si no encuentra
    // nada, usa la altura fallbackY.
    public static Vector3 GroundPoint(Vector3 around, float fallbackY)
    {
        Vector3 origin = new Vector3(around.x, Mathf.Max(around.y, fallbackY) + 1.5f, around.z);
        Vector3 point = new Vector3(around.x, fallbackY, around.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (IsPlayerOrBoss(groundHits[i].collider)) continue;
            if (groundHits[i].distance < best)
            {
                best = groundHits[i].distance;
                point = groundHits[i].point;
            }
        }
        return point;
    }

    // Disco de radio 1, acostado en el piso (normal hacia arriba)
    public static Mesh Disc
    {
        get
        {
            if (disc == null) disc = BuildRing(0f, 1f, 48, "Disco");
            return disc;
        }
    }

    // Aro finito de radio 1, acostado en el piso
    public static Mesh Ring
    {
        get
        {
            if (ring == null) ring = BuildRing(0.86f, 1f, 64, "Aro");
            return ring;
        }
    }

    private static Mesh BuildRing(float inner, float outer, int segments, string name)
    {
        var vertices = new Vector3[segments * 2];
        var triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = dir * inner;
            vertices[i * 2 + 1] = dir * outer;

            int a = i * 2, b = i * 2 + 1, c = (i * 2 + 2) % (segments * 2), d = (i * 2 + 3) % (segments * 2);
            triangles[i * 6] = a; triangles[i * 6 + 1] = c; triangles[i * 6 + 2] = b;
            triangles[i * 6 + 3] = b; triangles[i * 6 + 4] = c; triangles[i * 6 + 5] = d;
        }
        var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Luz que aparece de golpe y se apaga sola (impactos, explosiones)
    public static void Flash(Vector3 position, Color color, float intensity, float range, float duration)
    {
        GameObject go = new GameObject("Destello");
        go.transform.position = position;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        go.AddComponent<BossFlash>().Init(intensity, duration);
    }

    // Esfera brillante sin collider (proyectiles, estrellas). La malla se saca una sola vez de una
    // esfera primitiva: así los proyectiles nunca tienen collider, ni siquiera el primer frame (si
    // no, los de un mismo anillo se chocaban entre ellos al aparecer).
    public static GameObject GlowSphere(string name, Material material, float diameter)
    {
        if (sphereMesh == null)
        {
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereMesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(primitive);
        }
        GameObject sphere = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        sphere.GetComponent<MeshFilter>().sharedMesh = sphereMesh;
        sphere.transform.localScale = Vector3.one * diameter;
        Renderer renderer = sphere.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return sphere;
    }

    // Material URP Unlit, opaco o transparente (alfa normal, de las dos caras). Lo usan la
    // herramienta del jefe para crear los .mat y el BossController si le falta alguno. Los colores
    // de más de 1 brillan con el bloom de los perfiles.
    public static Material CreateUnlitMaterial(string name, Color color, bool transparent)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        var material = new Material(shader != null ? shader : Shader.Find("Unlit/Color")) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        if (transparent)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return material;
    }

    // Aplica un color (con su alfa) a un renderer sin crear materiales nuevos
    public static void SetColor(Renderer renderer, MaterialPropertyBlock block, Color color)
    {
        renderer.GetPropertyBlock(block);
        block.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(block);
    }
}
