using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

// Tools > Player > Lámpara
//
// Le agrega la lámpara de aceite a PlayerKnight.prefab, SIN rehacer el resto del prefab
// (en la mano que diga Player Lantern > Use Right Hand; por defecto la izquierda):
//   mano (derecha o izquierda)
//   └── LampGrip        ← mover/rotar ESTE para acomodar dónde la agarra la mano
//       └── LampPivot   (lo rota PlayerLantern: la lámpara cuelga y se balancea)
//           ├── Lamp    (el modelo, con la manija en el pivote)
//           └── LuzLampara
// Además: PlayerLantern en el modelo y el "IK Pass" del Animator prendido (para mover el brazo).
// La lámpara solo aparece en escenas con ZoneSettings en modo lámpara (Town).
// Se puede volver a correr: rehace la lámpara.
public static class LanternSetup
{
    private const string PlayerPrefabPath = "Assets/Prefabs/PlayerKnight.prefab";
    private const string LampModelPath = "Assets/Models/Accesories/Lamp/Lamp.fbx";
    private const float LampHeight = 0.32f; // alto realista de una lámpara de mano (metros)

    [MenuItem("Tools/Player/Lámpara")]
    private static void Run()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            if (Setup(root))
            {
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log("[LanternSetup] Listo: el Knight lleva la lámpara en la mano izquierda (solo en zonas en modo lámpara). " +
                          "Acomodala moviendo LampGrip; la posición del brazo se ajusta en KnightModel > Player Lantern.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // También la usa PlayerKnightSetup al armar el prefab de cero
    public static bool Setup(GameObject root)
    {
        GameObject lampModel = AssetDatabase.LoadAssetAtPath<GameObject>(LampModelPath);
        PlayerController player = root.GetComponentInChildren<PlayerController>(true);
        Animator animator = player != null ? player.GetComponentInChildren<Animator>(true) : null;
        if (lampModel == null || animator == null)
        {
            Debug.LogError($"[LanternSetup] Hace falta el modelo '{LampModelPath}' y el Knight con su Animator en el prefab.");
            return false;
        }

        // La mano la decide el componente (useRightHand, por defecto la izquierda)
        PlayerLantern existing = animator.GetComponent<PlayerLantern>();
        bool rightHand = existing != null && existing.UsesRightHand;
        Transform holdingHand = FindHumanBone(animator, rightHand ? "RightHand" : "LeftHand");
        if (holdingHand == null)
        {
            Debug.LogError("[LanternSetup] No se encontró el hueso de la mano (el Avatar tiene que ser Humanoid).");
            return false;
        }

        // Si ya había una lámpara (corrida anterior), se rehace
        Transform oldGrip = FindDeep(animator.transform, "LampGrip");
        if (oldGrip != null) Object.DestroyImmediate(oldGrip.gameObject);

        Transform playerRoot = player.transform;
        Quaternion facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(playerRoot.forward, Vector3.up), Vector3.up);

        GameObject grip = new GameObject("LampGrip");
        grip.transform.SetParent(holdingHand, false);

        GameObject pivot = new GameObject("LampPivot");
        pivot.transform.SetParent(grip.transform, false);
        pivot.transform.rotation = facing; // derecho, mirando hacia adelante del personaje

        // --- Modelo: alto realista, con la manija (la parte de arriba) en el pivote ---
        GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(lampModel, pivot.transform);
        lamp.name = "Lamp";
        lamp.transform.localPosition = Vector3.zero;
        lamp.transform.localRotation = Quaternion.identity;

        Bounds bounds = GetBounds(lamp);
        if (bounds.size.y > 0.001f)
        {
            float scaleFactor = LampHeight / bounds.size.y;
            lamp.transform.localScale = new Vector3(scaleFactor * lamp.transform.localScale.x,
                                                    scaleFactor * lamp.transform.localScale.y,
                                                    scaleFactor * lamp.transform.localScale.z);
            bounds = GetBounds(lamp);
        }
        Vector3 handle = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        lamp.transform.position += pivot.transform.position - handle;
        bounds = GetBounds(lamp);

        // La lámpara no proyecta sombra (si no, taparía su propia luz) y no choca con nada
        foreach (Renderer r in lamp.GetComponentsInChildren<Renderer>(true))
            r.shadowCastingMode = ShadowCastingMode.Off;
        foreach (Collider c in lamp.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(c);
        int layer = player.gameObject.layer;
        foreach (Transform t in grip.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;

        // --- Luz cálida de llama, en el centro del vidrio ---
        GameObject lightObject = new GameObject("LuzLampara");
        lightObject.transform.SetParent(pivot.transform, false);
        lightObject.transform.position = bounds.center;
        lightObject.layer = layer;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.72f, 0.42f);
        light.range = 9f;
        light.intensity = 2.2f;
        light.shadows = LightShadows.Soft;
        light.shadowNearPlane = 0.1f;

        // --- Script en el modelo (usa OnAnimatorIK) ---
        PlayerLantern lantern = animator.GetComponent<PlayerLantern>();
        if (lantern == null) lantern = animator.gameObject.AddComponent<PlayerLantern>();
        SerializedObject so = new SerializedObject(lantern);
        so.FindProperty("lampGrip").objectReferenceValue = grip.transform;
        so.FindProperty("lampPivot").objectReferenceValue = pivot.transform;
        so.FindProperty("lampLight").objectReferenceValue = light;
        so.FindProperty("useRightHand").boolValue = rightHand;
        // La versión anterior frenaba demasiado al alzar la lámpara (0,55): se lleva al nuevo valor
        SerializedProperty raisedSpeed = so.FindProperty("raisedMoveSpeed");
        if (raisedSpeed.floatValue < 0.7f) raisedSpeed.floatValue = 0.85f;
        so.ApplyModifiedPropertiesWithoutUndo();

        EnableIKPass(animator);
        return true;
    }

    // OnAnimatorIK solo se llama si la capa del Animator tiene "IK Pass" prendido
    private static void EnableIKPass(Animator animator)
    {
        if (!(animator.runtimeAnimatorController is AnimatorController controller))
        {
            Debug.LogWarning("[LanternSetup] No se pudo prender el IK Pass: prendelo a mano en la capa Base Layer del Animator.");
            return;
        }

        AnimatorControllerLayer[] layers = controller.layers;
        if (layers.Length == 0 || layers[0].iKPass) return;
        layers[0].iKPass = true;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    private static Transform FindHumanBone(Animator animator, string humanBoneName)
    {
        Avatar avatar = animator.avatar;
        if (avatar == null || !avatar.isHuman) return null;
        foreach (HumanBone bone in avatar.humanDescription.human)
        {
            if (bone.humanName == humanBoneName)
                return FindDeep(animator.transform, bone.boneName);
        }
        return null;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }
        return null;
    }

    private static Bounds GetBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        return b;
    }
}
