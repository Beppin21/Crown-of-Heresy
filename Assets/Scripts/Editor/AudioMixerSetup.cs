using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

// Conecta el audio del juego al Audio Mixer (Assets/MasterMIxed/MasterMIxed.mixer) solo, sin pasos
// a mano. Corre después de cada compilación, al abrir una escena y al volver de Play (nunca en Play),
// y solo completa lo que está vacío, así que lo que se ajuste a mano en el Inspector se respeta:
//   - Prefabs: los fuegos de Vefects salen por Ambiente; los pasos del caballero por SFX.
//   - Town: crea "Musica del Pueblo" (MusicaPueblo con los tres temas, sus grupos y snapshots) y le
//     pone DialogosTienda a la tienda (con los clips de Assets/Audio/Dialogos, por Voz).
//   - Toda AudioSource de las escenas abiertas que no tenga salida va a un grupo (ninguna sin rutear).
// Después hay que guardar la escena (Ctrl+S): el log lo avisa. También: Tools > Audio > Conectar al mixer.
[InitializeOnLoad]
public static class AudioMixerSetup
{
    private const string MixerPath = "Assets/MasterMIxed/MasterMIxed.mixer";
    private const string MusicFolder = "Assets/Audio/Musica/";
    private const string PlayerPrefabPath = "Assets/Prefabs/PlayerKnight.prefab";
    private const string FirePrefabsFolder = "Assets/Vefects";
    private const string MusicObjectName = "Musica del Pueblo";

