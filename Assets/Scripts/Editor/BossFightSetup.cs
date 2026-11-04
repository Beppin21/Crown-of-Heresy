using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Arma la pelea del jefe en la escena abierta (el Dungeon): Tools > Jefe > Armar pelea del jefe.
//
//   1. Busca el objeto "Boss" y su esqueleto (la fase 1). Pone "Boss" en el piso, justo debajo del
//      esqueleto, agranda el esqueleto a PhaseOneScale y le apaga el plano azul de fondo que traía
//      el modelo ("bg_plane").
//   2. Pone en loop la animación del esqueleto (la toma "anim" del FBX), con su controller.
//   3. Pone a Astraía (Assets/Prefabs/Boss/Astraia_Fase2.prefab, la arma Tools > Jefe > Armar
//      Astraía) como hija del jefe y apagada: aparece en la fase 2. La Astraía suelta que había en
//      la escena (el FBX sin animación) queda apagada.
//   4. Arma el cuerpo que recibe los golpes ("Hurtbox", con una cápsula por fase) y los materiales
//      de los efectos (Assets/Prefabs/Boss/Materials).
//   5. Agrega el BossController y los cinco ataques, y conecta todo.
//   6. Pone el punto de reaparición ("BossRespawn") hacia el inicio del jugador, antes de la
//      primera pared y fuera del radio en el que empieza la pelea si entra.
//
// Se puede correr de nuevo: reusa lo que ya existe y no toca los números de los componentes que ya
// estaban. Los valores del código (vida, daño, tiempos, pausas) llegan solos a la escena cuando se
// sube BossController.TuningVersion (ver UpdateOutdatedTuning), y se pueden volver a poner a mano
// con Tools > Jefe > Restablecer valores del jefe. Después hay que guardar la escena (Ctrl+S).
[InitializeOnLoad]
public static class BossFightSetup
{
    private const string BossName = "Boss";
    private const string SkeletonModelPath = "Assets/Models/Final_Boss/Phase_1/source/skeleton.fbx";
    private const string AstraiaModelPath = "Assets/Models/Final_Boss/Phase_2/source/Astraía.fbx";
    private const string AstraiaPrefabPath = "Assets/Prefabs/Boss/Astraia_Fase2.prefab";
    private const string SkeletonControllerPath = "Assets/Prefabs/Boss/Esqueleto_Fase1.controller";
    private const string MaterialDir = "Assets/Prefabs/Boss/Materials";
    private const string FontPath = "Assets/fonts/SemiBold.asset"; // Cormorant Garamond SemiBold (la del menú)
    private const string ImpactEffectPath = "Assets/Vefects/Free Fire VFX URP/Particles/VFX_Fire_Floor_01_Simple.prefab";

    private const float PhaseOneScale = 2.4f;     // el esqueleto mide 2.25 m: queda de unos 5.4 m
    private const float DefaultAggroRadius = 14f; // el del BossController
    private const float RespawnDistance = 18f;    // a cuánto del jefe se reaparece, si entra en la sala

    [MenuItem("Tools/Jefe/Armar pelea del jefe")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Jefe", "Salí del modo Play antes de armar la pelea.", "OK");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        GameObject boss = scene.GetRootGameObjects().FirstOrDefault(g => g.name == BossName);
        if (boss == null)
        {
            EditorUtility.DisplayDialog("Jefe", $"No hay un objeto '{BossName}' en la escena abierta ('{scene.name}'). Abrí el Dungeon.", "OK");
            return;
        }

        var report = new List<string>();
        Undo.RegisterFullObjectHierarchyUndo(boss, "Armar pelea del jefe");

        // 1. Esqueleto (fase 1)
        GameObject skeletonAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SkeletonModelPath);
        Transform skeleton = FindInstanceChild(boss.transform, skeletonAsset) ?? boss.transform.Find("skeleton");
        if (skeleton == null)
        {
            EditorUtility.DisplayDialog("Jefe", "El objeto 'Boss' no tiene adentro el esqueleto (skeleton.fbx).", "OK");
            return;
        }

