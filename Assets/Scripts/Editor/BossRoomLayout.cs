using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Arreglos de la sala del jefe de Belén en la escena, versionados con BossController.RoomVersion
// (como TuningVersion): se aplican solos, una vez, al compilar, al abrir la escena o al volver de
// Play, si el jefe tiene una versión anterior. Después hay que guardar la escena.
//
// Versión 1:
// - Fase 1 al medio de la sala: el objeto Boss (y con él el esqueleto) va a mitad de camino entre
//   Astraía y la puerta del jefe. Astraía (fase 2) se queda donde estaba, arriba de la escalera:
//   BossController toma su lugar de aparición de la posición del modelo.
// - El punto de reaparición vuelve a quedar entre el jefe y la puerta, y el radio en el que empieza
//   la pelea, más corto que esa distancia.
// - Astraía mira al jugador: el giro extra (phaseTwoYawOffset) sale de sus ojos (EYE L / EYE R).
// - Las luces Spot de las ventanas de la sala, más fuertes y abiertas, cada una con un rayo de luz
//   visible (WindowLightShaft).
//
// Versión 2: los ventanales de los costados alumbran todavía más (Pablo: la ventana grande del fondo,
// que apunta para abajo, ya estaba bien y no se toca).
[InitializeOnLoad]
public static class BossRoomLayout
{
    private const float SideWindowIntensity = 2100f; // eran 600
    private const float SideWindowRange = 45f;       // eran 30
    private const float SideWindowAngle = 62f;       // eran 40
    private const float SideWindowInnerAngle = 36f;
    private const float BigWindowIntensity = 4500f;  // la ventana grande: era 1500
    private const float BigWindowRange = 50f;
    private const float SideWindowIntensityV2 = 4500f; // versión 2: eran 2100
    private const float SideWindowRangeV2 = 55f;
    private const float SideWindowAngleV2 = 75f;
    private const float SideWindowInnerAngleV2 = 45f;
    private const float RespawnDistance = 18f;       // como BossFightSetup
    private const float DoorMargin = 2.5f;           // la reaparición no queda pegada a la puerta

