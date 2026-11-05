using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Pone el "Masked Character" (Regulator, Sketchfab, licencia Free Standard: libre, sin crédito
// obligatorio) en el enemigo del Dungeon, Assets/Prefabs/Enemy 1.prefab, en lugar del cubo con ojos.
// El modelo vino sin esqueleto: se le puso el de Mixamo (Assets/Models/masked-character/MaskedCharacter.fbx).
//
// Se arma solo una vez (etiqueta BuildVersionLabel en el prefab: subirla si cambia este script) y
// también desde Tools > Enemigos > Armar enemigo enmascarado:
//  1. El FBX como humanoide (Avatar propio, sin materiales importados), así le sirven las
//     animaciones humanoides que ya tiene el proyecto.
//  2. Material URP con sus texturas: color, normal y una máscara armada con metal (R), oclusión (G)
//     y suavidad = 1 - rugosidad (A), que es lo que lee URP Lit.
//  3. Animator Controller Animations/Enemies/MaskedEnemy.controller con animaciones humanoides que ya
//     tenía el proyecto: UAL2 (Quaternius), DoubleL (versiones InPlace) y la muerte de Mixamo.
//     EnemyDasher pasa a cada estado con CrossFade y los que no se repiten vuelven solos a "Quieto":
//       Quieto = Zombie_Idle_Loop, Girar = 1Hand_Up_Walk_A_F_InPlace (camina en el lugar mientras
//       gira hacia el jugador), Carga = 1Hand_Up_Crouch_Idle_1 (agachado mientras tiembla),
//       Embestida = 1Hand_Up_Sprint_A_F_InPlace (x1.4, corre durante el dash),
//       Zarpazo = Zombie_Scratch (la onda expansiva), Golpe = Hit_Knockback,
//       Muerte = Sword And Shield Death.
//  4. En el prefab, el cubo (el visualTransform de EnemyDasher, que tiembla al cargar) pasa a ser
//     "Modelo": sin malla, sin collider y sin los ojos, con el personaje adentro, de TargetHeight de
//     alto y los pies en el piso del collider. El personaje y su Animator miran a +Z (hacia donde
//     embiste). Cada armado prueba todos los estados y, si una animación lo deja de espaldas, el
//     estado usa una copia girada 180° (ver FixFacing). El collider del cuerpo toma la medida del
//     personaje y la caja de la embestida queda delante del pecho, invisible.
[InitializeOnLoad]
public static class EnemyModelSetup
{
    private const string Folder = "Assets/Models/masked-character";
    private const string ModelPath = Folder + "/MaskedCharacter.fbx";
    private const string AlbedoPath = Folder + "/textures/Material_008_albedo.jpg";
    private const string NormalPath = Folder + "/textures/Material_008_normal.png";
    private const string MetallicPath = Folder + "/textures/Material_008_metallic.jpg";
    private const string RoughnessPath = Folder + "/textures/Material_008_roughness.jpg";
    private const string OcclusionPath = Folder + "/textures/Material_008_AO.jpg";
    private const string MaskPath = Folder + "/textures/Material_008_mask.png";
    private const string MaterialPath = Folder + "/Masked Character.mat";
    private const string ControllerFolder = "Assets/Animations/Enemies";
    private const string ControllerPath = ControllerFolder + "/MaskedEnemy.controller";
    private const string ClipLibraryPath = "Assets/Animations/Player/UAL2_Standard.fbx";
    private const string DeathClipPath = "Assets/Animations/Player/Sword And Shield Death.fbx";
    // De DoubleL (humanoides, versiones "InPlace": no mueven al personaje, lo mueve EnemyDasher)
    private const string DoubleLFolder = "Assets/DoubleL/FBX Unity/One Hand Up/Movement";
    private const string TurnClipPath = DoubleLFolder + "/Walk/Base/InPlace/1Hand_Up_Walk_A_F_InPlace.fbx";
    private const string WindUpClipPath = DoubleLFolder + "/Crouch/Idle/Idle/1Hand_Up_Crouch_Idle_1.fbx";
    private const string SprintClipPath = DoubleLFolder + "/Sprint/Base/InPlace/1Hand_Up_Sprint_A_F_InPlace.fbx";
    private const float SprintSpeed = 1.4f; // la embestida es cortita (0.35 s): las piernas, rápidas
    private const string PrefabPath = "Assets/Prefabs/Enemy 1.prefab";

    // Build2: el Avatar se había armado con el modelo a 1.7 cm y después se agrandó la escala: el
    // esqueleto guardado no coincidía ("Avatar Rig Configuration mis-match") y en Play la malla
    // colapsaba (los enemigos se volvían invisibles). Ahora se rehace si no coincide.
    // Build3: gira caminando en el lugar, se agacha al cargar y corre en la embestida (DoubleL)
    // Build4: cambió izquierda y derecha del Avatar creyendo que el rig de Mixamo había quedado de
    // espaldas. No era así: el OBJ que se subió mira a +Z (la máscara y las puntas de los pies) y
    // mixamorig:LeftUpLeg está del lado izquierdo de verdad (-X). El cambio lo dejaba de espaldas con
    // las animaciones de UAL2 y doblaba brazos y piernas para el lado equivocado.
    // Build5: el Avatar vuelve a su mapeo y cada estado se prueba (FixFacing)
    // Build6: el temblor que tenía el cubo, con el personaje era demasiado: 80 % menos (ver ShakeScale)
    // Build7: las copias giradas se guardan con el giro desde que se crean (ver TurnedCopy)
    private const string BuildVersionLabel = "EnemyMaskedBuild7";

    // El temblor de la carga (embestida y onda expansiva) queda en un 20 % del que tenía el cubo:
    // con el personaje es solo un aviso. Se cambia una sola vez: si el prefab ya no tiene los valores
    // del cubo, alguien los ajustó a mano y se respetan.
    private const float ShakeScale = 0.2f;
    private const float CubeShakeIntensity = 0.15f;
    private const float CubeBlastShakeIntensity = 0.1f;

    // Momento (normalizado) de cada animación en el que se mide para dónde mira el cuerpo
    private const float FacingSampleTime = 0.1f;
    // Copias giradas 180° de las animaciones que lo dejan de espaldas (se rehacen en cada armado)
    private const string TurnedFolder = ControllerFolder + "/Giradas";

    private const float TargetHeight = 1.9f;  // alto del personaje
    private const float FloorY = -0.5f;       // donde apoyaba el cubo de 1 m (el centro del enemigo queda igual)
    private const float BodyWidth = 0.8f;     // ancho y fondo del collider del cuerpo

    static EnemyModelSetup()
    {
        EditorApplication.delayCall += AutoRun;
        // Si la compilación cayó al apretar Play, en Play no se arma nada: se arma al volver
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += AutoRun;
        };
    }

    private class ImportWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(p => p.StartsWith(Folder + "/") && !p.EndsWith(".mat") && !p.Contains("_mask")))
                EditorApplication.delayCall += AutoRun;
        }
    }

    private static void AutoRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) return;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null || System.Array.IndexOf(AssetDatabase.GetLabels(prefab), BuildVersionLabel) >= 0) return;
        Build();
    }

    [MenuItem("Tools/Enemigos/Armar enemigo enmascarado")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Enemigo", "Salí del modo Play antes de armar el enemigo.", "OK");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Debug.LogWarning($"[EnemyModelSetup] Falta el modelo ({ModelPath}) o el prefab ({PrefabPath}).");
            return;
        }

        // 1. Humanoide
        if (!EnsureHumanoid(out Avatar avatar)) return;
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        // 2 y 3. Material y animaciones
        Material material = GetOrCreateMaterial();
        AnimatorController controller = BuildController();
        if (controller == null) return;
        FixFacing(modelAsset, avatar, controller);

        // 4. Prefab
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            EnemyDasher dasher = root.GetComponent<EnemyDasher>();
            if (dasher == null)
            {
                Debug.LogWarning("[EnemyModelSetup] El prefab del enemigo no tiene EnemyDasher.");
                return;
            }
            var so = new SerializedObject(dasher);
            Transform visual = so.FindProperty("visualTransform").objectReferenceValue as Transform;
            GameObject hitbox = so.FindProperty("hitboxObject").objectReferenceValue as GameObject;
            if (visual == null) visual = root.transform.Find("Cube") ?? root.transform.Find("Modelo");
            if (visual == null)
            {
                Debug.LogWarning("[EnemyModelSetup] No encontré la parte visual del enemigo (visualTransform).");
                return;
            }

            // El cubo y sus ojos afuera; queda un contenedor vacío que es el que tiembla al cargar
            foreach (Transform child in visual.Cast<Transform>().ToArray())
                Object.DestroyImmediate(child.gameObject);
            foreach (Component part in visual.GetComponents<Component>())
                if (part is MeshRenderer || part is MeshFilter || part is Collider)
                    Object.DestroyImmediate(part);
            visual.name = "Modelo";
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;

            GameObject character = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, visual);
            character.name = "Masked Character";
            foreach (Renderer renderer in character.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials.Length > 0 ? renderer.sharedMaterials : new Material[1];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                // La caja se calcula con la pose de cada frame: nunca se oculta por tener una caja vieja
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }

            Animator animator = character.GetComponent<Animator>();
            if (animator == null) animator = character.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // lo mueve el Rigidbody de EnemyDasher
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            FitCharacter(character.transform, visual);

            // Collider del cuerpo a la medida del personaje (abajo sigue en el piso de antes)
            BoxCollider body = root.GetComponent<BoxCollider>();
            if (body != null)
            {
                body.size = new Vector3(BodyWidth, TargetHeight, BodyWidth);
                body.center = new Vector3(0f, FloorY + TargetHeight * 0.5f, 0f);
            }

            // La caja de la embestida: delante del pecho, alta como el torso, sin el cubo rojo que se veía
            if (hitbox != null)
            {
                hitbox.transform.localPosition = new Vector3(0f, FloorY + TargetHeight * 0.55f, 0.6f);
                hitbox.transform.localRotation = Quaternion.identity;
                hitbox.transform.localScale = new Vector3(1.1f, TargetHeight * 0.7f, 0.6f);
                MeshRenderer hitboxRenderer = hitbox.GetComponent<MeshRenderer>();
                if (hitboxRenderer != null) hitboxRenderer.enabled = false;
            }

            so.FindProperty("animator").objectReferenceValue = animator;
            string shake = LowerCubeShake(so.FindProperty("shakeIntensity"), CubeShakeIntensity) +
                           LowerCubeShake(so.FindProperty("secondAttackShakeIntensity"), CubeBlastShakeIntensity);
            so.ApplyModifiedPropertiesWithoutUndo();
            if (shake.Length > 0) Debug.Log("[EnemyModelSetup] Temblor de la carga 80 % más suave:" + shake);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (saved != null) AssetDatabase.SetLabels(saved, new[] { BuildVersionLabel });
            Debug.Log($"[EnemyModelSetup] Enemigo enmascarado listo en {PrefabPath}: {TargetHeight} m, con " +
                      $"{controller.layers[0].stateMachine.states.Length} animaciones (MaskedEnemy.controller). " +
                      "Los enemigos de las escenas se actualizan solos.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // Si el valor sigue siendo el del cubo, lo baja a ShakeScale; devuelve qué cambió (vacío si nada)
    private static string LowerCubeShake(SerializedProperty property, float cubeValue)
    {
        if (property == null || !Mathf.Approximately(property.floatValue, cubeValue)) return "";
        property.floatValue = cubeValue * ShakeScale;
        return $" {property.name} {cubeValue:0.###} → {property.floatValue:0.###}";
    }

    // FBX como humanoide con su propio Avatar; si vino en otra escala (Mixamo con un OBJ en metros), se corrige
    private static bool EnsureHumanoid(out Avatar avatar)
    {
        avatar = null;
        if (!(AssetImporter.GetAtPath(ModelPath) is ModelImporter importer)) return false;
        if (importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
            importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importAnimation)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.SaveAndReimport();
        }

        float height = MeasureModelHeight();
        if (height > 0.01f && (height < 0.5f || height > 5f))
        {
            importer.globalScale *= TargetHeight / height;
            importer.SaveAndReimport();
            Debug.Log($"[EnemyModelSetup] El modelo medía {height:0.###} m: cambié la escala de importación a {importer.globalScale:0.###}.");
        }

        // El esqueleto guardado en la configuración del Avatar tiene que ser el del modelo tal como se
        // importa ahora: si no (por ejemplo, porque cambió la escala), se borra y Unity lo vuelve a
        // generar con el mapeo automático
        if (!SkeletonMatchesModel(importer))
        {
            HumanDescription description = importer.humanDescription;
            description.human = new HumanBone[0];
            description.skeleton = new SkeletonBone[0];
            importer.humanDescription = description;
            importer.autoGenerateAvatarMappingIfUnspecified = true;
            importer.SaveAndReimport();
            Debug.Log("[EnemyModelSetup] La configuración del Avatar no coincidía con el modelo: la rehice desde cero." +
                      (SkeletonMatchesModel(importer) ? "" : " OJO: sigue sin coincidir, revisá el Rig del FBX."));
        }

        avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogWarning("[EnemyModelSetup] Unity no pudo armar un Avatar humanoide con el esqueleto del modelo. Revisá el FBX en el Inspector (Rig > Configure).");
            return false;
        }

        // Build4 había cambiado izquierda y derecha (ver BuildVersionLabel): se deshace
        if (SidesSwapped(importer.humanDescription))
        {
            HumanDescription description = importer.humanDescription;
            description.human = SwapLeftRight(description.human);
            importer.humanDescription = description;
            importer.SaveAndReimport();
            avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            bool ok = avatar != null && avatar.isValid && avatar.isHuman;
            Debug.Log("[EnemyModelSetup] Izquierda y derecha del Avatar otra vez en su lugar: el rig de Mixamo estaba bien." +
                      (ok ? "" : " OJO: el Avatar no quedó válido."));
            if (!ok) return false;
        }
        return true;
    }

    // ¿El mapeo guardado ya tiene la pierna izquierda de Unity en el hueso "Right" del rig?
    private static bool SidesSwapped(HumanDescription description)
    {
        if (description.human == null) return false;
        HumanBone leftLeg = description.human.FirstOrDefault(h => h.humanName == "LeftUpperLeg");
        return !string.IsNullOrEmpty(leftLeg.boneName) && leftLeg.boneName.Contains("Right");
    }

    // Cada hueso humano "Left..." pasa a usar el hueso del rig de su par "Right..." y al revés
    private static HumanBone[] SwapLeftRight(HumanBone[] human)
    {
        var bones = human.ToDictionary(h => h.humanName, h => h.boneName);
        var swapped = (HumanBone[])human.Clone();
        for (int i = 0; i < swapped.Length; i++)
        {
            string name = swapped[i].humanName;
            string pair = name.StartsWith("Left") ? "Right" + name.Substring(4)
                        : name.StartsWith("Right") ? "Left" + name.Substring(5)
                        : null;
            if (pair != null && bones.TryGetValue(pair, out string bone))
                swapped[i].boneName = bone;
        }
        return swapped;
    }

    // Compara la posición de las caderas guardada en la configuración del Avatar con la del modelo
    private static bool SkeletonMatchesModel(ModelImporter importer)
    {
        SkeletonBone[] skeleton = importer.humanDescription.skeleton;
        if (skeleton == null || skeleton.Length == 0) return true; // vacío: Unity lo genera en cada importación
        SkeletonBone stored = skeleton.FirstOrDefault(b => b.name != null && b.name.EndsWith("Hips"));
        if (string.IsNullOrEmpty(stored.name)) return false;

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) return true;
        var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject probe = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
            Transform hips = probe.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.EndsWith("Hips"));
            if (hips == null) return true;
            float saved = stored.position.magnitude;
            float actual = hips.localPosition.magnitude;
            return Mathf.Abs(saved - actual) <= 0.05f * Mathf.Max(saved, actual, 0.0001f);
        }
        finally
        {
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static float MeasureModelHeight()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) return 0f;
        // En una escena de prueba aparte, para no tocar la escena abierta
        var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject probe = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
            ToesAndHead(probe.transform, out float floor, out float top);
            return top - floor;
        }
        finally
        {
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    // Altura del piso (las puntas de los pies) y de la coronilla, con los huesos de Mixamo; si no
    // están, con las cajas de las mallas
    private static void ToesAndHead(Transform character, out float floor, out float top)
    {
        Transform[] bones = character.GetComponentsInChildren<Transform>(true);
        Transform head = bones.FirstOrDefault(b => b.name.EndsWith("HeadTop_End"));
        Transform[] toes = bones.Where(b => b.name.EndsWith("Toe_End") || b.name.EndsWith("ToeBase")).ToArray();
        if (head != null && toes.Length > 0)
        {
            floor = toes.Min(t => t.position.y);
            top = head.position.y;
            return;
        }
        Renderer[] renderers = character.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) { floor = 0f; top = 0f; return; }
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        floor = bounds.min.y;
        top = bounds.max.y;
    }

    // Frente hacia +Z del enemigo (el frente sale de las piernas: en Unity derecha = Cross(arriba,
    // frente), así que frente = Cross(derecha, arriba)), TargetHeight de alto y pies en FloorY
    private static void FitCharacter(Transform character, Transform visual)
    {
        // Sin giro: con un Avatar humanoide, en Play las animaciones ponen el frente del Avatar
        // mirando al +Z del Animator, sea cual sea la pose de origen
        character.localPosition = Vector3.zero;
        character.localRotation = Quaternion.identity;
        character.localScale = Vector3.one;

        ToesAndHead(character, out float floor, out float top);
        float height = top - floor;
        if (height > 0.01f)
            character.localScale = Vector3.one * (TargetHeight / height);

        ToesAndHead(character, out floor, out _);
        Vector3 feet = visual.InverseTransformPoint(new Vector3(character.position.x, floor, character.position.z));
        character.localPosition = new Vector3(0f, FloorY - feet.y, 0f);
    }

    private static AnimatorController BuildController()
    {
        AnimationClip idle = FindClip(ClipLibraryPath, "Zombie_Idle_Loop");
        AnimationClip blast = FindClip(ClipLibraryPath, "Zombie_Scratch");
        AnimationClip hit = FindClip(ClipLibraryPath, "Hit_Knockback");
        AnimationClip death = FirstClip(DeathClipPath);
        AnimationClip turn = FirstClip(TurnClipPath);
        AnimationClip windUp = FirstClip(WindUpClipPath);
        AnimationClip sprint = FirstClip(SprintClipPath);
        if (idle == null || blast == null || hit == null || death == null || turn == null || windUp == null || sprint == null)
        {
            Debug.LogWarning("[EnemyModelSetup] Faltan animaciones (UAL2_Standard.fbx, Sword And Shield Death.fbx o las de DoubleL).");
            return null;
        }

        if (!AssetDatabase.IsValidFolder(ControllerFolder))
            AssetDatabase.CreateFolder("Assets/Animations", "Enemies");
        AssetDatabase.DeleteAsset(ControllerPath); // se rehace entero
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState idleState = machine.AddState("Quieto");
        idleState.motion = idle;
        machine.defaultState = idleState;
        // Los que se repiten: EnemyDasher entra y sale de ellos
        machine.AddState("Girar").motion = turn;
        machine.AddState("Carga").motion = windUp;
        AnimatorState sprintState = machine.AddState("Embestida");
        sprintState.motion = sprint;
        sprintState.speed = SprintSpeed;
        AddOneShot(machine, "Zarpazo", blast, idleState);
        AddOneShot(machine, "Golpe", hit, idleState);
        AnimatorState deathState = machine.AddState("Muerte");
        deathState.motion = death; // queda en el piso hasta que el enemigo desaparece

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    // Estado que se reproduce una vez y vuelve solo a "Quieto"
    private static void AddOneShot(AnimatorStateMachine machine, string name, AnimationClip clip, AnimatorState back)
    {
        AnimatorState state = machine.AddState(name);
        state.motion = clip;
        AnimatorStateTransition toIdle = state.AddTransition(back);
        toIdle.hasExitTime = true;
        toIdle.exitTime = 0.9f;
        toIdle.hasFixedDuration = true;
        toIdle.duration = 0.2f;
    }

    // Prueba cada estado con el Avatar en una escena aparte y mide para dónde queda el cuerpo. Las
    // animaciones conservan la orientación de su archivo ("Based Upon: Original"), y no todos los
    // paquetes miran para el mismo lado. Si alguna lo deja de espaldas, el estado pasa a usar una
    // copia con el giro de la raíz (Root Transform Rotation > Offset) en 180°. Las originales no se
    // tocan: las de UAL2 también las usa el caballero.
    private static void FixFacing(GameObject modelAsset, Avatar avatar, AnimatorController controller)
    {
        AssetDatabase.DeleteAsset(TurnedFolder);
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, preview);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Animator animator = probe.GetComponent<Animator>();
            if (animator == null) animator = probe.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // sin cámara: si no, no mueve los huesos
            BindController(animator, controller);

            Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (hand == null)
            {
                Debug.LogWarning("[EnemyModelSetup] No pude probar para dónde miran las animaciones: el Avatar no tiene mano izquierda.");
                return;
            }
            Vector3 restHand = hand.position;
            bool posed = false;
            var report = new List<string>();
            var turned = new Dictionary<string, float>(); // copia → giro que tiene que quedar escrito
            foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
            {
                AnimatorState state = child.state;
                if (!(state.motion is AnimationClip clip)) continue;
                float facing = Facing(animator, state.name);
                posed |= Vector3.Distance(hand.position, restHand) > 0.02f;
                string note = "";
                if (facing < -0.5f)
                {
                    AnimationClip copy = TurnedCopy(clip, state.name, out string path, out float offset);
                    turned[path] = offset;
                    if (copy != null) state.motion = copy;
                    BindController(animator, controller);
                    facing = Facing(animator, state.name);
                    note = facing > 0.5f ? " (girada)" : " (girada, OJO: sigue mal)";
                }
                else if (facing < 0.5f) note = " (OJO: de costado)";
                report.Add($"{state.name} {facing:+0.00;-0.00}{note}");
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            if (!posed)
                Debug.LogWarning("[EnemyModelSetup] No pude probar para dónde miran las animaciones: el Animator no movió los huesos en el editor.");
            else
                Debug.Log("[EnemyModelSetup] Para dónde mira cada estado (+1 = hacia donde embiste, -1 = de espaldas): " +
                          string.Join(" | ", report));
            string[] unsaved = turned.Where(t => !SavedWithOffset(t.Key, t.Value)).Select(t => Path.GetFileName(t.Key)).ToArray();
            if (unsaved.Length > 0)
                Debug.LogWarning("[EnemyModelSetup] OJO: estas copias quedaron guardadas sin el giro: " + string.Join(", ", unsaved));
            else if (turned.Count > 0)
                Debug.Log($"[EnemyModelSetup] Las {turned.Count} copias giradas quedaron guardadas con el giro.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void BindController(Animator animator, AnimatorController controller)
    {
        animator.runtimeAnimatorController = null;
        animator.runtimeAnimatorController = controller;
        animator.Rebind();
    }

    // Frente del cuerpo (caderas y hombros: en Unity frente = Cross(derecha, arriba)) en el estado
    // dado, comparado con el +Z del Animator
    private static float Facing(Animator animator, string state)
    {
        animator.Play(state, 0, FacingSampleTime);
        animator.Update(0f);
        Transform leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (leftLeg == null || rightLeg == null || leftArm == null || rightArm == null) return 0f;
        Vector3 right = rightLeg.position - leftLeg.position + rightArm.position - leftArm.position;
        Vector3 front = Vector3.ProjectOnPlane(Vector3.Cross(right, Vector3.up), Vector3.up);
        return front.sqrMagnitude < 1e-8f ? 0f : Vector3.Dot(front.normalized, animator.transform.forward);
    }

    // Copia de la animación (también las del FBX) con la raíz girada 180°, solo para este enemigo.
    // El giro se pone antes de crear el archivo, así queda escrito desde el principio. En Build6 se
    // ponía después, y al crear la copia siguiente Unity recargaba la anterior del disco sin el giro:
    // Quieto y Zarpazo quedaron guardadas en 0 y, al reabrir Unity, volvió a ponerse de espaldas.
    // Se devuelve la copia tal como quedó en el disco, para medirla así.
    private static AnimationClip TurnedCopy(AnimationClip source, string stateName, out string path, out float offset)
    {
        if (!AssetDatabase.IsValidFolder(TurnedFolder))
            AssetDatabase.CreateFolder(ControllerFolder, Path.GetFileName(TurnedFolder));
        string name = $"{stateName} (girada)";
        path = $"{TurnedFolder}/{name}.anim";
        var copy = new AnimationClip();
        EditorUtility.CopySerialized(source, copy);
        copy.name = name;
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.orientationOffsetY = Mathf.Repeat(settings.orientationOffsetY + 180f, 360f);
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        offset = settings.orientationOffsetY;
        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    }

    // Lo que importa es lo que quedó escrito: con eso arranca Unity y con eso se entra en Play
    private static bool SavedWithOffset(string path, float offset)
    {
        return File.Exists(path) && File.ReadAllText(path).Contains(
            "m_OrientationOffsetY: " + offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static AnimationClip FirstClip(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    private static AnimationClip FindClip(string path, string suffix)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                            .FirstOrDefault(c => !c.name.StartsWith("__preview__") && c.name.EndsWith(suffix));
    }

    private static Material GetOrCreateMaterial()
    {
        PrepareTexture(NormalPath, normalMap: true, readable: false);
        PrepareTexture(MetallicPath, normalMap: false, readable: true);
        PrepareTexture(RoughnessPath, normalMap: false, readable: true);
        PrepareTexture(OcclusionPath, normalMap: false, readable: true);

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        bool created = material == null;
        if (created) material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Masked Character" };

        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        Texture2D mask = GetOrCreateMask();
        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_MainTex", albedo);
        material.SetColor("_BaseColor", Color.white);
        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
        }
        if (mask != null)
        {
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetFloat("_Smoothness", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetTexture("_OcclusionMap", mask);
            material.SetFloat("_OcclusionStrength", 1f);
            material.EnableKeyword("_OCCLUSIONMAP");
        }
        else
        {
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.3f);
        }

        if (created) AssetDatabase.CreateAsset(material, MaterialPath);
        else EditorUtility.SetDirty(material);
        return material;
    }

    private static void PrepareTexture(string path, bool normalMap, bool readable)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
        bool changed = false;
        if (normalMap && importer.textureType != TextureImporterType.NormalMap) { importer.textureType = TextureImporterType.NormalMap; changed = true; }
        if (readable && (!importer.isReadable || importer.sRGBTexture)) { importer.isReadable = true; importer.sRGBTexture = false; changed = true; }
        if (changed) importer.SaveAndReimport();
    }

    // Metal (R), oclusión (G) y suavidad = 1 - rugosidad (A): la máscara de URP Lit
    private static Texture2D GetOrCreateMask()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
        if (existing != null) return existing;

        Texture2D metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(MetallicPath);
        Texture2D roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(RoughnessPath);
        Texture2D occlusion = AssetDatabase.LoadAssetAtPath<Texture2D>(OcclusionPath);
        if (metallic == null || roughness == null || !metallic.isReadable || !roughness.isReadable) return null;

        int width = Mathf.Max(metallic.width, roughness.width);
        int height = Mathf.Max(metallic.height, roughness.height);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float metal = metallic.GetPixelBilinear(u, v).r;
                float rough = roughness.GetPixelBilinear(u, v).r;
                float ao = occlusion != null && occlusion.isReadable ? occlusion.GetPixelBilinear(u, v).r : 1f;
                pixels[y * width + x] = new Color32((byte)(metal * 255f), (byte)(ao * 255f), 0, (byte)((1f - rough) * 255f));
            }
        }

        var mask = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        mask.SetPixels32(pixels);
        mask.Apply();
        File.WriteAllBytes(MaskPath, mask.EncodeToPNG());
        Object.DestroyImmediate(mask);

        AssetDatabase.ImportAsset(MaskPath);
        if (AssetImporter.GetAtPath(MaskPath) is TextureImporter maskImporter && maskImporter.sRGBTexture)
        {
            maskImporter.sRGBTexture = false;
            maskImporter.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
    }
}
