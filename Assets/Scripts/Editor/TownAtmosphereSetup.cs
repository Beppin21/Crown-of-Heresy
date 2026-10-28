using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Herramientas para ambientar la escena del pueblo de noche:
//
//   Tools > Town > Ambientación nocturna (niebla + humo)
//     Luz de luna fría, niebla densa, humo que se mezcla con la niebla alrededor del jugador y
//     post-processing (bloom para el fuego, colores fríos, viñeta, grano de película).
//     Se aplica a la escena abierta. Se puede volver a correr: rehace todo.
//
//   Tools > Town > Prender fuego a la selección
//     Con una casa seleccionada, le agrega un incendio a su medida: llamas, brasas, columna de
//     humo y una luz naranja que parpadea.
//
// Las texturas de humo, llama y chispa se generan por código (no hace falta bajar nada).
public static class TownAtmosphereSetup
{
    private const string VfxFolder = "Assets/Art/VFX";
    private const string SmokeTexturePath = VfxFolder + "/Humo.png";
    private const string FlameTexturePath = VfxFolder + "/Llama.png";
    private const string SparkTexturePath = VfxFolder + "/Chispa.png";
    private const string FogLitMaterialPath = VfxFolder + "/NieblaIluminada.mat";
    private const string FireSmokeMaterialPath = VfxFolder + "/HumoIncendio.mat";
    private const string GlowMaterialPath = VfxFolder + "/ResplandorFuego.mat";
    private const string AshMaterialPath = VfxFolder + "/Ceniza.mat";
    private const string AshTexturePath = VfxFolder + "/Ceniza.png";
    private const string SilhouetteMaterialPath = VfxFolder + "/Silueta.mat";
    private const string SilhouetteControllerPath = VfxFolder + "/Silueta.controller";
    private const string SilhouetteModelPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Models/Armature.fbx";
    private const string SilhouetteIdlePath = "Assets/Animations/Player/UAL2_Standard.fbx";
    private const string FlameMaterialPath = VfxFolder + "/Llama.mat";
    private const string SparkMaterialPath = VfxFolder + "/Chispa.mat";
    private const string ProfilePath = "Assets/Settings/TownNightProfile.asset";

    private const string AtmosphereRootName = "Ambiente_Noche";
    private const string FireRootName = "Incendio";

    private static readonly Color FogColor = new Color(0.10f, 0.11f, 0.13f);

    // ---------------------------------------------------------------
    // AMBIENTACIÓN NOCTURNA
    // ---------------------------------------------------------------