    static AudioMixerSetup()
    {
        EditorApplication.delayCall += () => Run(false);
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += () => Run(false);
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) Run(false);
        };
    }

    [MenuItem("Tools/Audio/Conectar al mixer")]
    private static void RunFromMenu() => Run(true);

    private static void Run(bool verbose)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;

        AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (mixer == null)
        {
            if (verbose) Debug.LogWarning($"[Audio] No encontré el mixer en {MixerPath}.");
            return;
        }

        AudioMixerGroup musica = Group(mixer, "Master/Musica");
        AudioMixerGroup sfx = Group(mixer, "Master/SFX");
        AudioMixerGroup ambiente = Group(mixer, "Master/Ambiente");
        AudioMixerGroup voz = Group(mixer, "Master/Voz");
        if (musica == null || sfx == null || ambiente == null || voz == null)
        {
            Debug.LogWarning("[Audio] Al mixer le faltan los grupos Musica, SFX, Ambiente o Voz: no conecté nada.");
            return;
        }

        var log = new List<string>();
        SetupFirePrefabs(ambiente, log);
        SetupPlayerPrefab(sfx, log);

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            int before = log.Count;
            if (scene.name == "Town")
                SetupTown(scene, mixer, musica, voz, log);
            RouteLooseSources(scene, sfx, ambiente, voz, log);

            if (log.Count > before)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                log.Add($"→ Guardá la escena {scene.name} (Ctrl+S).");
            }
        }

        if (log.Count > 0)
            Debug.Log("[Audio] Conectado al mixer:\n- " + string.Join("\n- ", log));
        else if (verbose)
            Debug.Log("[Audio] Todo ya estaba conectado al mixer.");
    }

    private static AudioMixerGroup Group(AudioMixer mixer, string path)
    {
        foreach (AudioMixerGroup group in mixer.FindMatchingGroups(path))
        {
            if (group.name == path.Substring(path.LastIndexOf('/') + 1))
                return group;
        }
        return null;
    }

    // ---------------------------------------------------------------
    // Prefabs
    // ---------------------------------------------------------------

    // Los fuegos de Vefects traen un AudioSource con el crepitar en loop: van por Ambiente
    private static void SetupFirePrefabs(AudioMixerGroup ambiente, List<string> log)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { FirePrefabsFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;

            bool needs = false;
            foreach (AudioSource source in asset.GetComponentsInChildren<AudioSource>(true))
                needs |= source.outputAudioMixerGroup == null;
            if (!needs) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
            {
                if (source.outputAudioMixerGroup == null)
                    source.outputAudioMixerGroup = ambiente;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            log.Add($"{asset.name}: AudioSource → Ambiente");
        }
    }

    // Los pasos del caballero (PlayerFootsteps.outputGroup) van por SFX
    private static void SetupPlayerPrefab(AudioMixerGroup sfx, List<string> log)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (asset == null) return;
        PlayerFootsteps footsteps = asset.GetComponentInChildren<PlayerFootsteps>(true);
        if (footsteps == null) return;

        SerializedProperty group = new SerializedObject(footsteps).FindProperty("outputGroup");
        if (group == null || group.objectReferenceValue != null) return;

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        SetIfEmpty(root.GetComponentInChildren<PlayerFootsteps>(true), "outputGroup", sfx);
        PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        log.Add("PlayerKnight: pasos → SFX");
    }

    // ---------------------------------------------------------------
    // Town
    // ---------------------------------------------------------------

    private static void SetupTown(Scene scene, AudioMixer mixer, AudioMixerGroup musica, AudioMixerGroup voz, List<string> log)
    {
        // Voz de la mercader: en el objeto de la tienda (ShopTrigger), que ya tiene un AudioSource
        foreach (ShopTrigger shop in Object.FindObjectsByType<ShopTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (shop.gameObject.scene != scene) continue;

            DialogosTienda dialogos = shop.GetComponent<DialogosTienda>();
            if (dialogos == null)
            {
                dialogos = shop.gameObject.AddComponent<DialogosTienda>();
                dialogos.CargarClips();
                log.Add($"Town: DialogosTienda en \"{shop.name}\" con los clips de Assets/Audio/Dialogos");
            }
            if (SetIfEmpty(dialogos, "grupoVoz", voz))
                log.Add("Town: diálogos → Voz");
            AudioSource source = shop.GetComponent<AudioSource>();
            if (source != null && source.outputAudioMixerGroup == null)
            {
                source.outputAudioMixerGroup = voz;
                EditorUtility.SetDirty(source);
            }
            break;
        }

        // Música: un objeto propio con MusicaPueblo
        MusicaPueblo musicaPueblo = null;
        foreach (MusicaPueblo found in Object.FindObjectsByType<MusicaPueblo>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (found.gameObject.scene == scene) { musicaPueblo = found; break; }
        }
        if (musicaPueblo == null)
        {
            var go = new GameObject(MusicObjectName);
            SceneManager.MoveGameObjectToScene(go, scene);
            musicaPueblo = go.AddComponent<MusicaPueblo>();
            log.Add($"Town: creé \"{MusicObjectName}\" (MusicaPueblo)");
        }

        bool changed = false;
        changed |= SetIfEmpty(musicaPueblo, "musicaNormal", AssetDatabase.LoadAssetAtPath<AudioClip>(MusicFolder + "MusicaNormal.mp3"));
        changed |= SetIfEmpty(musicaPueblo, "musicaFuego", AssetDatabase.LoadAssetAtPath<AudioClip>(MusicFolder + "MusicaFuego.mp3"));
        changed |= SetIfEmpty(musicaPueblo, "musicaTienda", AssetDatabase.LoadAssetAtPath<AudioClip>(MusicFolder + "MusicaTienda.mp3"));
        changed |= SetIfEmpty(musicaPueblo, "grupoMusica", musica);
        changed |= SetIfEmpty(musicaPueblo, "grupoNormal", Group(mixer, "Master/Musica/Normal"));
        changed |= SetIfEmpty(musicaPueblo, "grupoFuego", Group(mixer, "Master/Musica/Fuego"));
        changed |= SetIfEmpty(musicaPueblo, "grupoTienda", Group(mixer, "Master/Musica/Tienda"));
        changed |= SetIfEmpty(musicaPueblo, "snapshotNormal", mixer.FindSnapshot("Normal"));
        changed |= SetIfEmpty(musicaPueblo, "snapshotFuego", mixer.FindSnapshot("Fuego"));
        changed |= SetIfEmpty(musicaPueblo, "snapshotTienda", mixer.FindSnapshot("Tienda"));
        changed |= SetIfEmpty(musicaPueblo, "mixer", mixer);
        if (changed)
            log.Add("Town: música con sus temas, grupos (Musica/Normal, Fuego, Tienda) y snapshots");
    }

    private static bool SetIfEmpty(Object target, string field, Object value)
    {
        if (value == null) return false;
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null || property.objectReferenceValue != null) return false;
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // Lo que quede sin salida: las que están en loop son ambiente, el resto efectos
    private static void RouteLooseSources(Scene scene, AudioMixerGroup sfx, AudioMixerGroup ambiente, AudioMixerGroup voz, List<string> log)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
            {
                if (source.outputAudioMixerGroup != null) continue;
                if (source.GetComponent<MusicaPueblo>() != null) continue; // las capas las crea en Play

                AudioMixerGroup group = source.GetComponent<DialogosTienda>() != null ? voz : source.loop ? ambiente : sfx;
                source.outputAudioMixerGroup = group;
                EditorUtility.SetDirty(source);
                log.Add($"{scene.name}: AudioSource de \"{source.name}\" → {group.name}");
            }
        }
    }
}