    static BossRoomLayout()
    {
        EditorApplication.delayCall += Run;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += Run;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Run;
        };
    }

    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        foreach (BossController controller in Object.FindObjectsByType<BossController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(controller);
            SerializedProperty version = so.FindProperty("roomVersion");
            if (version == null || version.intValue >= BossController.RoomVersion) continue;

            var report = new List<string>();
            if (version.intValue < 1) ApplyVersion1(controller, report);
            if (version.intValue < 2) ApplyVersion2(report);

            so.Update();
            so.FindProperty("roomVersion").intValue = BossController.RoomVersion;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Debug.Log($"[BossRoomLayout] Sala del jefe de '{controller.gameObject.scene.name}' actualizada " +
                      $"(versión {BossController.RoomVersion}). Guardá la escena (Ctrl+S).\n" +
                      string.Join("\n", report.Select(line => "- " + line)), controller);
        }
    }

    private static void ApplyVersion1(BossController controller, List<string> report)
    {
        Transform boss = controller.transform;
        GameObject phaseTwo = controller.PhaseTwoModel;
        BossDoor door = Object.FindFirstObjectByType<BossDoor>(FindObjectsInactive.Include);
        Physics.SyncTransforms();

        // 1. Cada fase en su lugar
        if (door == null)
        {
            report.Add("No hay puerta del jefe (BossDoor) en la escena: el jefe no se movió.");
        }
        else
        {
            Vector3 phaseTwoSpot = phaseTwo != null ? phaseTwo.transform.position : boss.position;
            Vector3 middle = Vector3.Lerp(phaseTwoSpot, door.transform.position, 0.5f);
            float from = Mathf.Max(phaseTwoSpot.y, door.transform.position.y) + 4f;
            Vector3 ground = GroundBelow(new Vector3(middle.x, from, middle.z), boss)
                             ?? new Vector3(middle.x, door.transform.position.y, middle.z);

            Undo.RecordObject(boss, "Sala del jefe");
            boss.position = ground;
            report.Add($"Fase 1 (Boss, con el esqueleto) al medio de la sala, en {Format(ground)}.");
            if (phaseTwo != null)
            {
                Undo.RecordObject(phaseTwo.transform, "Sala del jefe");
                phaseTwo.transform.position = phaseTwoSpot;
                report.Add($"Astraía (fase 2) sigue apareciendo en {Format(phaseTwoSpot)}, arriba de la escalera.");
            }
            Physics.SyncTransforms();
            PlaceRespawn(controller, door.transform.position, report);
        }

        // 2. Astraía de frente al jugador
        float? yaw = PhaseTwoYawOffset(phaseTwo);
        if (yaw.HasValue)
        {
            var so = new SerializedObject(controller);
            SerializedProperty offset = so.FindProperty("phaseTwoYawOffset");
            float before = offset.floatValue;
            offset.floatValue = yaw.Value;
            so.ApplyModifiedPropertiesWithoutUndo();
            report.Add($"Giro de Astraía para mirar al jugador: {yaw.Value:0}° (era {before:0}°), medido con sus ojos.");
        }
        else
        {
            report.Add("No encontré los ojos de Astraía (EYE L / EYE R): su giro no cambió.");
        }

        // 3. Luz de las ventanas
        Light[] spots = WindowSpots();
        foreach (Light spot in spots)
        {
            Undo.RecordObject(spot, "Sala del jefe");
            if (IsBigWindow(spot))
            {
                spot.intensity = Mathf.Max(spot.intensity, BigWindowIntensity);
                spot.range = Mathf.Max(spot.range, BigWindowRange);
            }
            else
            {
                spot.intensity = Mathf.Max(spot.intensity, SideWindowIntensity);
                spot.range = Mathf.Max(spot.range, SideWindowRange);
                spot.spotAngle = Mathf.Max(spot.spotAngle, SideWindowAngle);
                spot.innerSpotAngle = Mathf.Max(spot.innerSpotAngle, SideWindowInnerAngle);
            }
            if (spot.GetComponent<WindowLightShaft>() == null)
                Undo.AddComponent<WindowLightShaft>(spot.gameObject);
        }
        report.Add(spots.Length > 0
            ? $"{spots.Length} luces de las ventanas más fuertes (laterales {SideWindowIntensity:0}, la grande {BigWindowIntensity:0}) y con rayo de luz visible."
            : "No encontré las luces de las ventanas de la sala (Spot en 'BossRoom').");
    }

    // Los Spot de los ventanales de los costados, más fuertes y más abiertos; el de la ventana grande
    // del fondo queda como estaba
    private static void ApplyVersion2(List<string> report)
    {
        Light[] spots = WindowSpots().Where(spot => !IsBigWindow(spot)).ToArray();
        foreach (Light spot in spots)
        {
            Undo.RecordObject(spot, "Sala del jefe");
            spot.intensity = Mathf.Max(spot.intensity, SideWindowIntensityV2);
            spot.range = Mathf.Max(spot.range, SideWindowRangeV2);
            spot.spotAngle = Mathf.Max(spot.spotAngle, SideWindowAngleV2);
            spot.innerSpotAngle = Mathf.Max(spot.innerSpotAngle, SideWindowInnerAngleV2);
        }
        report.Add(spots.Length > 0
            ? $"{spots.Length} ventanales de los costados con más luz: intensidad {SideWindowIntensityV2:0}, alcance {SideWindowRangeV2:0} m y {SideWindowAngleV2:0}° de apertura. La ventana grande no se tocó."
            : "No encontré los ventanales de los costados (Spot en 'BossRoom').");
    }

    // Las luces Spot de las ventanas: las de la "BossRoom" que más tiene
    private static Light[] WindowSpots()
    {
        Transform room = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => t.name == "BossRoom")
            .OrderByDescending(t => t.GetComponentsInChildren<Light>(true).Count(l => l.type == LightType.Spot))
            .FirstOrDefault();
        return room != null ? room.GetComponentsInChildren<Light>(true).Where(l => l.type == LightType.Spot).ToArray() : new Light[0];
    }

    // La ventana grande del fondo (Window Big), que apunta para abajo
    private static bool IsBigWindow(Light spot)
    {
        for (Transform t = spot.transform; t != null; t = t.parent)
            if (t.name.StartsWith("Window Big")) return true;
        return false;
    }

    // El punto de reaparición, entre el jefe y la puerta, mirando al jefe
    private static void PlaceRespawn(BossController controller, Vector3 doorPosition, List<string> report)
    {
        Transform boss = controller.transform;
        Transform respawn = boss.Find("BossRespawn");
        if (respawn == null)
        {
            report.Add("El jefe no tiene 'BossRespawn': la reaparición no se tocó.");
            return;
        }

        Vector3 direction = doorPosition - boss.position;
        direction.y = 0f;
        float toDoor = direction.magnitude;
        if (toDoor < 0.1f) return;
        direction /= toDoor;

        float distance = Mathf.Clamp(toDoor - DoorMargin, 2f, RespawnDistance);
        Vector3 point = boss.position + direction * distance;
        point = GroundBelow(point + Vector3.up * 2f, boss) ?? point;
        Undo.RecordObject(respawn, "Sala del jefe");
        respawn.SetPositionAndRotation(point, Quaternion.LookRotation(-direction));
        report.Add($"Punto de reaparición a {distance:0.0} m del jefe, hacia la puerta, en {Format(point)}.");

        // Que al reaparecer no empiece la pelea sola
        var so = new SerializedObject(controller);
        SerializedProperty aggro = so.FindProperty("aggroRadius");
        float maxAggro = Mathf.Max(6f, distance - 2f);
        if (aggro.floatValue > maxAggro)
        {
            aggro.floatValue = maxAggro;
            so.ApplyModifiedPropertiesWithoutUndo();
            report.Add($"El radio en el que empieza la pelea bajó a {maxAggro:0.0} m.");
        }
    }

    // Giro extra para que Astraía mire hacia su +Z: el frente sale de sus ojos (en Unity la derecha
    // es Cross(arriba, frente), así que el frente es Cross(derecha, arriba))
    private static float? PhaseTwoYawOffset(GameObject phaseTwo)
    {
        if (phaseTwo == null) return null;
        bool wasActive = phaseTwo.activeSelf;
        phaseTwo.SetActive(true); // apagada no tiene cajas para medir
        try
        {
            Renderer[] renderers = phaseTwo.GetComponentsInChildren<Renderer>(true);
            Renderer left = renderers.FirstOrDefault(r => r.name.StartsWith("EYE L"));
            Renderer right = renderers.FirstOrDefault(r => r.name.StartsWith("EYE R"));
            if (left == null || right == null) return null;

            Transform root = phaseTwo.transform;
            Vector3 across = root.InverseTransformPoint(right.bounds.center) - root.InverseTransformPoint(left.bounds.center);
            across.y = 0f;
            if (across.sqrMagnitude < 1e-6f) return null;
            Vector3 front = Vector3.Cross(across, Vector3.up);
            return Mathf.Round(Vector3.SignedAngle(front, Vector3.forward, Vector3.up));
        }
        finally
        {
            phaseTwo.SetActive(wasActive);
        }
    }

    // Piso debajo de un punto, sin contar al jefe
    private static Vector3? GroundBelow(Vector3 from, Transform ignore)
    {
        float best = float.MaxValue;
        Vector3? point = null;
        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
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

    private static string Format(Vector3 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";
}
