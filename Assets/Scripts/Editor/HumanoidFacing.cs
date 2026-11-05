using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Mide para dónde queda mirando el cuerpo de un humanoide con cada animación y arma copias de las
// animaciones con la raíz girada 180° cuando hace falta. Las animaciones de este proyecto conservan
// la orientación de su archivo ("Based Upon: Original"), y no todos los paquetes miran para el mismo
// lado. Lo usan EnemyModelSetup (enemigo enmascarado) y TownShopSetup (la mercader).
public static class HumanoidFacing
{
    // Deja un modelo de prueba (en una escena aparte, en el origen) listo para muestrear el controller
    public static Animator PrepareProbe(GameObject probe, Avatar avatar, AnimatorController controller)
    {
        probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Animator animator = probe.GetComponent<Animator>();
        if (animator == null) animator = probe.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // sin cámara: si no, no mueve los huesos
        BindController(animator, controller);
        return animator;
    }

    public static void BindController(Animator animator, AnimatorController controller)
    {
        animator.runtimeAnimatorController = null;
        animator.runtimeAnimatorController = controller;
        animator.Rebind();
    }

    // Frente del cuerpo en el plano del piso, según caderas y hombros (en Unity frente = Cross(derecha,
    // arriba)). Sin muestrear nada da el frente de la pose de origen. Vector3.zero si no se puede medir.
    public static Vector3 BodyFront(Animator animator)
    {
        Transform leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (leftLeg == null || rightLeg == null || leftArm == null || rightArm == null) return Vector3.zero;
        Vector3 right = rightLeg.position - leftLeg.position + rightArm.position - leftArm.position;
        Vector3 front = Vector3.ProjectOnPlane(Vector3.Cross(right, Vector3.up), Vector3.up);
        return front.sqrMagnitude < 1e-8f ? Vector3.zero : front.normalized;
    }

    // Frente del cuerpo en el estado dado (en el momento normalizado time) comparado con reference:
    // +1 = mira hacia ahí, -1 = de espaldas
    public static float Facing(Animator animator, string state, float time, Vector3 reference)
    {
        animator.Play(state, 0, time);
        animator.Update(0f);
        Vector3 front = BodyFront(animator);
        return front == Vector3.zero || reference == Vector3.zero ? 0f : Vector3.Dot(front, reference.normalized);
    }

    // Copia de la animación (también las de un FBX) con la raíz girada 180°, en path. El giro se pone
    // antes de crear el archivo, así queda escrito desde el principio: si se cambia después, al crear
    // otra copia Unity puede recargar esta del disco sin el giro (le pasó a EnemyModelSetup en Build6).
    // Devuelve la copia tal como quedó en el disco.
    public static AnimationClip TurnedCopy(AnimationClip source, string path, out float offset)
    {
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        var copy = new AnimationClip();
        EditorUtility.CopySerialized(source, copy);
        copy.name = Path.GetFileNameWithoutExtension(path);
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.orientationOffsetY = Mathf.Repeat(settings.orientationOffsetY + 180f, 360f);
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        offset = settings.orientationOffsetY;
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    }

    // Lo que importa es lo que quedó escrito: con eso arranca Unity y con eso se entra en Play
    public static bool SavedWithOffset(string path, float offset)
    {
        return File.Exists(path) && File.ReadAllText(path).Contains(
            "m_OrientationOffsetY: " + offset.ToString(CultureInfo.InvariantCulture));
    }

    // Crea la carpeta (y las de arriba) si no existe
    public static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
