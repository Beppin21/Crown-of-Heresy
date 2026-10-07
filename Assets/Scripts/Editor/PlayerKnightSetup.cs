using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Herramienta de editor que arma DE CERO el Player Souls-like con el modelo del Knight:
//   1. Configura las animaciones (Mixamo, Quaternius y DoubleL) y arma el Animator Controller
//      con todos los parámetros que usan PlayerController y PlayerCombat.
//   2. Crea un prefab nuevo, Assets/Prefabs/PlayerKnight.prefab: Knight + PlayerController +
//      PlayerStats + PlayerCombat + hitbox en la espada + cámara + EventSystem.
//      El Player.prefab viejo NO se toca (queda de respaldo).
//   3. En las escenas reemplaza el Player viejo por el nuevo (misma posición) y le conecta la
//      UI del Canvas (vida, inventario y plata).
//
// Se corre desde el menú: Tools > Player > Armar Player Knight (Souls-like).
// Se puede volver a correr las veces que haga falta: rehace todo desde cero.
public static class PlayerKnightSetup
{
    private const string OldPlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    private const string NewPlayerPrefabPath = "Assets/Prefabs/PlayerKnight.prefab";
    private const string CanvasPrefabPath = "Assets/Prefabs/Canvas.prefab";
    private const string KnightModelPath = "Assets/Models/Player_Knight/knight(b3_6).fbx";
    private const string AnimationsFolder = "Assets/Animations/Player";
    private const string ControllerPath = AnimationsFolder + "/PlayerKnight.controller";
    private const string UpperBodyMaskPath = AnimationsFolder + "/UpperBody.mask";
    private const string NoFrictionPath = "Assets/Materials/PlayerSinFriccion.physicMaterial";

    private const string KnightChildName = "KnightModel";
    private const string CameraTargetName = "CameraTarget";
    private const string HitboxName = "SwordHitbox";

    private static readonly string[] ScenesWithPlayer = { "Assets/Scenes/Dungeon.unity", "Assets/Scenes/Town.unity" };

    // Objetos del Canvas.prefab que usa el Player (IDs locales dentro de ese prefab)
    private const long CanvasInventoryPanelId = 2480055401952903049; // GameObject InventoryPanel
    private const long CanvasItemListTextId = 1845608281788767212;   // TextMeshProUGUI de ItemListText
    private const long CanvasMoneyTextId = 2794682604027754687;      // TextMeshProUGUI de la plata
    private const long CanvasHealthTextId = 3399443427425801710;     // TextMeshProUGUI de la vida

    private const float RollDuration = 0.6f; // = rollDuration de PlayerController

    // Mixamo (un clip por archivo)
    private const string MixamoRollPath = AnimationsFolder + "/Quick Roll To Run.fbx";
    private const string MixamoDeathPath = AnimationsFolder + "/Sword And Shield Death.fbx";

    // Quaternius, Universal Animation Library 2 (muchos clips en un solo archivo)
    private const string UalPath = AnimationsFolder + "/UAL2_Standard.fbx";
    private const string UalBlock = "Sword_Block";
    private const string UalStagger = "Hit_Knockback";
    private const string UalGuardBreak = "Idle_Shield_Break";
    private const string UalHeal = "Consume";
    private const string UalRoll = "Sword_Dash";  // respaldo si falta la rodada de Mixamo

    // DoubleL (locomoción, combo de ataques, golpe pesado y respaldos).
    // Caminar/correr usan las versiones CON avance (no "InPlace"): ese avance es el root motion
    // que mueve al personaje, así los pies no patinan.
    private const string DoubleLRoot = "Assets/DoubleL/FBX Unity/";
    private const string IdlePath = DoubleLRoot + "One Hand Up/Movement/Idle/Idle/1Hand_Up_Stand_Idle_A_2.fbx";
    private const string WalkPath = DoubleLRoot + "One Hand Up/Movement/Walk/Base/1Hand_Up_Walk_A_F.fbx";
    private const string WalkBackPath = DoubleLRoot + "One Hand Up/Movement/Walk/Base/1Hand_Up_Walk_A_B.fbx";
    private const string WalkLeftPath = DoubleLRoot + "One Hand Up/Movement/Walk/Base/1Hand_Up_Walk_A_F_L90_A.fbx";
    private const string WalkRightPath = DoubleLRoot + "One Hand Up/Movement/Walk/Base/1Hand_Up_Walk_A_F_R90_A.fbx";
    private const string RunPath = DoubleLRoot + "One Hand Up/Movement/Run/Base/1Hand_Up_Run_A_F.fbx";
    private const string RunBackPath = DoubleLRoot + "One Hand Up/Movement/Run/Base/1Hand_Up_Run_A_B.fbx";
    private const string RunLeftPath = DoubleLRoot + "One Hand Up/Movement/Run/Base/1Hand_Up_Run_A_F_L90_A.fbx";
    private const string RunRightPath = DoubleLRoot + "One Hand Up/Movement/Run/Base/1Hand_Up_Run_A_F_R90_A.fbx";

    private const string SwordGripName = "SwordGrip";
    private const string SwordHipSocketName = "SwordHipSocket";

    // Starter Assets (locomoción sin espada). No se les cambia la configuración de import porque
    // también las usa el Player.prefab viejo.
    private const string StarterAnimRoot = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/";
    private const string UnarmedIdlePath = StarterAnimRoot + "Stand--Idle.anim.fbx";
    private const string UnarmedWalkPath = StarterAnimRoot + "Locomotion--Walk_N.anim.fbx";
    private const string UnarmedRunPath = StarterAnimRoot + "Locomotion--Run_N.anim.fbx";
    private const float UnarmedWalkSpeed = 2.5f; // = walkSpeed de PlayerController (umbral del parámetro Speed)
    private const float UnarmedRunSpeed = 5.5f;  // = runSpeed de PlayerController
    private const string Attack1Path = DoubleLRoot + "One Hand Up/Attack_A/InPlace/1Hand_Up_Attack_A_1_InPlace.fbx";
    private const string Attack2Path = DoubleLRoot + "One Hand Up/Attack_A/InPlace/1Hand_Up_Attack_A_2_InPlace.fbx";
    private const string Attack3Path = DoubleLRoot + "One Hand Up/Attack_A/InPlace/1Hand_Up_Attack_A_3_InPlace.fbx";
    private const string HeavyPath = DoubleLRoot + "One Hand Up/Attack_B/InPlace/1Hand_Up_Attack_B_1_InPlace.fbx";
    private const string BlockIdlePath = DoubleLRoot + "One Hand Up/Sheild/Idle/1Hand_Up_Shield_Block_Idle_1.fbx";
    private const string BlockHitPath = DoubleLRoot + "One Hand Up/Sheild/Hit/InPlace/1Hand_Up_Shield_Block_Hit_1_InPlace.fbx";
    private const string StaggerPath = DoubleLRoot + "Hit/InPlace/Hit_B_4_InPlace.fbx";
    private const string DeathPath = DoubleLRoot + "Hit/InPlace/Hit_B_5_InPlace.fbx";

