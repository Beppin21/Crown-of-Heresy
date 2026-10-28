using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEngine;

// Tools > Player > Cámara Cinemachine
//
// Cambia la cámara de PlayerKnight.prefab por Cinemachine, SIN rehacer el resto del prefab
// (se conservan los ajustes de la espada, la cintura, etc.):
//   MainCamera   → se le saca ThirdPersonCamera y se le agrega CinemachineBrain (es la cámara real)
//   CM_Camara    → CinemachineCamera que sigue al CameraTarget del jugador:
//                    - Orbital Follow: orbita alrededor del personaje
//                    - Rotation Composer: lo encuadra (sobre el hombro)
//                    - Basic Multi Channel Perlin: temblor de cámara en mano
//                    - Deoccluder: no se mete adentro de las paredes
//                    - PlayerCameraRig: mouse/stick, lock-on y estados (explorar, correr, combate...)
// Se puede volver a correr: rehace CM_Camara.
public static class CinemachineSetup
{
    private const string PlayerPrefabPath = "Assets/Prefabs/PlayerKnight.prefab";
    private const string RigName = "CM_Camara";
    private const string HandheldNoisePath = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";

    [MenuItem("Tools/Player/Cámara Cinemachine")]
    private static void Run()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            if (Setup(root))
            {
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log("[CinemachineSetup] Listo: PlayerKnight usa Cinemachine. Los estados de la cámara " +
                          "(explorar, correr, combate, lock-on, apuntar) se ajustan en CM_Camara > Player Camera Rig.");
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
        Transform cameraTarget = FindDeep(root.transform, "CameraTarget");
        UnityEngine.Camera mainCamera = null;
        foreach (UnityEngine.Camera c in root.GetComponentsInChildren<UnityEngine.Camera>(true))
        {
            if (c.CompareTag("MainCamera")) { mainCamera = c; break; }
        }
        PlayerController player = root.GetComponentInChildren<PlayerController>(true);

        if (cameraTarget == null || mainCamera == null || player == null)
        {
            Debug.LogError("[CinemachineSetup] El prefab tiene que tener CameraTarget, una cámara con tag MainCamera y PlayerController.");
            return false;
        }

        // --- Cámara real: la maneja Cinemachine ---
        ThirdPersonCamera oldOrbit = mainCamera.GetComponent<ThirdPersonCamera>();
        if (oldOrbit != null) Object.DestroyImmediate(oldOrbit);

        CinemachineBrain brain = mainCamera.GetComponent<CinemachineBrain>();
        if (brain == null) brain = mainCamera.gameObject.AddComponent<CinemachineBrain>();
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.6f);
        brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate; // el jugador se mueve con Rigidbody interpolado

        // --- Cámara virtual ---
        Transform old = root.transform.Find(RigName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        GameObject rig = new GameObject(RigName);
        rig.transform.SetParent(root.transform, false);
        rig.transform.position = mainCamera.transform.position;

        CinemachineCamera vcam = rig.AddComponent<CinemachineCamera>();
        vcam.Follow = cameraTarget;
        vcam.LookAt = cameraTarget;
        LensSettings lens = vcam.Lens;
        lens.FieldOfView = 50f;
        lens.NearClipPlane = 0.1f;
        lens.FarClipPlane = mainCamera.farClipPlane;
        vcam.Lens = lens;

        // Orbita alrededor del personaje; los ejes los mueve PlayerCameraRig (mouse/stick)
        CinemachineOrbitalFollow orbit = rig.AddComponent<CinemachineOrbitalFollow>();
        orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
        orbit.Radius = 2.2f;
        TrackerSettings tracker = orbit.TrackerSettings;
        tracker.BindingMode = BindingMode.WorldSpace;     // el giro es del mouse, no del cuerpo
        tracker.PositionDamping = new Vector3(0.25f, 0.2f, 0.25f); // sigue con un poquito de retraso
        orbit.TrackerSettings = tracker;
        orbit.HorizontalAxis.Range = new Vector2(-180f, 180f);
        orbit.HorizontalAxis.Wrap = true;
        orbit.VerticalAxis.Range = new Vector2(-35f, 55f);
        orbit.VerticalAxis.Wrap = false;
        orbit.VerticalAxis.Value = 8f;

        // Encuadre: el personaje a la izquierda (cámara sobre el hombro derecho)
        CinemachineRotationComposer composer = rig.AddComponent<CinemachineRotationComposer>();
        ScreenComposerSettings composition = composer.Composition;
        composition.ScreenPosition = new Vector2(-0.18f, 0.04f);
        composer.Composition = composition;
        composer.Damping = new Vector2(0.4f, 0.3f); // el encuadre acompaña con algo de peso

        // Temblor de cámara en mano
        CinemachineBasicMultiChannelPerlin noise = rig.AddComponent<CinemachineBasicMultiChannelPerlin>();
        noise.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>(HandheldNoisePath);
        noise.AmplitudeGain = 1f;
        noise.FrequencyGain = 0.8f;
        if (noise.NoiseProfile == null)
            Debug.LogWarning($"[CinemachineSetup] No se encontró el perfil de temblor '{HandheldNoisePath}': asigná uno en Noise Profile.");

        // Paredes: se acerca al personaje en vez de atravesarlas
        CinemachineDeoccluder deoccluder = rig.AddComponent<CinemachineDeoccluder>();
        deoccluder.CollideAgainst = ~(1 << player.gameObject.layer);
        deoccluder.IgnoreTag = "Player";
        deoccluder.MinimumDistanceFromTarget = 0.3f;
        var avoid = deoccluder.AvoidObstacles;
        avoid.Enabled = true;
        avoid.CameraRadius = 0.25f;
        avoid.DistanceLimit = 0f;
        avoid.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward;
        avoid.SmoothingTime = 0.2f;
        avoid.Damping = 0.4f;           // vuelve a su lugar despacio
        avoid.DampingWhenOccluded = 0.05f; // se acerca rápido al aparecer una pared
        deoccluder.AvoidObstacles = avoid;

        rig.AddComponent<PlayerCameraRig>();
        return true;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }
        return null;
    }
}