        SetupSkeletonAnimation(report);
        skeleton = FindInstanceChild(boss.transform, skeletonAsset) ?? boss.transform.Find("skeleton"); // por si el reimport lo cambió

        Physics.SyncTransforms();
        Vector3 skeletonPosition = skeleton.position;
        Vector3 ground = GroundBelow(skeletonPosition + Vector3.up * 3f, boss.transform)
                         ?? new Vector3(skeletonPosition.x, boss.transform.position.y, skeletonPosition.z);
        boss.transform.position = ground;
        skeleton.localPosition = Vector3.zero;
        skeleton.localScale = Vector3.one * PhaseOneScale;
        report.Add($"Jefe en el piso en {Format(ground)}; esqueleto x{PhaseOneScale}.");

        Transform background = skeleton.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "bg_plane");
        if (background != null && background.gameObject.activeSelf)
        {
            background.gameObject.SetActive(false);
            report.Add("Apagué el plano azul de fondo del esqueleto (bg_plane).");
        }

        Animator skeletonAnimator = skeleton.GetComponent<Animator>();
        if (skeletonAnimator == null) skeletonAnimator = Undo.AddComponent<Animator>(skeleton.gameObject);
        skeletonAnimator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(SkeletonControllerPath);
        skeletonAnimator.applyRootMotion = false;
        skeletonAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // las manos se mueven aunque no se vean

        // 3. Astraía (fase 2)
        GameObject astraiaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AstraiaPrefabPath);
        if (astraiaPrefab == null)
        {
            BossAstraiaSetup.Build();
            astraiaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AstraiaPrefabPath);
        }
        if (astraiaPrefab == null)
        {
            EditorUtility.DisplayDialog("Jefe", $"No pude armar '{AstraiaPrefabPath}'. Probá Tools > Jefe > Armar Astraía (fase 2).", "OK");
            return;
        }

        Transform phaseTwo = FindInstanceChild(boss.transform, astraiaPrefab);
        if (phaseTwo == null)
        {
            phaseTwo = ((GameObject)PrefabUtility.InstantiatePrefab(astraiaPrefab, boss.transform)).transform;
            Undo.RegisterCreatedObjectUndo(phaseTwo.gameObject, "Armar pelea del jefe");
        }
        phaseTwo.localPosition = Vector3.zero;
        phaseTwo.localRotation = Quaternion.identity;
        phaseTwo.gameObject.SetActive(true); // prendida un momento para medirla
        Bounds astraiaBounds = RendererBounds(phaseTwo.gameObject);
        phaseTwo.gameObject.SetActive(false);

        GameObject astraiaModel = AssetDatabase.LoadAssetAtPath<GameObject>(AstraiaModelPath);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == boss || !root.activeSelf) continue;
            if (PrefabUtility.GetCorrespondingObjectFromSource(root) != astraiaModel) continue;
            Undo.RecordObject(root, "Armar pelea del jefe");
            root.SetActive(false);
            report.Add($"Apagué la Astraía suelta de la escena ('{root.name}'): ahora aparece desde el jefe en la fase 2.");
        }

        // 4. Cuerpo que recibe los golpes y materiales
        Bounds skeletonBounds = BodyBounds(skeleton.gameObject);
        Transform hurtboxTransform = boss.transform.Find("Hurtbox");
        if (hurtboxTransform == null)
        {
            var go = new GameObject("Hurtbox");
            Undo.RegisterCreatedObjectUndo(go, "Armar pelea del jefe");
            go.transform.SetParent(boss.transform, false);
            hurtboxTransform = go.transform;
        }
        hurtboxTransform.localPosition = Vector3.zero;
        BossHurtbox hurtbox = GetOrAdd<BossHurtbox>(hurtboxTransform.gameObject);
        Rigidbody body = GetOrAdd<Rigidbody>(hurtboxTransform.gameObject);
        body.isKinematic = true;
        body.useGravity = false;

        float groundY = boss.transform.position.y;
        CapsuleCollider phaseOneHitbox = SetupCapsule(hurtboxTransform, "Cuerpo fase 1",
            Mathf.Max(0.9f, Mathf.Min(skeletonBounds.extents.x, skeletonBounds.extents.z) + 0.5f),
            Mathf.Max(3f, skeletonBounds.max.y - groundY + 1f));
        CapsuleCollider phaseTwoHitbox = SetupCapsule(hurtboxTransform, "Cuerpo fase 2",
            Mathf.Clamp(Mathf.Min(astraiaBounds.extents.x, astraiaBounds.extents.z) * 0.5f, 1f, 3f),
            Mathf.Max(4f, astraiaBounds.max.y - groundY));
        report.Add($"Cuerpo que recibe golpes: cápsula de {phaseOneHitbox.height:0.0} m (fase 1) y {phaseTwoHitbox.height:0.0} m (fase 2).");

        Directory.CreateDirectory(MaterialDir);
        Material telegraph = GetOrCreateMaterial("Jefe Senal", new Color(1f, 0.12f, 0.05f, 0.4f), true);
        Material bolt = GetOrCreateMaterial("Jefe Rayo", new Color(1f, 0.35f, 0.1f) * 4f, false);
        Material orb = GetOrCreateMaterial("Jefe Esfera", new Color(0.4f, 0.7f, 1f) * 4f, false);
        Material star = GetOrCreateMaterial("Jefe Estrella", new Color(1f, 0.85f, 0.5f) * 4f, false);

        // 5. Controlador y ataques
        BossController controller = GetOrAdd<BossController>(boss);
        GetOrAdd<BossArmSlam>(boss);
        GetOrAdd<BossHaloBolts>(boss);
        GetOrAdd<BossProjectileRings>(boss);
        GetOrAdd<BossShockwave>(boss);
        GetOrAdd<BossStarRain>(boss);

        var so = new SerializedObject(controller);
        so.FindProperty("phaseOneModel").objectReferenceValue = skeleton.gameObject;
        so.FindProperty("phaseTwoModel").objectReferenceValue = phaseTwo.gameObject;
        so.FindProperty("phaseTwoCenterHeight").floatValue = Mathf.Max(1f, astraiaBounds.center.y - groundY);
        so.FindProperty("hurtbox").objectReferenceValue = hurtbox;
        so.FindProperty("phaseOneHitbox").objectReferenceValue = phaseOneHitbox;
        so.FindProperty("phaseTwoHitbox").objectReferenceValue = phaseTwoHitbox;
        so.FindProperty("nameFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontPath);
        so.FindProperty("telegraphMaterial").objectReferenceValue = telegraph;
        so.FindProperty("boltMaterial").objectReferenceValue = bolt;
        so.FindProperty("orbMaterial").objectReferenceValue = orb;
        so.FindProperty("starMaterial").objectReferenceValue = star;
        so.FindProperty("impactEffect").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactEffectPath);
        so.ApplyModifiedPropertiesWithoutUndo();

        var hurtboxSo = new SerializedObject(hurtbox);
        hurtboxSo.FindProperty("boss").objectReferenceValue = controller;
        hurtboxSo.ApplyModifiedPropertiesWithoutUndo();

        // 6. Punto de reaparición
        PlaceRespawnPoint(boss, controller, report);

        // Un jefe recién agregado (o uno de una versión vieja) queda con los valores del código
        if (new SerializedObject(controller).FindProperty("tuningVersion").intValue < BossController.TuningVersion)
            ApplyCodeDefaults(controller, report);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = boss;
        string summary = string.Join("\n", report.Select(line => "- " + line));
        Debug.Log($"[BossFightSetup] Pelea del jefe armada en '{scene.name}':\n{summary}");
        EditorUtility.DisplayDialog("Jefe", $"Pelea armada en '{scene.name}':\n\n{summary}\n\nRevisá dónde quedó 'BossRespawn' y guardá la escena (Ctrl+S).", "OK");
    }

    // Campos del BossController que no son números de diseño sino medidas de la escena (los pone
    // "Armar pelea del jefe" o se ajustan a mano según el modelo): al poner los valores del código
    // se conservan
    private static readonly string[] MeasuredFields = { "phaseTwoCenterHeight", "phaseOneYawOffset", "phaseTwoYawOffset" };

    // Si el jefe de una escena abierta tiene valores de una versión anterior a
    // BossController.TuningVersion, le pone los del código. Así un cambio de valores en el código
    // llega a la escena sin pasos extra (hay que guardarla). Se revisa:
    //   - después de cada compilación y al abrir una escena;
    //   - justo antes de entrar a Play y al volver de Play;
    //   - en Play, si la compilación fue al apretar Play: ahí se cambian solo para esa partida (no
    //     se puede tocar la escena) y al volver de Play se ponen en la escena.
    static BossFightSetup()
    {
        EditorApplication.delayCall += () => UpdateOutdatedTuning(false);
        EditorSceneManager.sceneOpened += (scene, mode) => UpdateOutdatedTuning(false);
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) UpdateOutdatedTuning(true);
            else if (state == PlayModeStateChange.EnteredEditMode) UpdateOutdatedTuning(false);
        };
    }

    private static void UpdateOutdatedTuning(bool exitingEditMode)
    {
        bool playing = EditorApplication.isPlaying;
        // Saliendo de Play: esos objetos están por destruirse, lo hace EnteredEditMode
        if (playing && !EditorApplication.isPlayingOrWillChangePlaymode) return;
        // Entrando a Play (fuera del aviso de ExitingEditMode) la escena todavía se está armando
        if (!playing && !exitingEditMode && EditorApplication.isPlayingOrWillChangePlaymode) return;

        foreach (BossController controller in Object.FindObjectsByType<BossController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            int version = new SerializedObject(controller).FindProperty("tuningVersion").intValue;
            if (version >= BossController.TuningVersion) continue;

            var report = new List<string>();
            ApplyCodeDefaults(controller, report, persist: !playing);
            if (playing) controller.OnTuningApplied();
            Debug.Log($"[BossFightSetup] El jefe de '{controller.gameObject.scene.name}' tenía los valores de la versión {version}: " +
                      $"ahora tiene los del código (versión {BossController.TuningVersion})." +
                      (playing ? " Solo para esta partida: al salir de Play se ponen en la escena." : " Guardá la escena (Ctrl+S).") + "\n" +
                      string.Join("\n", report.Select(line => "- " + line)), controller);
        }
    }

    // Lo mismo a mano, aunque la versión ya esté al día (por ejemplo, para deshacer cambios hechos
    // en el Inspector)
    [MenuItem("Tools/Jefe/Restablecer valores del jefe")]
    public static void ResetTuning()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Jefe", "Salí del modo Play antes de restablecer los valores.", "OK");
            return;
        }

        BossController controller = Object.FindFirstObjectByType<BossController>(FindObjectsInactive.Include);
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Jefe", "No hay un BossController en la escena abierta. Primero: Tools > Jefe > Armar pelea del jefe.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Jefe",
                "Se van a poner en los valores del código la vida, el daño, los tiempos y las pausas del jefe y de sus ataques. " +
                "Lo que hayas cambiado a mano en el Inspector se pierde.", "Restablecer", "Cancelar"))
            return;

        var report = new List<string>();
        ApplyCodeDefaults(controller, report);
        Selection.activeGameObject = controller.gameObject;
        string summary = string.Join("\n", report.Select(line => "- " + line));
        Debug.Log($"[BossFightSetup] Valores restablecidos:\n{summary}");
        EditorUtility.DisplayDialog("Jefe", $"{summary}\n\nGuardá la escena (Ctrl+S).", "OK");
    }

    // Pone en el jefe y sus ataques los números del código (vida, daño, tiempos, pausas...), sin
    // perder las referencias (modelos, materiales, cuerpo) ni las medidas de MeasuredFields. La
    // fuente del nombre pasa a la de FontPath. Con persist en false (en Play) no marca la escena.
    private static void ApplyCodeDefaults(BossController controller, List<string> report, bool persist = true)
    {
        Component[] targets = controller.GetComponents<BossAttack>().Cast<Component>().Prepend(controller).ToArray();
        if (persist) Undo.RecordObjects(targets, "Valores del jefe");
        foreach (Component target in targets)
            ResetToDefaults(target, target == controller ? MeasuredFields : new string[0]);
        report.Add($"Valores del código en el BossController y {targets.Length - 1} ataques.");

        var so = new SerializedObject(controller);
        so.FindProperty("nameFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontPath);
        so.FindProperty("tuningVersion").intValue = BossController.TuningVersion;
        so.ApplyModifiedPropertiesWithoutUndo();
        report.Add($"Fuente del nombre y del cartel: {Path.GetFileNameWithoutExtension(FontPath)}.");
        FitAggroToRespawn(controller, report);
        if (persist) EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
    }

    // Copia a "target" los valores por defecto de su script (los de una instancia nueva) y le
    // devuelve las referencias a objetos y los campos de "keep" que tenía antes
    private static void ResetToDefaults(Component target, string[] keep)
    {
        var references = new Dictionary<string, Object>();
        var kept = new Dictionary<string, float>();
        var before = new SerializedObject(target);
        SerializedProperty property = before.GetIterator();
        while (property.NextVisible(true))
            if (property.propertyType == SerializedPropertyType.ObjectReference && property.propertyPath != "m_Script")
                references[property.propertyPath] = property.objectReferenceValue;
        foreach (string field in keep)
        {
            SerializedProperty value = before.FindProperty(field);
            if (value != null && value.propertyType == SerializedPropertyType.Float) kept[field] = value.floatValue;
        }

        var temp = new GameObject("Valores por defecto") { hideFlags = HideFlags.HideAndDontSave };
        temp.SetActive(false); // apagado: en Play el componente nuevo no corre su Awake
        try
        {
            EditorUtility.CopySerialized(temp.AddComponent(target.GetType()), target);
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }

        var after = new SerializedObject(target);
        foreach (KeyValuePair<string, Object> reference in references)
        {
            SerializedProperty restored = after.FindProperty(reference.Key);
            if (restored != null) restored.objectReferenceValue = reference.Value;
        }
        foreach (KeyValuePair<string, float> value in kept)
            after.FindProperty(value.Key).floatValue = value.Value;
        after.ApplyModifiedPropertiesWithoutUndo();
    }

    // Que al reaparecer no empiece la pelea sola: el radio de inicio tiene que quedar más corto
    // que la distancia del jefe al punto de reaparición
    private static void FitAggroToRespawn(BossController controller, List<string> report)
    {
        Transform respawn = controller.transform.Find("BossRespawn");
        if (respawn == null) return;
        Vector3 offset = respawn.position - controller.transform.position;
        offset.y = 0f;

        var so = new SerializedObject(controller);
        SerializedProperty aggro = so.FindProperty("aggroRadius");
        float maxAggro = Mathf.Max(6f, offset.magnitude - 2f);
        if (aggro.floatValue <= maxAggro) return;
        aggro.floatValue = maxAggro;
        so.ApplyModifiedPropertiesWithoutUndo();
        report.Add($"El radio en el que empieza la pelea bajó a {maxAggro:0.0} m para que no te agarre al reaparecer.");
    }

    // La animación del FBX del esqueleto en loop, en su propio Animator Controller
    private static void SetupSkeletonAnimation(List<string> report)
    {
        if (AssetImporter.GetAtPath(SkeletonModelPath) is ModelImporter importer)
        {
            ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            bool changed = clips.Length > 0 && importer.clipAnimations.Length == 0;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                if (clip.loopTime) continue;
                clip.loopTime = true;
                changed = true;
            }
            if (changed)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                report.Add("La animación del esqueleto quedó en loop.");
            }
        }

        AnimationClip clipAsset = AssetDatabase.LoadAllAssetsAtPath(SkeletonModelPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (clipAsset == null)
        {
            report.Add("OJO: el FBX del esqueleto no trae animación; queda quieto.");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(SkeletonControllerPath) == null)
        {
            AnimatorController.CreateAnimatorControllerAtPathWithClip(SkeletonControllerPath, clipAsset);
            report.Add($"Creé {SkeletonControllerPath} con la animación '{clipAsset.name}'.");
        }
    }

    private static void PlaceRespawnPoint(GameObject boss, BossController controller, List<string> report)
    {
        Transform respawn = boss.transform.Find("BossRespawn");
        if (respawn == null)
        {
            var go = new GameObject("BossRespawn");
            Undo.RegisterCreatedObjectUndo(go, "Armar pelea del jefe");
            go.transform.SetParent(boss.transform, false);
            respawn = go.transform;
        }
        GetOrAdd<BossRespawnPoint>(respawn.gameObject);

        // Hacia donde arranca el jugador: por ahí se llega a la sala
        Vector3 center = boss.transform.position + Vector3.up * 1.5f;
        GameObject player = GameObject.FindWithTag("Player");
        Vector3 direction = player != null ? player.transform.position - boss.transform.position : Vector3.right;
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.right;

        float wall = 60f;
        foreach (RaycastHit hit in Physics.RaycastAll(center, direction, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(boss.transform)) continue;
            if (player != null && hit.collider.transform.root == player.transform.root) continue;
            wall = Mathf.Min(wall, hit.distance);
        }

        float distance = Mathf.Min(RespawnDistance, wall - 1.5f);
        Vector3 point = boss.transform.position + direction * Mathf.Max(2f, distance);
        point = GroundBelow(point + Vector3.up * 2f, boss.transform) ?? point;
        respawn.position = point;
        respawn.rotation = Quaternion.LookRotation(-direction); // mirando al jefe

        FitAggroToRespawn(controller, report);
        report.Add($"Punto de reaparición a {distance:0.0} m del jefe, hacia el inicio del jugador (pared a {wall:0.0} m).");
        if (distance < DefaultAggroRadius * 0.75f)
            report.Add("OJO: la sala parece chica en esa dirección; mové 'BossRespawn' a la entrada a mano si quedó mal.");
    }

    private static CapsuleCollider SetupCapsule(Transform parent, string name, float radius, float height)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Armar pelea del jefe");
            go.transform.SetParent(parent, false);
            child = go.transform;
        }
        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        CapsuleCollider capsule = GetOrAdd<CapsuleCollider>(child.gameObject);
        capsule.isTrigger = false; // la espada del jugador ignora los triggers
        capsule.direction = 1;
        capsule.radius = radius;
        capsule.height = Mathf.Max(height, radius * 2f);
        capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
        return capsule;
    }

    private static Material GetOrCreateMaterial(string name, Color color, bool transparent)
    {
        string path = $"{MaterialDir}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = BossFx.CreateUnlitMaterial(name, color, transparent);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // Hijo directo que es una instancia de ese prefab o modelo
    private static Transform FindInstanceChild(Transform parent, GameObject source)
    {
        if (source == null) return null;
        foreach (Transform child in parent)
            if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == source) return child;
        return null;
    }

    // Piso debajo de un punto, sin contar al jefe
    private static Vector3? GroundBelow(Vector3 from, Transform ignore)
    {
        RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3? point = null;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(ignore)) continue;
            if (hit.distance < best)
            {
                best = hit.distance;
                point = hit.point;
            }
        }
        return point;
    }

    // Caja del cuerpo del esqueleto (sin las manos ni el fondo); si no lo encuentra, todo el modelo
    private static Bounds BodyBounds(GameObject skeleton)
    {
        Renderer body = skeleton.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "skeleton:skeleton");
        return body != null ? body.bounds : RendererBounds(skeleton);
    }

    private static Bounds RendererBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        return bounds;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }

    private static string Format(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";
}