    [MenuItem("Tools/Player/Armar Player Knight (Souls-like)")]
    private static void Run()
    {
        if (!EditorUtility.DisplayDialog("Armar Player Knight",
                "Esto va a:\n\n" +
                "- Crear de cero Assets/Prefabs/PlayerKnight.prefab (Knight + PlayerController + " +
                "PlayerStats + PlayerCombat + cámara), y su Animator.\n" +
                "- En Dungeon y Town, reemplazar el Player viejo por el nuevo en la misma posición " +
                "y conectarle la UI.\n\n" +
                "Player.prefab no se toca (queda de respaldo). Conviene tener todo commiteado antes. ¿Seguimos?",
                "Sí, armar", "Cancelar"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        string originalScene = SceneManager.GetActiveScene().path;

        try
        {
            // Escena vacía de trabajo: el prefab se arma acá sin ensuciar ninguna escena del juego
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EditorUtility.DisplayProgressBar("Armar Player Knight", "Configurando animaciones...", 0.1f);
            AnimatorController controller = BuildAnimatorController(out Dictionary<string, float> clipLengths);

            EditorUtility.DisplayProgressBar("Armar Player Knight", "Armando PlayerKnight.prefab...", 0.5f);
            GameObject newPrefab = CreatePlayerPrefab(controller, clipLengths);

            EditorUtility.DisplayProgressBar("Armar Player Knight", "Reemplazando el Player en las escenas...", 0.8f);
            ReplacePlayerInScenes(newPrefab);

            Debug.Log("[PlayerKnightSetup] Listo: las escenas usan PlayerKnight.prefab. " +
                      "Revisá hacia dónde mira el Knight y el SwordHitbox (hijo de la mano derecha).");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (!string.IsNullOrEmpty(originalScene) && File.Exists(originalScene))
                EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
        }
    }

    // ---------------------------------------------------------------
    // 1. ANIMATOR CONTROLLER
    // ---------------------------------------------------------------

    private static AnimatorController BuildAnimatorController(out Dictionary<string, float> clipLengths)
    {
        // Import de los clips: Humanoid (para poder usarlos en el Knight), loops donde corresponde,
        // y rotación/altura "horneadas" en la pose. La posición XZ NO se hornea: queda como root
        // motion, que PlayerRootMotion le pasa al Rigidbody al caminar/correr (en el resto de las
        // animaciones PlayerController la ignora).
        foreach (string path in new[] { IdlePath, WalkPath, WalkBackPath, WalkLeftPath, WalkRightPath,
                                        RunPath, RunBackPath, RunLeftPath, RunRightPath, BlockIdlePath })
            ConfigureClipImport(path, _ => true, bakeHeight: true);
        foreach (string path in new[] { Attack1Path, Attack2Path, Attack3Path, HeavyPath, BlockHitPath, StaggerPath, DeathPath })
            ConfigureClipImport(path, _ => false, bakeHeight: true);

        ConfigureClipImport(MixamoRollPath, _ => false, bakeHeight: true, makeHumanoid: true);
        ConfigureClipImport(MixamoDeathPath, _ => false, bakeHeight: true, makeHumanoid: true);
        ConfigureClipImport(UalPath, name => name.EndsWith("_Loop") || ClipNameIs(name, UalBlock),
                            bakeHeight: true, makeHumanoid: true);

        // Desenvainar / envainar (opcionales): cualquier FBX de la carpeta con "draw" o "sheath" en el nombre
        string drawPath = FindAnimationFile("draw");
        string sheathePath = FindAnimationFile("sheath");
        foreach (string path in new[] { drawPath, sheathePath })
        {
            if (path != null)
                ConfigureClipImport(path, _ => false, bakeHeight: true, makeHumanoid: true);
        }

        // Caminata y quieto "tensos" sin espada (opcionales): FBX de la carpeta con "walk" o "idle"
        // en el nombre y alguna palabra de cautela (tense, cautious, scared, sneak, nervous...).
        // Tienen que tener avance propio (bajadas de Mixamo SIN tildar "In Place").
        string tenseWalkPath = FindAnimationFile("walk", TenseWords);
        string tenseIdlePath = FindAnimationFile("idle", TenseWords) ?? FindAnimationFile("look", TenseWords) ??
                               FindAnimationFile("look around");
        foreach (string path in new[] { tenseWalkPath, tenseIdlePath })
        {
            if (path != null)
                ConfigureClipImport(path, _ => true, bakeHeight: true, makeHumanoid: true);
        }

        foreach (string path in new[] { MixamoRollPath, MixamoDeathPath, UalPath, drawPath, sheathePath, tenseWalkPath, tenseIdlePath })
        {
            if (path != null) CheckHumanoidAvatar(path);
        }

        EnsureFolder(AnimationsFolder);
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath); // se rehace de cero cada vez

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // Parámetros que usan PlayerController y PlayerCombat
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
        controller.AddParameter("Armed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsBlocking", AnimatorControllerParameterType.Bool);
        foreach (string trigger in new[] { "Roll", "Attack_Light_1", "Attack_Light_2", "Attack_Light_3",
                                           "Attack_Heavy", "Stagger", "Death", "Heal",
                                           "ParrySuccess", "BlockImpact", "GuardBreak", "Draw", "Sheathe" })
            controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // Locomoción: dos sets de animaciones mezclados según "Armed" (0 = espada guardada, 1 = en la mano)
        AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree armedSwitch, 0);
        armedSwitch.blendType = BlendTreeType.Simple1D;
        armedSwitch.blendParameter = "Armed";
        armedSwitch.useAutomaticThresholds = false;

        // Sin espada: quieto / caminar / correr hacia adelante según "Speed" (el cuerpo mira hacia
        // donde camina). Si hay caminata/quieto "tensos" (Mixamo) se usan esos; si no, los del
        // muñeco de Starter Assets.
        BlendTree unarmed = armedSwitch.CreateBlendTreeChild(0f);
        unarmed.name = "Unarmed";
        unarmed.blendType = BlendTreeType.Simple1D;
        unarmed.blendParameter = "Speed";
        unarmed.useAutomaticThresholds = false;
        AnimationClip unarmedIdle = (tenseIdlePath != null ? LoadClip(tenseIdlePath) : null) ?? LoadClip(UnarmedIdlePath) ?? LoadClip(IdlePath);
        AnimationClip unarmedWalk = (tenseWalkPath != null ? LoadClip(tenseWalkPath) : null) ?? LoadClip(UnarmedWalkPath) ?? LoadClip(WalkPath);
        unarmed.AddChild(unarmedIdle, 0f);
        unarmed.AddChild(unarmedWalk, UnarmedWalkSpeed);
        unarmed.AddChild(LoadClip(UnarmedRunPath) ?? LoadClip(RunPath), UnarmedRunSpeed);
        Debug.Log($"[PlayerKnightSetup] Sin espada: quieto = '{unarmedIdle?.name}', caminar = '{unarmedWalk?.name}'" +
                  (tenseWalkPath == null ? " (para una caminata tensa, poné en " + AnimationsFolder +
                                           " un FBX con 'walk' y 'tense'/'cautious'/'scared'/'sneak' en el nombre)." : "."));

        // Con espada: Blend Tree 2D de 8 direcciones según MoveX (costado) y MoveY (adelante/atrás),
        // relativos al cuerpo. Radio 1 = caminar, radio 2 = correr. Las diagonales salen de mezclar
        // las direcciones vecinas (adelante + costado, atrás + costado).
        BlendTree tree = armedSwitch.CreateBlendTreeChild(1f);
        tree.name = "Armed";
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = "MoveX";
        tree.blendParameterY = "MoveY";
        tree.AddChild(LoadClip(IdlePath), Vector2.zero);
        tree.AddChild(LoadClip(WalkPath), new Vector2(0f, 1f));
        tree.AddChild(LoadClip(WalkBackPath), new Vector2(0f, -1f));
        tree.AddChild(LoadClip(WalkLeftPath), new Vector2(-1f, 0f));
        tree.AddChild(LoadClip(WalkRightPath), new Vector2(1f, 0f));
        tree.AddChild(LoadClip(RunPath), new Vector2(0f, 2f));
        tree.AddChild(LoadClip(RunBackPath), new Vector2(0f, -2f));
        tree.AddChild(LoadClip(RunLeftPath), new Vector2(-2f, 0f));
        tree.AddChild(LoadClip(RunRightPath), new Vector2(2f, 0f));
        sm.defaultState = locomotion;

        // Para cada acción se busca primero el clip preferido y, si no está, uno de respaldo
        AnimationClip attack1 = LoadClip(Attack1Path);
        AnimationClip attack2 = LoadClip(Attack2Path);
        AnimationClip attack3 = LoadClip(Attack3Path);
        AnimationClip heavy = LoadClip(HeavyPath);
        AnimationClip block = LoadClip(UalPath, UalBlock) ?? LoadClip(BlockIdlePath);
        AnimationClip staggerClip = LoadClip(UalPath, UalStagger) ?? LoadClip(StaggerPath);
        AnimationClip guardBreakClip = LoadClip(UalPath, UalGuardBreak) ?? staggerClip;
        AnimationClip healClip = LoadClip(UalPath, UalHeal);
        AnimationClip rollClip = LoadClip(MixamoRollPath) ?? LoadClip(UalPath, UalRoll);
        AnimationClip deathClip = LoadClip(MixamoDeathPath) ?? LoadClip(DeathPath);

        // La rodada es siempre hacia adelante: PlayerController gira el cuerpo hacia donde se rueda.
        // El clip se acelera/frena para que dure lo mismo que la rodada del código.
        AnimatorState roll = AddState(sm, "Roll", rollClip, RollSpeedFor(rollClip));
        AnimatorState light1 = AddState(sm, "Attack_Light_1", attack1);
        AnimatorState light2 = AddState(sm, "Attack_Light_2", attack2);
        AnimatorState light3 = AddState(sm, "Attack_Light_3", attack3);
        AnimatorState heavyState = AddState(sm, "Attack_Heavy", heavy);
        AnimatorState blockIdle = AddState(sm, "Block_Idle", block);
        AnimatorState blockHit = AddState(sm, "Block_Hit", LoadClip(BlockHitPath));
        AnimatorState stagger = AddState(sm, "Stagger", staggerClip);
        AnimatorState guardBreak = AddState(sm, "Guard_Break", guardBreakClip);
        AnimatorState heal = AddState(sm, "Heal", healClip);
        AnimatorState death = AddState(sm, "Death", deathClip);

        // Curación: solo desde la locomoción (no corta un ataque ni una rodada)
        if (healClip != null)
        {
            FromLocomotion(locomotion, heal, "Heal");
            BackTo(heal, locomotion, 0.9f);
        }

        // Acciones que pueden arrancar desde cualquier estado
        FromAnyState(sm, roll, "Roll", false);
        FromAnyState(sm, light1, "Attack_Light_1", true);
        FromAnyState(sm, light2, "Attack_Light_2", true);
        FromAnyState(sm, light3, "Attack_Light_3", true);
        FromAnyState(sm, heavyState, "Attack_Heavy", true);
        FromAnyState(sm, stagger, "Stagger", false);
        FromAnyState(sm, guardBreak, "GuardBreak", false);
        FromAnyState(sm, blockHit, "BlockImpact", false);
        FromAnyState(sm, blockHit, "ParrySuccess", false);
        FromAnyState(sm, death, "Death", false); // Death no tiene salida: queda en la última pose

        foreach (AnimatorState s in new[] { roll, light1, light2, light3, heavyState, stagger, guardBreak })
            BackTo(s, locomotion, 0.9f);

        // Bloqueo: se mantiene mientras IsBlocking esté en true
        AnimatorStateTransition toBlock = locomotion.AddTransition(blockIdle);
        toBlock.AddCondition(AnimatorConditionMode.If, 0f, "IsBlocking");
        toBlock.hasExitTime = false;
        toBlock.duration = 0.15f;

        AnimatorStateTransition fromBlock = blockIdle.AddTransition(locomotion);
        fromBlock.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsBlocking");
        fromBlock.hasExitTime = false;
        fromBlock.duration = 0.15f;

        BackTo(blockHit, blockIdle, 0.9f);

        // Capa "UpperBody": desenvainar/envainar mueven solo torso, brazos y cabeza, así las
        // piernas siguen con la locomoción (se puede caminar mientras se saca la espada).
        // "Empty" no tiene animación: mientras está ahí, la capa no cambia nada.
        AnimationClip drawClip = drawPath != null ? LoadClip(drawPath) : null;
        AnimationClip sheatheClip = sheathePath != null ? LoadClip(sheathePath) : null;

        AnimatorStateMachine upperSm = new AnimatorStateMachine { name = "UpperBody", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(upperSm, controller);
        controller.AddLayer(new AnimatorControllerLayer
        {
            name = "UpperBody",
            stateMachine = upperSm,
            avatarMask = GetUpperBodyMask(),
            defaultWeight = 1f,
            blendingMode = AnimatorLayerBlendingMode.Override,
        });

        AnimatorState empty = upperSm.AddState("Empty");
        upperSm.defaultState = empty;
        if (drawClip != null)
        {
            AnimatorState draw = AddState(upperSm, "Draw", drawClip);
            FromAnyState(upperSm, draw, "Draw", false);
            BackTo(draw, empty, 0.9f);
        }
        if (sheatheClip != null)
        {
            AnimatorState sheathe = AddState(upperSm, "Sheathe", sheatheClip);
            FromAnyState(upperSm, sheathe, "Sheathe", false);
            BackTo(sheathe, empty, 0.9f);
        }
        if (drawClip == null || sheatheClip == null)
            Debug.Log("[PlayerKnightSetup] Falta la animación de desenvainar o de envainar: la espada va a aparecer/desaparecer " +
                      $"sin animación. Poné en {AnimationsFolder} FBX con \"draw\" y \"sheath\" en el nombre.");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        clipLengths = new Dictionary<string, float>
        {
            { "Draw", drawClip != null ? drawClip.length : 0.3f },
            { "Sheathe", sheatheClip != null ? sheatheClip.length : 0.3f },
            { "Attack_Light_1", attack1 != null ? attack1.length : 0.6f },
            { "Attack_Light_2", attack2 != null ? attack2.length : 0.65f },
            { "Attack_Light_3", attack3 != null ? attack3.length : 0.9f },
            { "Attack_Heavy", heavy != null ? heavy.length : 1.1f },
        };
        return controller;
    }

    // Máscara de la capa UpperBody: torso, cabeza, brazos y manos (sin piernas ni root motion)
    private static AvatarMask GetUpperBodyMask()
    {
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
        }

        for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
        {
            bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                         part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                         part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                         part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
            mask.SetHumanoidBodyPartActive(part, upper);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    // Palabras que indican una animación "tensa" (caminata cautelosa de un lugar desconocido)
    private static readonly string[] TenseWords = { "tense", "cautious", "careful", "scared", "afraid", "sneak", "nervous", "tenso", "cauteloso" };

    // Busca en la carpeta de animaciones un FBX cuyo nombre contenga una palabra (sin importar
    // mayúsculas) y, si se pasan, además alguna de las palabras de 'alsoAnyOf'
    private static string FindAnimationFile(string keyword, string[] alsoAnyOf = null)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { AnimationsFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (!name.Contains(keyword)) continue;
            if (alsoAnyOf != null)
            {
                bool any = false;
                foreach (string word in alsoAnyOf) any |= name.Contains(word);
                if (!any) continue;
            }
            return path;
        }
        return null;
    }

    // Velocidad a la que hay que reproducir un clip de rodada para que dure lo mismo que la rodada del código
    private static float RollSpeedFor(AnimationClip clip)
    {
        return clip != null ? Mathf.Clamp(clip.length / RollDuration, 0.5f, 3f) : 1f;
    }

    private static AnimatorState AddState(AnimatorStateMachine sm, string name, AnimationClip clip, float speed = 1f)
    {
        AnimatorState state = sm.AddState(name);
        state.motion = clip;
        state.speed = speed;
        return state;
    }

    private static void FromLocomotion(AnimatorState locomotion, AnimatorState target, string trigger)
    {
        AnimatorStateTransition t = locomotion.AddTransition(target);
        t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        t.hasExitTime = false;
        t.duration = 0.1f;
    }

    private static void FromAnyState(AnimatorStateMachine sm, AnimatorState target, string trigger, bool canRepeat)
    {
        AnimatorStateTransition t = sm.AddAnyStateTransition(target);
        t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        t.hasExitTime = false;
        t.duration = 0.1f;
        t.canTransitionToSelf = canRepeat; // los ataques del combo pueden repetirse, el resto no
    }

    private static void BackTo(AnimatorState from, AnimatorState to, float exitTime)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.duration = 0.15f;
    }