    [MenuItem("Tools/Town/Ambientación nocturna (niebla + humo)")]
    private static void SetupNight()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!EditorUtility.DisplayDialog("Ambientación nocturna",
                $"Se va a ambientar de noche la escena abierta: '{scene.name}'.\n\n" +
                "- Luz de luna, niebla densa y luz ambiente oscura.\n" +
                "- Niebla y humo alrededor del jugador.\n" +
                "- Post-processing (bloom, colores fríos, viñeta, grano).\n\n¿Seguimos?",
                "Sí", "Cancelar"))
            return;

        // Si ya se había corrido, se rehace de cero
        GameObject old = GameObject.Find(AtmosphereRootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        SetupLighting();

        GameObject root = new GameObject(AtmosphereRootName);
        Undo.RegisterCreatedObjectUndo(root, "Ambientación nocturna");
        root.AddComponent<NightAtmosphere>();
        root.AddComponent<FogCulling>(); // lo que queda dentro de la niebla no se dibuja

        // Post-processing
        GameObject volumeObject = new GameObject("PostProcess_Noche");
        volumeObject.transform.SetParent(root.transform, false);
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.sharedProfile = GetOrCreateNightProfile();

        // Niebla y humo que acompañan al jugador
        // Iluminada: cerca de un incendio la niebla se tiñe de naranja y parpadea con el fuego
        Material smoke = GetOrCreateParticleMaterial(FogLitMaterialPath, GetSmokeTexture(), additive: false, softFar: 4f,
                                                     litGlow: FogColor * 0.9f);

        GameObject fogRoot = new GameObject("NieblaYHumo");
        fogRoot.transform.SetParent(root.transform, false);
        fogRoot.AddComponent<AmbientFogFollower>();
        CreateGroundFog(fogRoot.transform, smoke);
        CreateDriftingSmoke(fogRoot.transform, smoke);
        CreateFogWall(fogRoot.transform, smoke);

        // Ceniza que cae lenta alrededor del jugador
        Material ash = GetOrCreateParticleMaterial(AshMaterialPath, GetAshTexture(), additive: false, softFar: 0.2f);
        CreateAsh(fogRoot.transform, ash);

        // Viento con ráfagas (mueve niebla, humo, ceniza, fuego y la lámpara)
        GameObject wind = new GameObject("Viento");
        wind.transform.SetParent(root.transform, false);
        wind.transform.rotation = Quaternion.Euler(0f, 60f, 0f); // hacia dónde sopla
        wind.AddComponent<WindZone>();
        wind.AddComponent<WindGusts>();
        EnableWind(fogRoot.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        Debug.Log("[TownAtmosphere] Ambientación nocturna lista. Ajustá la densidad de la niebla en " +
                  "Window > Rendering > Lighting > Environment > Fog, y los colores en Assets/Settings/TownNightProfile.");
    }

    private static void SetupLighting()
    {
        // Niebla exponencial: densa cerca y se "come" todo a lo lejos
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.06f; // a ~35 m ya no se ve nada (ver FogCulling)
        RenderSettings.fogColor = FogColor;

        // Luz ambiente oscura y fría (de arriba un poco azul, de abajo casi negra)
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.09f, 0.11f, 0.16f);
        RenderSettings.ambientEquatorColor = new Color(0.06f, 0.065f, 0.08f);
        RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.03f);
        RenderSettings.skybox = null; // el fondo lo pinta NightAtmosphere del color de la niebla
        RenderSettings.reflectionIntensity = 0.25f;

        // Luna: la primera luz direccional; si hay más, se apagan (de noche hay una sola)
        Light moon = null;
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            if (moon == null) { moon = light; continue; }
            Undo.RecordObject(light, "Ambientación nocturna");
            light.enabled = false;
            Debug.Log($"[TownAtmosphere] Se apagó la luz direccional extra '{light.name}'.");
        }
        if (moon == null)
        {
            moon = new GameObject("Luna").AddComponent<Light>();
            moon.type = LightType.Directional;
            Undo.RegisterCreatedObjectUndo(moon.gameObject, "Ambientación nocturna");
        }

        Undo.RecordObject(moon, "Ambientación nocturna");
        Undo.RecordObject(moon.transform, "Ambientación nocturna");
        moon.name = "Luna";
        moon.color = new Color(0.55f, 0.65f, 0.85f);
        moon.intensity = 0.3f;
        moon.shadows = LightShadows.Soft;
        moon.shadowStrength = 0.8f;
        moon.transform.rotation = Quaternion.Euler(28f, 210f, 0f);
        RenderSettings.sun = moon;
    }

    // Niebla baja: muchas "nubes" grandes y transparentes pegadas al piso que se mueven despacio
    private static void CreateGroundFog(Transform parent, Material material)
    {
        ParticleSystem ps = CreateSystem("Niebla", parent, material);
        var main = ps.main;
        main.duration = 10f;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 20f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(7f, 13f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.5f, 0.53f, 0.58f, 0.3f);
        main.maxParticles = 350;

        var emission = ps.emission;
        emission.rateOverTime = 20f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(55f, 1.5f, 55f);

        SetFadeInOut(ps);
        SetWind(ps, new Vector2(0.15f, 0.35f), Vector2.zero, new Vector2(-0.05f, 0.1f));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.15f;
        noise.frequency = 0.08f;
        noise.scrollSpeed = 0.05f;

        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
    }

    // Humo a la deriva: menos partículas, más altas y oscuras (olor a incendio en todo el pueblo)
    private static void CreateDriftingSmoke(Transform parent, Material material)
    {
        ParticleSystem ps = CreateSystem("Humo", parent, material);
        ps.transform.localPosition = new Vector3(0f, 2.5f, 0f);

        var main = ps.main;
        main.duration = 10f;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(16f, 24f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(10f, 18f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.24f, 0.22f, 0.21f, 0.18f); // gris tirando a marrón, como humo de madera
        main.maxParticles = 120;

        var emission = ps.emission;
        emission.rateOverTime = 5f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(60f, 4f, 60f);

        SetFadeInOut(ps);
        SetWind(ps, new Vector2(0.3f, 0.6f), new Vector2(0.02f, 0.08f), new Vector2(0f, 0.15f));

        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
    }

    // Ceniza: copitos grises que caen lento y revolotean, en una caja de 36 x 36 m sobre el jugador
    // (como en Silent Hill). Son chiquitos y sin iluminación: casi no cuestan.
    private static void CreateAsh(Transform parent, Material material)
    {
        ParticleSystem ps = CreateSystem("Ceniza", parent, material);
        ps.transform.localPosition = new Vector3(0f, 8f, 0f);

        var main = ps.main;
        main.duration = 10f;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 14f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.76f, 0.72f, 0.85f), new Color(0.5f, 0.48f, 0.46f, 0.85f));
        main.maxParticles = 700;

        var emission = ps.emission;
        emission.rateOverTime = 55f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(36f, 1f, 36f);

        // Caen despacio y revolotean (como papel quemado)
        SetVelocity(ps, new Vector2(-0.1f, 0.1f), new Vector2(-0.45f, -0.25f), new Vector2(-0.1f, 0.1f));
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.2f;

        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

        SetFadeInOut(ps);
    }

    // Prende el módulo "External Forces" de las partículas para que el viento (WindZone) las mueva.
    // Cada tipo reacciona distinto: la ceniza vuela mucho, la niebla espesa casi nada.
    private static void EnableWind(Transform root)
    {
        foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            string name = ps.name;
            float multiplier =
                name == "Ceniza" ? 1.2f :
                name == "Brasas" ? 1f :
                name == "ColumnaDeHumo" ? 0.8f :
                name == "Humo" ? 0.5f :
                name == "ParedDeHumo" ? 0.35f :
                name == "Niebla" ? 0.25f :
                name.StartsWith("Llamas") ? 0.35f :
                name == "Resplandor" ? 0.15f : 0.5f;

            var forces = ps.externalForces;
            forces.enabled = true;
            forces.multiplier = multiplier;
        }
    }

    // Pared de humo: un anillo de nubes grandes y espesas a 12-20 m del jugador. Cuando no hay
    // nada cerca, lo que se ve alrededor es una pared de humo que se mueve, no un vacío liso.
    // (Más lejos no serviría: la niebla del Lighting ya las taparía.)
    private static void CreateFogWall(Transform parent, Material material)
    {
        ParticleSystem ps = CreateSystem("ParedDeHumo", parent, material);
        ps.transform.localPosition = new Vector3(0f, 1.5f, 0f);

        var main = ps.main;
        main.duration = 10f;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(12f, 20f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.36f, 0.38f, 0.42f, 0.38f);
        main.maxParticles = 180;

        var emission = ps.emission;
        emission.rateOverTime = 14f;

        // Anillo horizontal: radio 20 m, emitiendo solo en el 40% exterior (de 12 a 20 m)
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 20f;
        shape.radiusThickness = 0.4f;
        shape.rotation = new Vector3(90f, 0f, 0f);

        SetFadeInOut(ps);
        SetWind(ps, new Vector2(0.2f, 0.5f), new Vector2(-0.05f, 0.1f), new Vector2(-0.1f, 0.1f));

        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        noise.frequency = 0.05f;
        noise.scrollSpeed = 0.05f;
    }

    private static VolumeProfile GetOrCreateNightProfile()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        // Rango de colores de cine (los blancos del fuego no se "queman")
        Tonemapping tonemapping = GetOverride<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.ACES);

        // Brillo alrededor de las llamas y las luces
        Bloom bloom = GetOverride<Bloom>(profile);
        bloom.intensity.Override(0.9f);
        bloom.threshold.Override(1f);
        bloom.scatter.Override(0.7f);

        // Imagen oscura, fría y apagada, con el contraste de Silent Hill
        ColorAdjustments color = GetOverride<ColorAdjustments>(profile);
        color.postExposure.Override(-0.2f);
        color.contrast.Override(18f);
        color.saturation.Override(-35f);
        color.colorFilter.Override(new Color(0.9f, 0.95f, 1f));

        WhiteBalance whiteBalance = GetOverride<WhiteBalance>(profile);
        whiteBalance.temperature.Override(-12f);

        // Bordes oscuros y grano de película
        Vignette vignette = GetOverride<Vignette>(profile);
        vignette.intensity.Override(0.38f);
        vignette.smoothness.Override(0.45f);

        FilmGrain grain = GetOverride<FilmGrain>(profile);
        grain.type.Override(FilmGrainLookup.Medium3);
        grain.intensity.Override(0.35f);

        ChromaticAberration aberration = GetOverride<ChromaticAberration>(profile);
        aberration.intensity.Override(0.08f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    private static T GetOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T component)) return component;

        component = profile.Add<T>(true);
        component.name = typeof(T).Name;
        component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(component, profile); // si no, no se guarda dentro del asset
        return component;
    }

    // ---------------------------------------------------------------
    // INCENDIO
    // ---------------------------------------------------------------

    // Marca la escena abierta como zona de exploración con lámpara (sin combate; clic derecho
    // acerca la cámara a la lámpara). En el Dungeon no se usa.
    // ---------------------------------------------------------------
    // INCENDIO QUE SE PRENDE DESPUÉS
    // ---------------------------------------------------------------

    // Igual que "Prender fuego", pero el incendio arranca apagado y se prende más tarde (por tiempo
    // o al acercarse el jugador), creciendo de a poco. Se configura en el componente Fire Ignition.
    [MenuItem("Tools/Town/Fuego que se prende después (selección)")]
    private static void SetDelayedFire()
    {
        int count = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            if (EditorUtility.IsPersistent(go)) continue;
            GameObject fire = SetOnFire(go);
            if (fire == null) continue;
            fire.AddComponent<FireIgnition>();
            count++;
        }
        if (count == 0)
            EditorUtility.DisplayDialog("Fuego que se prende después", "Seleccioná en la escena la casa (o las casas).", "OK");
        else
            Debug.Log($"[TownAtmosphere] {count} incendio(s) que se prenden después. Por defecto a los 90 s: " +
                      "cambialo en Incendio > Fire Ignition (también se puede prender al acercarse el jugador).");
    }

    // ---------------------------------------------------------------
    // SILUETAS EN LA NIEBLA
    // ---------------------------------------------------------------

    [MenuItem("Tools/Town/Siluetas en la niebla")]
    private static void AddApparitions()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(SilhouetteModelPath);
        if (model == null)
        {
            EditorUtility.DisplayDialog("Siluetas", $"No se encontró el modelo '{SilhouetteModelPath}'.", "OK");
            return;
        }

        GameObject old = GameObject.Find("Siluetas");
        if (old != null) Undo.DestroyObjectImmediate(old);

        GameObject root = new GameObject("Siluetas");
        Undo.RegisterCreatedObjectUndo(root, "Siluetas en la niebla");

        Material black = GetOrCreateSilhouetteMaterial();
        RuntimeAnimatorController idle = GetOrCreateSilhouetteController();

        // Dos siluetas: nunca hay muchas a la vez (y escondidas no gastan nada)
        for (int i = 0; i < 2; i++)
        {
            GameObject figure = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            figure.name = $"Silueta_{i + 1}";

            foreach (Renderer r in figure.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = new Material[r.sharedMaterials.Length];
                for (int m = 0; m < materials.Length; m++) materials[m] = black;
                r.sharedMaterials = materials;
            }
            foreach (Collider c in figure.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);

            Animator animator = figure.GetComponent<Animator>();
            if (animator == null) animator = figure.AddComponent<Animator>();
            animator.runtimeAnimatorController = idle;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;

            figure.AddComponent<FogApparition>();
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("[TownAtmosphere] Siluetas listas: aparecen en el borde de la niebla y desaparecen al acercarse. " +
                  "Distancias y tiempos en cada Silueta > Fog Apparition.");
    }

    // Negro mate: de lejos, en la niebla, es solo una forma oscura
    private static Material GetOrCreateSilhouetteMaterial()
    {
        EnsureFolder(VfxFolder);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(SilhouetteMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, SilhouetteMaterialPath);
        }
        material.SetColor("_BaseColor", new Color(0.015f, 0.015f, 0.018f));
        material.SetFloat("_Smoothness", 0f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // Una sola animación en loop: la postura encorvada "Zombie_Idle_Loop" de Quaternius
    private static RuntimeAnimatorController GetOrCreateSilhouetteController()
    {
        AnimationClip clip = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SilhouetteIdlePath))
        {
            if (asset is AnimationClip c && c.name.EndsWith("Zombie_Idle_Loop")) { clip = c; break; }
        }
        if (clip == null)
            Debug.LogWarning("[TownAtmosphere] No se encontró 'Zombie_Idle_Loop' en el pack de Quaternius: la silueta queda quieta en pose de referencia.");

        if (AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(SilhouetteControllerPath) != null)
            AssetDatabase.DeleteAsset(SilhouetteControllerPath);
        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(SilhouetteControllerPath);
        var state = controller.layers[0].stateMachine.AddState("Quieta");
        state.motion = clip;
        return controller;
    }

    [MenuItem("Tools/Town/Zona: modo lámpara")]
    private static void AddLanternZone()
    {
        ZoneSettings zone = Object.FindFirstObjectByType<ZoneSettings>();
        if (zone == null)
        {
            GameObject go = new GameObject("ZonaConfig");
            Undo.RegisterCreatedObjectUndo(go, "Zona: modo lámpara");
            zone = go.AddComponent<ZoneSettings>();
        }
        Undo.RecordObject(zone, "Zona: modo lámpara");
        zone.lanternMode = true;
        EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
        Selection.activeGameObject = zone.gameObject;
        Debug.Log($"[TownAtmosphere] '{zone.gameObject.scene.name}' está en modo lámpara (sin combate; clic derecho acerca la cámara).");
    }

    [MenuItem("Tools/Town/Prender fuego a la selección")]
    private static void SetHouseOnFire()
    {
        List<GameObject> buildings = new List<GameObject>();
        foreach (GameObject go in Selection.gameObjects)
        {
            if (!EditorUtility.IsPersistent(go)) buildings.Add(go);
        }
        if (buildings.Count == 0)
        {
            EditorUtility.DisplayDialog("Prender fuego", "Seleccioná en la escena la casa (o las casas) que se tienen que incendiar.", "OK");
            return;
        }

        List<GameObject> fires = new List<GameObject>();
        foreach (GameObject building in buildings)
        {
            GameObject fire = SetOnFire(building);
            if (fire != null) fires.Add(fire);
        }
        if (fires.Count > 0) Selection.objects = fires.ToArray();
    }

    private static GameObject SetOnFire(GameObject house)
    {
        if (!TryGetBuildingBody(house, out Bounds bounds, out List<Renderer> parts))
        {
            Debug.LogWarning($"[TownAtmosphere] '{house.name}' no tiene ningún modelo visible para medir.", house);
            return null;
        }

        // Si ya tenía un incendio (corrida anterior), se reemplaza
        Transform oldFire = house.transform.Find(FireRootName);
        if (oldFire != null) Undo.DestroyObjectImmediate(oldFire.gameObject);

        GameObject fire = new GameObject(FireRootName);
        Undo.RegisterCreatedObjectUndo(fire, "Prender fuego");
        fire.transform.SetParent(house.transform, false);
        fire.transform.position = bounds.center;
        fire.transform.rotation = Quaternion.identity;
        // Escala neutra aunque la casa esté escalada: así los tamaños de abajo son en metros reales
        Vector3 houseScale = house.transform.lossyScale;
        fire.transform.localScale = new Vector3(1f / houseScale.x, 1f / houseScale.y, 1f / houseScale.z);

        float footprint = Mathf.Min(bounds.size.x, bounds.size.z);
        float scale = Mathf.Clamp(footprint / 6f, 0.5f, 3f); // tamaño de las llamas según la casa

        // Si hay texturas animadas (flipbooks) de fuego/humo en Assets/Art/VFX/Flipbooks, se usan;
        // si no, las texturas generadas por código.
        Flipbook fireFlipbook = FindFlipbook(FireWords);
        Flipbook smokeFlipbook = FindFlipbook(SmokeWords);

        Material flameMaterial = fireFlipbook != null
            ? GetOrCreateParticleMaterial(FlameFlipbookMaterialPath, fireFlipbook.texture, additive: true, softFar: 0.6f, flipbookBlending: true)
            : GetOrCreateParticleMaterial(FlameMaterialPath, GetFlameTexture(), additive: true, softFar: 0.6f);
        Material sparkMaterial = GetOrCreateParticleMaterial(SparkMaterialPath, GetSparkTexture(), additive: true, softFar: 0.2f);
        // Humo del incendio: iluminado (el fuego lo pinta de naranja desde abajo) y casi sin brillo propio
        Color fireSmokeGlow = new Color(0.025f, 0.022f, 0.02f);
        Material smokeMaterial = smokeFlipbook != null
            ? GetOrCreateParticleMaterial(SmokeFlipbookMaterialPath, smokeFlipbook.texture, additive: false, softFar: 3f, flipbookBlending: true, litGlow: fireSmokeGlow)
            : GetOrCreateParticleMaterial(FireSmokeMaterialPath, GetSmokeTexture(), additive: false, softFar: 3f, litGlow: fireSmokeGlow);
        Material glowMaterial = GetOrCreateParticleMaterial(GlowMaterialPath, GetSparkTexture(), additive: true, softFar: 3f);

        CreateFlames(fire.transform, flameMaterial, fireFlipbook, bounds, parts, scale);
        CreateEmbers(fire.transform, sparkMaterial, bounds, scale);
        ParticleSystem smoke = CreateFireSmoke(fire.transform, smokeMaterial, bounds, scale);
        if (smokeFlipbook != null) ApplyFlipbook(smoke, smokeFlipbook, randomStart: true);
        CreateFireLight(fire.transform, bounds);
        CreateFireGlow(fire.transform, glowMaterial, bounds, scale);
        EnableWind(fire.transform);

        if (fireFlipbook == null)
            Debug.Log("[TownAtmosphere] Para un fuego más realista, poné un flipbook de fuego en " +
                      $"{FlipbookFolder} con la grilla en el nombre (ej.: 'Fuego_8x8.png') y volvé a prender el fuego.");

        EditorSceneManager.MarkSceneDirty(house.scene);
        Debug.Log($"[TownAtmosphere] '{house.name}' se está incendiando. Mové/escalá los hijos de '{FireRootName}' " +
                  "para acomodar las llamas (por ejemplo, que salgan por las ventanas o el techo).", house);
        return fire;
    }

    // El "cuerpo" del edificio: solo las partes que arrancan cerca del piso (así, en el molino,
    // las aspas no cuentan y el fuego sale de la torre, no del aire). Máximo 10 m de alto.
    private static bool TryGetBuildingBody(GameObject building, out Bounds body, out List<Renderer> parts)
    {
        body = default;
        parts = new List<Renderer>();
        Renderer[] renderers = building.GetComponentsInChildren<Renderer>();
        List<Renderer> meshes = new List<Renderer>();
        foreach (Renderer r in renderers)
        {
            if (!(r is ParticleSystemRenderer)) meshes.Add(r);
        }
        if (meshes.Count == 0) return false;

        float groundY = float.MaxValue;
        foreach (Renderer r in meshes) groundY = Mathf.Min(groundY, r.bounds.min.y);

        bool any = false;
        foreach (Renderer r in meshes)
        {
            if (r.bounds.min.y > groundY + 1.5f) continue; // parte elevada (aspas, techo suelto, etc.)
            parts.Add(r);
            if (!any) { body = r.bounds; any = true; } else body.Encapsulate(r.bounds);
        }
        if (!any)
        {
            parts.AddRange(meshes);
            body = meshes[0].bounds;
            foreach (Renderer r in meshes) body.Encapsulate(r.bounds);
        }

        const float maxHeight = 10f;
        if (body.size.y > maxHeight)
            body.SetMinMax(body.min, new Vector3(body.max.x, body.min.y + maxHeight, body.max.z));
        return true;
    }

    // Llamas que salen de la SUPERFICIE de la casa (techo, paredes, aberturas): un emisor por cada
    // pieza grande del modelo. Si el modelo no sirve para eso, se usa una caja como antes.
    private static void CreateFlames(Transform parent, Material material, Flipbook flipbook, Bounds bounds, List<Renderer> parts, float scale)
    {
        // Las piezas más grandes primero (máximo 4 emisores por casa)
        List<MeshRenderer> surfaces = new List<MeshRenderer>();
        foreach (Renderer r in parts)
        {
            if (r is MeshRenderer mr && mr.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                surfaces.Add(mr);
        }
        surfaces.Sort((a, b) => Volume(b.bounds).CompareTo(Volume(a.bounds)));
        if (surfaces.Count > 4) surfaces.RemoveRange(4, surfaces.Count - 4);

        float totalArea = 0f;
        foreach (MeshRenderer mr in surfaces) totalArea += SurfaceArea(mr.bounds);

        if (surfaces.Count == 0)
        {
            ParticleSystem box = CreateFlameSystem("Llamas", parent, material, flipbook, scale, 45f * scale);
            box.transform.position = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.55f, bounds.center.z);
            var shape = box.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(bounds.size.x * 0.75f, bounds.size.y * 0.45f, bounds.size.z * 0.75f);
            return;
        }

        foreach (MeshRenderer surface in surfaces)
        {
            MakeMeshReadable(surface.GetComponent<MeshFilter>().sharedMesh);

            // Más llamas en las piezas más grandes (en total ~45 por segundo para una casa común)
            float share = totalArea > 0f ? SurfaceArea(surface.bounds) / totalArea : 1f / surfaces.Count;
            ParticleSystem ps = CreateFlameSystem($"Llamas_{surface.name}", parent, material, flipbook, scale, 45f * scale * share);
            ps.transform.position = surface.bounds.center;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.MeshRenderer;
            shape.meshRenderer = surface;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle; // desde cualquier punto de la superficie
            shape.normalOffset = 0.15f;                                  // apenas por fuera de la pared
        }
    }

    private static ParticleSystem CreateFlameSystem(string name, Transform parent, Material material, Flipbook flipbook, float scale, float rate)
    {
        ParticleSystem ps = CreateSystem(name, parent, material);

        // Lento: llamas que duran más y suben despacio (antes eran rápidas y nerviosas)
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.4f * scale, 2.8f * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        main.startColor = Color.white;
        main.maxParticles = 500;

        var emission = ps.emission;
        emission.rateOverTime = rate;

        SetVelocity(ps, new Vector2(-0.1f, 0.25f), new Vector2(0.6f * scale, 1.3f * scale), new Vector2(-0.1f, 0.1f));

        var color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        if (flipbook != null)
        {
            // El flipbook ya trae los colores reales del fuego: solo aparece y se apaga suave
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.85f, 0.75f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
        }
        else
        {
            // Blanco-amarillo al nacer → naranja → rojo oscuro → desaparece
            gradient.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f),
                    new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.35f),
                    new GradientColorKey(new Color(0.6f, 0.12f, 0.03f), 0.8f),
                },
                new[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.9f, 0.12f),
                    new GradientAlphaKey(0.5f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
        }
        color.color = gradient;

        // Crece al principio y se achica al final, como una lengua de fuego
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.55f)));

        // Ondulación lenta y amplia (no un temblor)
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.3f;

        if (flipbook != null) ApplyFlipbook(ps, flipbook, randomStart: true);
        return ps;
    }

    // ---------------------------------------------------------------
    // FLIPBOOKS (texturas animadas: una grilla de cuadros de fuego/humo real)
    // ---------------------------------------------------------------

    private const string FlipbookFolder = VfxFolder + "/Flipbooks";
    private const string FlameFlipbookMaterialPath = VfxFolder + "/LlamaFlipbook.mat";
    private const string SmokeFlipbookMaterialPath = VfxFolder + "/HumoFlipbook.mat";
    private static readonly string[] FireWords = { "fire", "fuego", "flame", "llama" };
    private static readonly string[] SmokeWords = { "smoke", "humo" };

    private class Flipbook
    {
        public Texture2D texture;
        public int columns;
        public int rows;
    }

    // Busca en Assets/Art/VFX/Flipbooks una textura con alguna de las palabras y la grilla en el
    // nombre (ej.: "Fuego_8x8.png" = 8 columnas x 8 filas)
    private static Flipbook FindFlipbook(string[] words)
    {
        if (!AssetDatabase.IsValidFolder(FlipbookFolder)) return null;

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { FlipbookFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool matches = false;
            foreach (string word in words) matches |= name.Contains(word);
            if (!matches) continue;

            var grid = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)\s*x\s*(\d+)");
            if (!grid.Success)
            {
                Debug.LogWarning($"[TownAtmosphere] '{Path.GetFileName(path)}' no tiene la grilla en el nombre: " +
                                 "renombralo agregando columnas x filas (ej.: '_8x8').");
                continue;
            }
            int columns = int.Parse(grid.Groups[1].Value);
            int rows = int.Parse(grid.Groups[2].Value);
            string softPath = CreateEdgeFadedFlipbook(path, columns, rows);
            ConfigureVfxTexture(softPath);
            return new Flipbook
            {
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(softPath),
                columns = columns,
                rows = rows,
            };
        }
        return null;
    }

    // Copia del flipbook con el borde de CADA cuadro desvanecido a transparente.
    // Hace falta porque en algunos flipbooks (como la bocanada de humo del Particle Pack) los
    // últimos cuadros llenan toda la celda: la imagen se corta en línea recta en el borde y la
    // partícula se ve como un rectángulo. El original no se toca; la copia va a Assets/Art/VFX.
    private static string CreateEdgeFadedFlipbook(string sourcePath, int columns, int rows)
    {
        string outputPath = $"{VfxFolder}/{Path.GetFileNameWithoutExtension(sourcePath)}_Suave.png";
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath) != null &&
            File.GetLastWriteTimeUtc(outputPath) >= File.GetLastWriteTimeUtc(sourcePath))
            return outputPath; // ya existe y está al día

        // Para leer los píxeles (sirve para cualquier formato, incluso .tif) se reimporta el
        // original como legible y sin comprimir, y después se lo deja como estaba
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
        bool wasReadable = importer.isReadable;
        TextureImporterCompression oldCompression = importer.textureCompression;
        int oldMaxSize = importer.maxTextureSize;
        importer.isReadable = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 8192;
        importer.SaveAndReimport();

        Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        Color32[] pixels = source.GetPixels32();
        int width = source.width, height = source.height;

        importer.isReadable = wasReadable;
        importer.textureCompression = oldCompression;
        importer.maxTextureSize = oldMaxSize;
        importer.SaveAndReimport();

        float cellWidth = width / (float)columns;
        float cellHeight = height / (float)rows;
        for (int y = 0; y < height; y++)
        {
            float v = (y % cellHeight) / cellHeight; // posición dentro del cuadro, 0..1
            for (int x = 0; x < width; x++)
            {
                float u = (x % cellWidth) / cellWidth;

                // Fundido en los 4 bordes del cuadro + un poco de redondeo en las esquinas
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float square = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.14f, edge));
                float radius = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                float round = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1.1f, radius));

                int i = y * width + x;
                pixels[i].a = (byte)Mathf.RoundToInt(pixels[i].a * square * round);
            }
        }

        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.SetPixels32(pixels);
        EnsureFolder(VfxFolder);
        File.WriteAllBytes(outputPath, result.EncodeToPNG());
        Object.DestroyImmediate(result);
        AssetDatabase.ImportAsset(outputPath);

        // Mismo tamaño máximo que el original (los flipbooks suelen ser de 2048 o más)
        if (AssetImporter.GetAtPath(outputPath) is TextureImporter outputImporter)
        {
            outputImporter.maxTextureSize = Mathf.Max(2048, Mathf.NextPowerOfTwo(Mathf.Max(width, height)));
            outputImporter.SaveAndReimport();
        }
        Debug.Log($"[TownAtmosphere] Flipbook con bordes suaves: {outputPath}");
        return outputPath;
    }

    // Reproduce la grilla de cuadros a lo largo de la vida de cada partícula. Cada una arranca en
    // un cuadro distinto, así no se ven todas iguales.
    private static void ApplyFlipbook(ParticleSystem ps, Flipbook flipbook, bool randomStart)
    {
        var sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = flipbook.columns;
        sheet.numTilesY = flipbook.rows;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        int frames = flipbook.columns * flipbook.rows;
        sheet.startFrame = randomStart ? new ParticleSystem.MinMaxCurve(0f, frames * 0.3f) : new ParticleSystem.MinMaxCurve(0f);
        sheet.cycleCount = 1;

        // "Flipbook blending": mezcla suave entre cuadros (necesita estos datos extra por partícula).
        // Normal: la usan los materiales iluminados para calcular cómo les pega la luz.
        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.UV2,
            ParticleSystemVertexStream.AnimBlend,
        });
    }

    // El sistema de partículas solo puede emitir desde una malla que se pueda leer (Read/Write)
    private static void MakeMeshReadable(Mesh mesh)
    {
        if (mesh == null || mesh.isReadable) return;
        string path = AssetDatabase.GetAssetPath(mesh);
        if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
    }

    private static float Volume(Bounds b) => b.size.x * b.size.y * b.size.z;

    private static float SurfaceArea(Bounds b) => 2f * (b.size.x * b.size.y + b.size.y * b.size.z + b.size.x * b.size.z);

    private static void CreateEmbers(Transform parent, Material material, Bounds bounds, float scale)
    {
        ParticleSystem ps = CreateSystem("Brasas", parent, material);
        ps.transform.position = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.6f, bounds.center.z);

        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4.5f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.7f, 0.3f), new Color(1f, 0.35f, 0.05f));
        main.maxParticles = 300;

        var emission = ps.emission;
        emission.rateOverTime = 25f * scale;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(bounds.size.x * 0.8f, bounds.size.y * 0.5f, bounds.size.z * 0.8f);

        // Suben rápido, empujadas por el calor, y las arrastra el viento
        SetVelocity(ps, new Vector2(0.2f, 1f), new Vector2(2f, 5f), new Vector2(-0.4f, 0.4f));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 1.2f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.8f;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    private static ParticleSystem CreateFireSmoke(Transform parent, Material material, Bounds bounds, float scale)
    {
        ParticleSystem ps = CreateSystem("ColumnaDeHumo", parent, material);
        ps.transform.position = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);

        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(2.5f * scale, 4.5f * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.13f, 0.12f, 0.11f, 0.7f);
        main.maxParticles = 250;

        var emission = ps.emission;
        emission.rateOverTime = 9f * scale;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(bounds.size.x * 0.5f, 0.5f, bounds.size.z * 0.5f);

        // Sube y el viento la va inclinando hacia un costado
        SetVelocity(ps, new Vector2(0.5f, 1.2f), new Vector2(1.8f, 3f), new Vector2(0f, 0.3f));

        // Se agranda mientras sube y se va diluyendo
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.5f)));

        SetFadeInOut(ps);

        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.15f;
        noise.scrollSpeed = 0.2f;
        return ps;
    }

    private static void CreateFireLight(Transform parent, Bounds bounds)
    {
        GameObject lightObject = new GameObject("LuzDelFuego");
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.position = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.6f, bounds.center.z);

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.5f, 0.15f);
        light.range = Mathf.Max(bounds.size.x, bounds.size.z) * 2.5f + 6f;
        light.intensity = 6f;
        light.shadows = LightShadows.Soft;

        lightObject.AddComponent<FireFlicker>();
    }

    // Resplandor: unas pocas "nubes" de luz naranja, enormes y muy tenues, alrededor del fuego.
    // Imita cómo la luz del incendio se dispersa en el humo y la niebla (se ve sobre todo de
    // lejos: un halo naranja entre la niebla antes de ver las llamas).
    private static void CreateFireGlow(Transform parent, Material material, Bounds bounds, float scale)
    {
        ParticleSystem ps = CreateSystem("Resplandor", parent, material);
        // Arriba del techo: si estuviera adentro de la casa, de frente lo taparían las paredes
        ps.transform.position = new Vector3(bounds.center.x, bounds.max.y + 1f, bounds.center.z);

        var main = ps.main;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(10f * scale, 16f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.12f, 0.1f), new Color(1f, 0.3f, 0.06f, 0.16f));
        main.maxParticles = 20;

        var emission = ps.emission;
        emission.rateOverTime = 4f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.3f;

        SetVelocity(ps, new Vector2(-0.1f, 0.1f), new Vector2(0.2f, 0.6f), new Vector2(-0.1f, 0.1f));
        SetFadeInOut(ps);

        // Que "respire" con el fuego: crece y se achica un poco
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.8f), new Keyframe(0.5f, 1.05f), new Keyframe(1f, 0.9f)));
    }

    // ---------------------------------------------------------------
    // PARTÍCULAS: utilidades
    // ---------------------------------------------------------------

    private static ParticleSystem CreateSystem(string name, Transform parent, Material material)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // las partículas no se mueven con el emisor
        main.scalingMode = ParticleSystemScalingMode.Local;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.maxParticleSize = 3f; // permite partículas grandes cerca de la cámara (niebla)
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // Cada partícula mira a la POSICIÓN de la cámara (no copia su rotación): así, al girar la
        // cámara, el humo no gira ni "nada", no se corta contra las paredes y no parpadea en los
        // costados de la pantalla.
        renderer.alignment = ParticleSystemRenderSpace.Facing;

        // Partículas iluminadas: normales "esféricas" (cada partícula se ilumina como una bola de
        // humo). Con normales planas, una luz que queda del otro lado no la iluminaba, y el
        // naranja del fuego aparecía o se perdía según hacia dónde mirara la cámara.
        if (material != null && material.shader != null && !material.shader.name.EndsWith("Unlit"))
            renderer.normalDirection = 0f;

        return ps;
    }

    // Aparece y desaparece suave (sin "pops")
    private static void SetFadeInOut(ParticleSystem ps)
    {
        var color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
    }

    // Viento: velocidad constante en mundo (x/z = horizontal, y = vertical), con un rango al azar
    private static void SetWind(ParticleSystem ps, Vector2 x, Vector2 y, Vector2 z) => SetVelocity(ps, x, y, z);

    private static void SetVelocity(ParticleSystem ps, Vector2 x, Vector2 y, Vector2 z)
    {
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // Los tres ejes tienen que usar el mismo modo de curva (acá: dos constantes)
        velocity.x = new ParticleSystem.MinMaxCurve(x.x, x.y);
        velocity.y = new ParticleSystem.MinMaxCurve(y.x, y.y);
        velocity.z = new ParticleSystem.MinMaxCurve(z.x, z.y);
    }

    // Material de partículas de URP. additive = brilla y se suma (fuego, chispas);
    // si no, mezcla normal con transparencia (humo, niebla).
    // softFar = distancia en la que la partícula se desvanece al tocar el piso o una pared
    // (evita el corte duro donde la partícula atraviesa la geometría).
    // litGlow (solo humo/niebla): si se pasa, el material es ILUMINADO: recibe la luz del fuego
    // (se tiñe de naranja y parpadea con él). litGlow es el brillo propio que tiene en la
    // oscuridad, para que lejos de las luces se vea igual que la niebla y no negro.
    private static Material GetOrCreateParticleMaterial(string path, Texture2D texture, bool additive, float softFar,
                                                        bool flipbookBlending = false, Color? litGlow = null)
    {
        EnsureFolder(VfxFolder);
        bool lit = litGlow.HasValue && !additive;
        Shader shader = Shader.Find(lit ? "Universal Render Pipeline/Particles/Lit" : "Universal Render Pipeline/Particles/Unlit");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;

        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);

        if (lit)
        {
            // Humo mate (sin reflejos) que igual "brilla" un poco solo, como la niebla
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_Metallic", 0f);
            material.SetColor("_EmissionColor", litGlow.Value);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        // Transparente
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", additive ? 2f : 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        // "Preserve Specular Lighting" viene prendido por defecto en los shaders iluminados de URP.
        // Cuando Unity revalida el material (al recompilar o al abrirlo en el Inspector), con eso
        // prendido lo pasa a mezcla "premultiplicada": el brillo propio (emisión) ya no se recorta
        // con la transparencia de la textura y se pinta sobre TODA la partícula cuadrada
        // (se veían rectángulos de humo). Apagado, la mezcla queda normal siempre.
        material.SetFloat("_BlendModePreserveSpecular", 0f);
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.renderQueue = (int)RenderQueue.Transparent;

        // Partículas suaves: se desvanecen al tocar la geometría
        material.SetFloat("_SoftParticlesEnabled", 1f);
        material.SetFloat("_SoftParticlesNearFadeDistance", 0f);
        material.SetFloat("_SoftParticlesFarFadeDistance", softFar);
        material.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / softFar, 0f, 0f));
        material.EnableKeyword("_SOFTPARTICLES_ON");

        // Se desvanecen al acercarse a la cámara (que la niebla no tape la pantalla de golpe)
        material.SetFloat("_CameraFadingEnabled", 1f);
        material.SetFloat("_CameraNearFadeDistance", 0.5f);
        material.SetFloat("_CameraFarFadeDistance", 2.5f);
        material.SetVector("_CameraFadeParams", new Vector4(0.5f, 1f / 2f, 0f, 0f));
        material.EnableKeyword("_FADING_ON");

        // Flipbook: mezcla suave entre un cuadro y el siguiente (sin "saltos" con la animación lenta)
        material.SetFloat("_FlipbookBlending", flipbookBlending ? 1f : 0f);
        if (flipbookBlending) material.EnableKeyword("_FLIPBOOKBLENDING_ON");
        else material.DisableKeyword("_FLIPBOOKBLENDING_ON");

        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------------------------------------------------------------
    // TEXTURAS GENERADAS
    // ---------------------------------------------------------------

    // Bocanada de humo: círculo difuso con "nubosidad" adentro
    private static Texture2D GetSmokeTexture()
    {
        return GetOrCreateTexture(SmokeTexturePath, 256, 256, (u, v) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float falloff = Mathf.Clamp01(1f - r);
            falloff = falloff * falloff * (3f - 2f * falloff); // smoothstep
            float cloud = Fbm(u * 4f, v * 4f, 5);
            float alpha = Mathf.Clamp01(falloff * (0.35f + cloud * 0.9f));
            return new Color(1f, 1f, 1f, alpha);
        });
    }

    // Lengua de fuego: ancha abajo, finita arriba, con bordes irregulares
    private static Texture2D GetFlameTexture()
    {
        return GetOrCreateTexture(FlameTexturePath, 128, 256, (u, v) =>
        {
            float width = Mathf.Lerp(0.42f, 0.04f, Mathf.Pow(v, 0.8f));
            float distortion = (Fbm(u * 3f, v * 4f + 10f, 4) - 0.5f) * 0.25f;
            float dx = Mathf.Abs(u - 0.5f + distortion * v) / width;
            float body = Mathf.Clamp01(1f - dx);
            body *= Mathf.Clamp01(v * 8f);                     // borde de abajo suave
            body *= Mathf.Clamp01((1f - v) * 1.5f);            // se apaga hacia arriba
            float alpha = Mathf.Clamp01(Mathf.Pow(body, 1.5f) * (0.7f + Fbm(u * 6f, v * 6f, 3) * 0.6f));
            return new Color(1f, 1f, 1f, alpha);
        });
    }

    // Chispa: punto brillante con halo
    // Copo de ceniza: manchita irregular, con bordes desparejos
    private static Texture2D GetAshTexture()
    {
        return GetOrCreateTexture(AshTexturePath, 64, 64, (u, v) =>
        {
            Vector2 p = new Vector2(u - 0.5f, v - 0.5f);
            float angle = Mathf.Atan2(p.y, p.x);
            float radius = 0.32f + (Mathf.PerlinNoise(Mathf.Cos(angle) * 2f + 5f, Mathf.Sin(angle) * 2f + 5f) - 0.5f) * 0.25f;
            float edge = 1f - Mathf.SmoothStep(radius - 0.08f, radius, p.magnitude);
            float grain = 0.75f + Fbm(u * 8f, v * 8f, 2) * 0.25f;
            return new Color(1f, 1f, 1f, Mathf.Clamp01(edge * grain));
        });
    }

    private static Texture2D GetSparkTexture()
    {
        return GetOrCreateTexture(SparkTexturePath, 64, 64, (u, v) =>
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float alpha = Mathf.Clamp01(Mathf.Pow(Mathf.Clamp01(1f - r), 2.5f));
            return new Color(1f, 1f, 1f, alpha);
        });
    }

    private static Texture2D GetOrCreateTexture(string path, int width, int height, System.Func<float, float, Color> pixel)
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
        {
            EnsureFolder(VfxFolder);
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    texture.SetPixel(x, y, pixel((x + 0.5f) / width, (y + 0.5f) / height));
            }
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
        }

        ConfigureVfxTexture(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Configuración de import para texturas de humo/fuego (también los flipbooks):
    //   - Compresión de alta calidad: la normal (DXT) comprime en bloques de 4x4 píxeles y en los
    //     degradés suaves de transparencia del humo se ven "escalones" y bloques (aspecto poligonal).
    //   - Clamp: en los flipbooks, con Repeat se cuela el borde del cuadro vecino.
    //   - Trilineal: sin saltos bruscos al cambiar de tamaño de mipmap a la distancia.
    //   - Alpha Is Transparency: sin bordes oscuros alrededor de las partes transparentes.
    private static void ConfigureVfxTexture(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;

        bool changed = false;
        void Set<T>(T current, T wanted, System.Action<T> apply)
        {
            if (Equals(current, wanted)) return;
            apply(wanted);
            changed = true;
        }

        Set(importer.alphaIsTransparency, true, v => importer.alphaIsTransparency = v);
        Set(importer.wrapMode, TextureWrapMode.Clamp, v => importer.wrapMode = v);
        Set(importer.mipmapEnabled, true, v => importer.mipmapEnabled = v);
        Set(importer.filterMode, FilterMode.Trilinear, v => importer.filterMode = v);
        Set(importer.textureCompression, TextureImporterCompression.CompressedHQ, v => importer.textureCompression = v);
        Set(importer.streamingMipmaps, false, v => importer.streamingMipmaps = v);
        Set(importer.mipMapsPreserveCoverage, false, v => importer.mipMapsPreserveCoverage = v);

        if (changed) importer.SaveAndReimport();
    }

    // Ruido fractal (varias capas de Perlin): da el aspecto "nuboso"
    private static float Fbm(float x, float y, int octaves)
    {
        float value = 0f, amplitude = 0.5f, frequency = 1f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            value += Mathf.PerlinNoise(x * frequency + 17.3f * i, y * frequency + 9.1f * i) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }
        return value / total;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
