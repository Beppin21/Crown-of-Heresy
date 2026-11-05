using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Pasa los ataques del caballero de la Base Layer a la capa "UpperBody" de PlayerKnight.controller:
// así el golpe lo hacen solo torso, brazos y cabeza (la máscara UpperBody.mask), y las piernas siguen
// con la locomoción de la Base Layer. Junto con PlayerController.moveWhileAttacking, el personaje
// puede caminar mientras ataca dando pasos de verdad, sin patinar.
//
// Lo que cambia en el controller:
//   - Base Layer: se borran los estados Attack_Light_1/2/3 y Attack_Heavy (y con ellos sus
//     transiciones Any State -> ataque y ataque -> Locomotion).
//   - UpperBody: se crean esos 4 estados con el mismo clip y el multiplicador AttackSpeed, entrando
//     desde Any State con su trigger y volviendo a "Empty" al 90% del clip.
//
// Se aplica solo una vez sobre el controller existente (etiqueta VersionLabel en el asset), así se
// conservan los cambios hechos a mano. PlayerKnightSetup (Tools > Player > Armar Player Knight)
// llama a MoveAttacksToUpperBody al rehacer el controller, para que el cambio no se pierda.
[InitializeOnLoad]
public static class PlayerKnightAttackLayer
{
    private const string ControllerPath = "Assets/Animations/Player/PlayerKnight.controller";
    private const string UpperBodyLayer = "UpperBody";
    private const string EmptyState = "Empty";
    private const string VersionLabel = "AtaquesEnUpperBody1";

    public static readonly string[] AttackStates = { "Attack_Light_1", "Attack_Light_2", "Attack_Light_3", "Attack_Heavy" };

    static PlayerKnightAttackLayer()
    {
        // delayCall: después de la compilación, con el AssetDatabase listo
        EditorApplication.delayCall += ApplyOnce;
    }

    private static void ApplyOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) return;
        if (AssetDatabase.GetLabels(controller).Contains(VersionLabel)) return;

        if (!MoveAttacksToUpperBody(controller)) return;

        AssetDatabase.SetLabels(controller, AssetDatabase.GetLabels(controller).Append(VersionLabel).ToArray());
        AssetDatabase.SaveAssets();
        Debug.Log("[PlayerKnightAttackLayer] Ataques pasados a la capa UpperBody de PlayerKnight.controller: " +
                  "las piernas siguen caminando mientras se ataca.", controller);
    }

    // Devuelve false si el controller no tiene la capa UpperBody (no se toca nada)
    public static bool MoveAttacksToUpperBody(AnimatorController controller)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        AnimatorControllerLayer upperLayer = layers.FirstOrDefault(l => l.name == UpperBodyLayer);
        if (layers.Length == 0 || upperLayer == null)
        {
            Debug.LogWarning($"[PlayerKnightAttackLayer] '{controller.name}' no tiene la capa {UpperBodyLayer}: " +
                             "los ataques quedan en la Base Layer.", controller);
            return false;
        }

        AnimatorStateMachine baseSm = layers[0].stateMachine;
        AnimatorStateMachine upperSm = upperLayer.stateMachine;
        AnimatorState empty = FindState(upperSm, EmptyState) ?? upperSm.defaultState;
        if (empty == null)
        {
            empty = upperSm.AddState(EmptyState);
            upperSm.defaultState = empty;
        }

        for (int i = 0; i < AttackStates.Length; i++)
        {
            string name = AttackStates[i];
            AnimatorState baseState = FindState(baseSm, name);
            AnimatorState upperState = FindState(upperSm, name);

            if (upperState == null && baseState != null)
            {
                upperState = upperSm.AddState(name, new Vector3(300f, 120f * i, 0f));
                upperState.motion = baseState.motion;
                upperState.speed = baseState.speed;
                upperState.speedParameter = baseState.speedParameter;
                upperState.speedParameterActive = baseState.speedParameterActive;

                // Entra desde cualquier estado con su trigger; el combo puede repetir el mismo golpe
                AnimatorStateTransition enter = upperSm.AddAnyStateTransition(upperState);
                enter.AddCondition(AnimatorConditionMode.If, 0f, name);
                enter.hasExitTime = false;
                enter.duration = 0.1f;
                enter.canTransitionToSelf = true;

                // Al terminar el golpe la capa vuelve a no hacer nada (los brazos vuelven a la locomoción)
                AnimatorStateTransition back = upperState.AddTransition(empty);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.15f;
            }

            // En la Base Layer ya no hacen falta: si quedaran, el trigger movería todo el cuerpo
            if (baseState != null && upperState != null)
                baseSm.RemoveState(baseState);
        }

        EditorUtility.SetDirty(controller);
        return true;
    }

    private static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        return sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
    }
}