    private static void ConfigureClipImport(string path, System.Func<string, bool> isLoop, bool bakeHeight, bool makeHumanoid = false)
    {
        if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
        {
            Debug.LogWarning($"[PlayerKnightSetup] No se encontró el archivo de animación '{path}'.");
            return;
        }

        if (makeHumanoid && importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport(); // primero se genera el Avatar, después se configuran los clips
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;

        foreach (ModelImporterClipAnimation clip in clips)
        {
            bool loop = isLoop(clip.name);
            clip.loopTime = loop;
            clip.loopPose = loop;
            clip.lockRootRotation = true;      // rotación horneada: no gira solo
            clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = bakeHeight; // altura horneada (basada en los pies): no flota ni se hunde
            clip.heightFromFeet = true;
            clip.lockRootPositionXZ = false;   // queda como root motion (ver PlayerRootMotion)
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    // Carga un clip de un FBX. Si el FBX trae varios (como el de Quaternius), se elige por nombre.
    private static AnimationClip LoadClip(string path, string clipName = null)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
            if (clipName == null || ClipNameIs(clip.name, clipName))
                return clip;
        }

        Debug.LogWarning($"[PlayerKnightSetup] No se encontró el AnimationClip '{clipName ?? "(cualquiera)"}' en '{path}'.");
        return null;
    }

    // En el FBX de Quaternius los clips se llaman "Armature|Nombre": se compara solo el "Nombre".
    private static bool ClipNameIs(string fullName, string shortName)
    {
        return fullName == shortName || fullName.EndsWith("|" + shortName);
    }

    // Si Unity no pudo mapear solo los huesos a Humanoid, esas animaciones no se van a ver en el Knight.
    private static void CheckHumanoidAvatar(string path)
    {
        if (!File.Exists(path)) return;

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Avatar avatar)
            {
                if (!avatar.isValid || !avatar.isHuman)
                    Debug.LogWarning($"[PlayerKnightSetup] El Avatar de '{path}' no es un Humanoid válido. " +
                                     "Seleccioná el FBX > Rig > Configure... y revisá el mapeo de huesos.");
                return;
            }
        }
        Debug.LogWarning($"[PlayerKnightSetup] '{path}' no generó Avatar: revisá que en Rig esté como Humanoid.");
    }

    // ---------------------------------------------------------------
    // 2. PREFAB NUEVO DEL PLAYER
    //
    //   PlayerKnight            (raíz, sin tag: solo agrupa)
    //   ├── Player              (tag Player: Rigidbody, cápsula y todos los scripts del jugador)
    //   │   ├── KnightModel     (modelo + Animator)
    //   │   │   └── ...mano derecha/SwordHitbox
    //   │   └── CameraTarget    (punto que sigue la cámara)
    //   ├── MainCamera          (cámara orbital: script Camera)
    //   └── EventSystem         (para la UI)
    // ---------------------------------------------------------------

    private static GameObject CreatePlayerPrefab(AnimatorController controller, Dictionary<string, float> clipLengths)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(KnightModelPath);
        if (modelAsset == null)
            throw new System.Exception($"No se encontró el modelo del Knight en '{KnightModelPath}'.");

        // Del Player viejo se copian la cámara, el EventSystem y los ajustes de algunos scripts
        GameObject oldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OldPlayerPrefabPath);
        GameObject oldCharacter = oldPrefab != null ? FindCharacter(oldPrefab) : null;

        GameObject root = new GameObject("PlayerKnight");
        try
        {
            GameObject player = new GameObject("Player");
            player.transform.SetParent(root.transform, false);
            player.tag = "Player";
            player.layer = oldCharacter != null ? oldCharacter.layer : 0;

            // --- Modelo ---
            GameObject knight = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, player.transform);
            knight.name = KnightChildName;
            knight.transform.localPosition = Vector3.zero;
            knight.transform.localRotation = Quaternion.identity;
            knight.transform.localScale = Vector3.one;

            // Si el modelo viene en otra escala (cm, por ejemplo), lo llevamos a ~1.8 m de alto
            Bounds bounds = GetRendererBounds(knight);
            if (bounds.size.y > 0.01f && (bounds.size.y < 1.2f || bounds.size.y > 2.6f))
            {
                knight.transform.localScale = Vector3.one * (1.8f / bounds.size.y);
                bounds = GetRendererBounds(knight);
            }
            // Pies apoyados en el origen del jugador
            knight.transform.position += Vector3.up * (player.transform.position.y - bounds.min.y);
            bounds = GetRendererBounds(knight);
            float height = bounds.size.y;

            Animator animator = knight.GetComponent<Animator>();
            if (animator == null) animator = knight.AddComponent<Animator>();
            if (animator.avatar == null) animator.avatar = LoadAvatar(KnightModelPath);
            animator.runtimeAnimatorController = controller;
            // Root motion: al caminar/correr el avance lo da la animación (PlayerRootMotion se lo pasa
            // al Rigidbody). Se actualiza en el paso de física para ir sincronizado con el Rigidbody.
            animator.applyRootMotion = true;
            animator.updateMode = AnimatorUpdateMode.Fixed;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            knight.AddComponent<PlayerRootMotion>();
            AddFootsteps(knight, oldCharacter);

            Transform rightHand = FindHumanBone(knight, animator.avatar, "RightHand");
            WeaponHitbox hitbox = SetupSword(knight, rightHand, player.layer);
            AttachLoosePropsToBones(knight, animator.avatar);
            Transform hipSocket = CreateHipSocket(knight, animator.avatar, player.transform, height);
            SetLayerRecursively(knight, player.layer);

            GameObject cameraTarget = new GameObject(CameraTargetName);
            cameraTarget.transform.SetParent(player.transform, false);
            cameraTarget.transform.localPosition = new Vector3(0f, height * 0.85f, 0f);

            // --- Física: Rigidbody + cápsula sin fricción (para no quedar pegado a las paredes) ---
            Rigidbody rb = player.AddComponent<Rigidbody>();
            rb.mass = 70f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = height;
            capsule.radius = Mathf.Min(0.35f, height * 0.18f);
            capsule.center = new Vector3(0f, height * 0.5f, 0f);
            capsule.sharedMaterial = GetNoFrictionMaterial();

            // --- Scripts del jugador ---
            player.AddComponent<PlayerStats>();
            PlayerCombat combat = player.AddComponent<PlayerCombat>();
            PlayerInteraction interaction = player.AddComponent<PlayerInteraction>();
            InventoryUI inventory = player.AddComponent<InventoryUI>();
            PlayerController controllerScript = player.AddComponent<PlayerController>();
            PlayerWeaponSheath sheath = player.AddComponent<PlayerWeaponSheath>();

            // Desenvainar/envainar: qué objeto ocultar y cuánto duran las animaciones
            SerializedObject sheathSo = new SerializedObject(sheath);
            Transform swordGrip = FindDeep(knight.transform, SwordGripName);
            Renderer swordRenderer = swordGrip != null ? swordGrip.GetComponentInChildren<Renderer>(true) : null;
            sheathSo.FindProperty("sword").objectReferenceValue = swordRenderer != null ? swordRenderer.transform : null;
            sheathSo.FindProperty("handGrip").objectReferenceValue = swordGrip;
            sheathSo.FindProperty("hipSocket").objectReferenceValue = hipSocket;
            sheathSo.FindProperty("drawDuration").floatValue = clipLengths["Draw"];
            sheathSo.FindProperty("sheatheDuration").floatValue = clipLengths["Sheathe"];
            sheathSo.ApplyModifiedPropertiesWithoutUndo();

            // Los ajustes del Player viejo (rango de interacción, capas, etc.) se conservan
            int hittableLayers = ~0;
            if (oldCharacter != null)
            {
                CopySettings(oldCharacter.GetComponent<PlayerInteraction>(), interaction);
                CopySettings(oldCharacter.GetComponent<InventoryUI>(), inventory);

                Component combat1 = FindComponentByTypeName(oldCharacter, "Combat1");
                if (combat1 != null)
                {
                    int oldMask = new SerializedObject(combat1).FindProperty("hittableLayers").intValue;
                    if (oldMask != 0) hittableLayers = oldMask;
                }
            }

            // PlayerCombat: hitbox, capas y duración real de cada animación de ataque
            SerializedObject combatSo = new SerializedObject(combat);
            combatSo.FindProperty("weaponHitbox").objectReferenceValue = hitbox;
            combatSo.FindProperty("hittableLayers").intValue = hittableLayers;
            SerializedProperty combo = combatSo.FindProperty("lightComboSequence");
            for (int i = 0; i < combo.arraySize; i++)
            {
                SerializedProperty attack = combo.GetArrayElementAtIndex(i);
                string trigger = attack.FindPropertyRelative("animationTrigger").stringValue;
                if (clipLengths.TryGetValue(trigger, out float length))
                    attack.FindPropertyRelative("animationLength").floatValue = length * 0.9f; // = exit time del Animator
            }
            combatSo.FindProperty("heavyAttack").FindPropertyRelative("animationLength").floatValue = clipLengths["Attack_Heavy"] * 0.9f;
            combatSo.ApplyModifiedPropertiesWithoutUndo();

            // --- Cámara orbital ---
            GameObject oldCamera = null;
            if (oldPrefab != null)
            {
                ThirdPersonCamera oldOrbit = oldPrefab.GetComponentInChildren<ThirdPersonCamera>(true);
                if (oldOrbit != null) oldCamera = oldOrbit.gameObject;
            }

            GameObject cameraObject;
            if (oldCamera != null)
            {
                cameraObject = Object.Instantiate(oldCamera, root.transform); // copia con sus ajustes (URP, sensibilidad, colisiones)
            }
            else
            {
                cameraObject = new GameObject("MainCamera", typeof(UnityEngine.Camera), typeof(AudioListener), typeof(ThirdPersonCamera));
                cameraObject.transform.SetParent(root.transform, false);
            }
            cameraObject.name = "MainCamera";
            cameraObject.tag = "MainCamera";
            cameraObject.transform.localPosition = new Vector3(0f, height, -3f);
            cameraObject.transform.localRotation = Quaternion.identity;

            ThirdPersonCamera orbit = cameraObject.GetComponent<ThirdPersonCamera>();
            // Estilo Silent Hill 2 (sobre el hombro). Para el estilo souls: clic derecho sobre el
            // componente Camera > "Preset: Souls".
            orbit.target = cameraTarget.transform;
            orbit.PresetSilentHill();
            if (orbit.obstacleMask == 0)
                orbit.obstacleMask = ~(1 << player.layer); // choca con todo menos con el propio jugador

            SerializedObject controllerSo = new SerializedObject(controllerScript);
            controllerSo.FindProperty("cameraTransform").objectReferenceValue = cameraObject.transform;
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            // --- EventSystem (la UI del inventario lo necesita) ---
            GameObject oldEventSystem = oldPrefab != null ? FindChildWithComponent(oldPrefab, "EventSystem") : null;
            if (oldEventSystem != null)
                Object.Instantiate(oldEventSystem, root.transform).name = "EventSystem";
            else
                Debug.LogWarning("[PlayerKnightSetup] No se encontró un EventSystem para copiar: si la UI no responde, agregá uno a la escena.");

            // --- Cámara de Cinemachine (reemplaza a ThirdPersonCamera) ---
            CinemachineSetup.Setup(root);

            // --- Lámpara de aceite (solo se ve en zonas en modo lámpara, ej. Town) ---
            LanternSetup.Setup(root);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, NewPlayerPrefabPath);
            if (prefab == null)
                throw new System.Exception($"No se pudo guardar '{NewPlayerPrefabPath}'.");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // El objeto "personaje" de un prefab de player: el que tiene los scripts del jugador.
    private static GameObject FindCharacter(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.GetComponent<InventoryUI>() != null || t.GetComponent<PlayerController>() != null ||
                FindComponentByTypeName(t.gameObject, "ThirdPersonController") != null)
                return t.gameObject;
        }
        return null;
    }

    // Arma la espada:
    //   mano derecha
    //   └── SwordGrip      ← rotando/moviendo ESTE objeto se acomoda la espada en la mano
    //       └── sword      (el modelo de la espada)
    //           └── SwordHitbox (trigger con la forma exacta de la hoja: gira y se mueve con ella)
    // En el modelo la espada es un objeto rígido suelto (no está pegado a ningún hueso), por eso
    // hay que colgarla de la mano para que siga las animaciones.
    private static WeaponHitbox SetupSword(GameObject knight, Transform rightHand, int layer)
    {
        Renderer sword = null;
        foreach (Renderer r in knight.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.ToLowerInvariant().Contains("sword")) { sword = r; break; }
        }

        if (rightHand == null)
        {
            Debug.LogWarning("[PlayerKnightSetup] No se encontró el hueso de la mano derecha: la espada queda en el modelo y hay que moverla a mano.");
            rightHand = knight.transform;
        }

        GameObject grip = new GameObject(SwordGripName);
        grip.transform.SetParent(rightHand, false);

        GameObject hitboxObject = new GameObject(HitboxName);
        hitboxObject.layer = layer;
        BoxCollider box = hitboxObject.AddComponent<BoxCollider>();
        box.isTrigger = true;

        Mesh swordMesh = sword != null && sword.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        if (sword != null && !(sword is SkinnedMeshRenderer) && swordMesh != null)
        {
            UnpackIfNeeded(knight);
            sword.transform.SetParent(grip.transform, true); // conserva cómo la sostenía en la pose del modelo

            // La caja usa el tamaño de la malla en el espacio de la propia espada: queda alineada con la hoja
            hitboxObject.transform.SetParent(sword.transform, false);
            box.center = swordMesh.bounds.center;
            box.size = swordMesh.bounds.size;
        }
        else
        {
            if (sword == null)
                Debug.LogWarning("[PlayerKnightSetup] No se encontró la espada en el modelo: el hitbox tiene un tamaño genérico, ajustalo a mano.");
            hitboxObject.transform.SetParent(grip.transform, false);
            Vector3 scale = hitboxObject.transform.lossyScale;
            box.size = new Vector3(0.1f / scale.x, 0.1f / scale.y, 1f / scale.z);
            box.center = new Vector3(0f, 0f, 0.5f / scale.z);
        }

        return hitboxObject.AddComponent<WeaponHitbox>();
    }

    // Objetos rígidos sueltos del modelo (por ejemplo las rosas del pecho): como no están pegados
    // a ningún hueso, se quedarían flotando cuando el cuerpo se mueve. Se cuelgan del hueso más
    // cercano para que sigan la animación.
    // Receptor de los eventos de pasos (OnFootstep / OnLand) que traen las animaciones de Starter
    // Assets. Usa los mismos sonidos que tenía el ThirdPersonController del Player viejo.
    private static void AddFootsteps(GameObject knight, GameObject oldCharacter)
    {
        PlayerFootsteps footsteps = knight.AddComponent<PlayerFootsteps>();
        Component oldController = oldCharacter != null ? FindComponentByTypeName(oldCharacter, "ThirdPersonController") : null;
        if (oldController == null) return;

        SerializedObject src = new SerializedObject(oldController);
        SerializedObject dst = new SerializedObject(footsteps);

        SerializedProperty srcClips = src.FindProperty("FootstepAudioClips");
        SerializedProperty dstClips = dst.FindProperty("footstepClips");
        if (srcClips != null)
        {
            dstClips.arraySize = srcClips.arraySize;
            for (int i = 0; i < srcClips.arraySize; i++)
                dstClips.GetArrayElementAtIndex(i).objectReferenceValue = srcClips.GetArrayElementAtIndex(i).objectReferenceValue;
        }

        SerializedProperty landing = src.FindProperty("LandingAudioClip");
        if (landing != null) dst.FindProperty("landingClip").objectReferenceValue = landing.objectReferenceValue;

        SerializedProperty volume = src.FindProperty("FootstepAudioVolume");
        if (volume != null) dst.FindProperty("volume").floatValue = volume.floatValue;

        dst.ApplyModifiedPropertiesWithoutUndo();
    }

    // Punto en la cadera izquierda donde cuelga la espada guardada (SwordHipSocket, hijo del hueso
    // Hips). Se orienta para que la empuñadura quede a la altura de la cadera y la hoja apunte
    // hacia abajo y un poco hacia atrás, con el lado plano mirando hacia afuera.
    // Para acomodarla distinto, mover/rotar SwordHipSocket en el prefab.
    private static Transform CreateHipSocket(GameObject knight, Avatar avatar, Transform player, float height)
    {
        Transform hips = FindHumanBone(knight, avatar, "Hips");
        Transform leftLeg = FindHumanBone(knight, avatar, "LeftUpperLeg");
        if (hips == null)
        {
            Debug.LogWarning("[PlayerKnightSetup] No se encontró el hueso Hips: la espada guardada se va a ocultar en vez de ir a la cintura.");
            return null;
        }

        float scale = height / 1.8f;
        Vector3 forward = player.forward;
        Vector3 left = -player.right;

        // Al costado izquierdo de la cadera, apenas adelante
        Vector3 side = leftLeg != null ? Vector3.ProjectOnPlane(leftLeg.position - hips.position, Vector3.up) : left * 0.1f * scale;
        Vector3 position = hips.position + side.normalized * Mathf.Max(side.magnitude * 1.8f, 0.17f * scale)
                           + forward * 0.06f * scale;

        // Orientación: el eje largo de la malla es la hoja, el más corto es el grosor
        Quaternion rotation = Quaternion.LookRotation(Vector3.down, left);
        Transform grip = FindDeep(knight.transform, SwordGripName);
        Renderer sword = grip != null ? grip.GetComponentInChildren<Renderer>(true) : null;
        Mesh mesh = sword != null && sword.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        if (mesh != null)
        {
            Bounds b = mesh.bounds;
            int longAxis = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : (b.size.y >= b.size.z ? 1 : 2);
            int thinAxis = b.size.x <= b.size.y && b.size.x <= b.size.z ? 0 : (b.size.y <= b.size.z ? 1 : 2);
            if (thinAxis == longAxis) thinAxis = (longAxis + 1) % 3;

            Vector3 bladeLocal = Vector3.zero;
            bladeLocal[longAxis] = b.center[longAxis] >= 0f ? 1f : -1f; // desde la empuñadura (pivote) hacia la punta
            Vector3 flatLocal = Vector3.zero;
            flatLocal[thinAxis] = 1f;

            Vector3 bladeWorld = (Vector3.down + -forward * 0.45f).normalized; // hacia abajo y un poco hacia atrás
            rotation = Quaternion.LookRotation(bladeWorld, left) * Quaternion.Inverse(Quaternion.LookRotation(bladeLocal, flatLocal));
        }

        GameObject socket = new GameObject(SwordHipSocketName);
        socket.transform.SetPositionAndRotation(position, rotation);
        socket.transform.SetParent(hips, true);
        return socket.transform;
    }

    private static void AttachLoosePropsToBones(GameObject knight, Avatar avatar)
    {
        if (avatar == null || !avatar.isHuman) return;

        List<Transform> bones = new List<Transform>();
        foreach (HumanBone bone in avatar.humanDescription.human)
        {
            Transform t = FindDeep(knight.transform, bone.boneName);
            if (t != null) bones.Add(t);
        }
        if (bones.Count == 0) return;

        foreach (MeshRenderer prop in knight.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (prop.transform.parent != knight.transform) continue; // ya está colgado de algo (la espada, por ejemplo)

            Vector3 center = prop.bounds.center;
            Transform closest = bones[0];
            foreach (Transform bone in bones)
            {
                if ((bone.position - center).sqrMagnitude < (closest.position - center).sqrMagnitude)
                    closest = bone;
            }

            UnpackIfNeeded(knight);
            prop.transform.SetParent(closest, true);
            Debug.Log($"[PlayerKnightSetup] '{prop.name}' quedó pegado al hueso '{closest.name}'.");
        }
    }

    private static void UnpackIfNeeded(GameObject knight)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(knight))
            PrefabUtility.UnpackPrefabInstance(knight, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
    }

    private static Transform FindHumanBone(GameObject model, Avatar avatar, string humanBoneName)
    {
        if (avatar == null || !avatar.isHuman) return null;

        foreach (HumanBone bone in avatar.humanDescription.human)
        {
            if (bone.humanName == humanBoneName)
                return FindDeep(model.transform, bone.boneName);
        }
        return null;
    }

    private static Avatar LoadAvatar(string modelPath)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
        {
            if (asset is Avatar avatar) return avatar;
        }
        Debug.LogWarning($"[PlayerKnightSetup] El modelo '{modelPath}' no tiene Avatar: revisá que en Rig esté como Humanoid.");
        return null;
    }

    private static PhysicsMaterial GetNoFrictionMaterial()
    {
        PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(NoFrictionPath);
        if (material != null) return material;

        EnsureFolder(Path.GetDirectoryName(NoFrictionPath).Replace('\\', '/'));
        material = new PhysicsMaterial("PlayerSinFriccion")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
        };
        AssetDatabase.CreateAsset(material, NoFrictionPath);
        return material;
    }

    // ---------------------------------------------------------------
    // 3. ESCENAS
    // ---------------------------------------------------------------

    // En cada escena: pone el PlayerKnight donde estaba el Player viejo, le conecta la UI del
    // Canvas y borra el viejo. Si ya había un PlayerKnight (corrida anterior), también lo reemplaza.
    private static void ReplacePlayerInScenes(GameObject newPrefab)
    {
        GameObject oldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OldPlayerPrefabPath);

        foreach (string scenePath in ScenesWithPlayer)
        {
            if (!File.Exists(scenePath)) continue;

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            List<GameObject> oldInstances = new List<GameObject>();
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                foreach (Transform t in sceneRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
                    Object source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                    if (source == oldPrefab || source == newPrefab)
                        oldInstances.Add(t.gameObject);
                }
            }

            if (oldInstances.Count == 0)
            {
                Debug.LogWarning($"[PlayerKnightSetup] {scenePath}: no hay ningún Player para reemplazar. Arrastrá PlayerKnight.prefab a mano.");
                continue;
            }

            // Posición: la del personaje viejo si existe; si no (lo borraron en la escena), la del prefab
            GameObject old = oldInstances[0];
            GameObject oldCharacter = FindCharacter(old);
            Transform spawn = oldCharacter != null ? oldCharacter.transform : old.transform;
            Vector3 position = spawn.position;
            Quaternion rotation = Quaternion.Euler(0f, spawn.eulerAngles.y, 0f);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab, scene);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.SetSiblingIndex(old.transform.GetSiblingIndex());

            ConnectCanvasUI(instance, scene, scenePath);

            foreach (GameObject o in oldInstances)
                Object.DestroyImmediate(o);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[PlayerKnightSetup] {scenePath}: Player reemplazado por PlayerKnight.");
        }
    }

    // Busca en la escena los objetos del Canvas (instancia de Canvas.prefab) que usa el jugador
    // y se los asigna a InventoryUI y PlayerStats.
    private static void ConnectCanvasUI(GameObject playerInstance, Scene scene, string scenePath)
    {
        string canvasGuid = AssetDatabase.AssetPathToGUID(CanvasPrefabPath);
        Dictionary<long, Object> canvasObjects = new Dictionary<long, Object>();

        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            foreach (Transform t in sceneRoot.GetComponentsInChildren<Transform>(true))
            {
                RegisterCanvasObject(t.gameObject, canvasGuid, canvasObjects);
                foreach (Component c in t.GetComponents<Component>())
                    RegisterCanvasObject(c, canvasGuid, canvasObjects);
            }
        }

        InventoryUI inventory = playerInstance.GetComponentInChildren<InventoryUI>(true);
        PlayerStats stats = playerInstance.GetComponentInChildren<PlayerStats>(true);

        SerializedObject inventorySo = new SerializedObject(inventory);
        bool allFound = TryAssign(inventorySo, "inventoryPanel", canvasObjects, CanvasInventoryPanelId);
        allFound &= TryAssign(inventorySo, "itemListText", canvasObjects, CanvasItemListTextId);
        allFound &= TryAssign(inventorySo, "moneyDisplay", canvasObjects, CanvasMoneyTextId);
        inventorySo.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject statsSo = new SerializedObject(stats);
        allFound &= TryAssign(statsSo, "healthText", canvasObjects, CanvasHealthTextId);
        statsSo.ApplyModifiedPropertiesWithoutUndo();

        if (!allFound)
            Debug.LogWarning($"[PlayerKnightSetup] {scenePath}: no se encontró toda la UI del Canvas. " +
                             "Asigná a mano los textos/panel que falten en InventoryUI y PlayerStats del Player.");
    }

    private static void RegisterCanvasObject(Object sceneObject, string canvasGuid, Dictionary<long, Object> canvasObjects)
    {
        // Se recorre la cadena de prefabs (por si el objeto es un prefab anidado dentro del Canvas)
        Object source = PrefabUtility.GetCorrespondingObjectFromSource(sceneObject);
        while (source != null)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId) && guid == canvasGuid)
            {
                canvasObjects[localId] = sceneObject;
                return;
            }
            source = PrefabUtility.GetCorrespondingObjectFromSource(source);
        }
    }

    private static bool TryAssign(SerializedObject so, string property, Dictionary<long, Object> canvasObjects, long canvasId)
    {
        if (!canvasObjects.TryGetValue(canvasId, out Object value)) return false;
        so.FindProperty(property).objectReferenceValue = value;
        return true;
    }

    // ---------------------------------------------------------------
    // UTILIDADES
    // ---------------------------------------------------------------

    // Copia los valores de un componente a otro del mismo tipo (sin pisar referencias a objetos
    // del prefab viejo, que no sirven en el nuevo).
    private static void CopySettings(Component from, Component to)
    {
        if (from == null || to == null) return;

        SerializedObject src = new SerializedObject(from);
        SerializedObject dst = new SerializedObject(to);
        SerializedProperty it = src.GetIterator();
        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (it.name == "m_Script" || it.propertyType == SerializedPropertyType.ObjectReference) continue;
            dst.CopyFromSerializedProperty(it);
        }
        dst.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject FindChildWithComponent(GameObject root, string typeName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (FindComponentByTypeName(t.gameObject, typeName) != null) return t.gameObject;
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

    private static Bounds GetRendererBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static Component FindComponentByTypeName(GameObject go, string typeName)
    {
        foreach (Component c in go.GetComponents<Component>())
        {
            if (c != null && c.GetType().Name == typeName) return c;
        }
        return null;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
