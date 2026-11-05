using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Volume))]
public class BrilloGlobal : MonoBehaviour
{
    public const string KEY_BRILLO = "opciones_brillo";
    public const string KEY_VOLUMEN = "opciones_volumen";

    void Start()
    {
        Aplicar(GetComponent<Volume>(), PlayerPrefs.GetFloat(KEY_BRILLO, 0f));
    }

    public static void Aplicar(Volume volumen, float valor)
    {
        if (volumen == null || volumen.profile == null) return;

        if (!volumen.profile.TryGet(out ColorAdjustments ajustes))
            ajustes = volumen.profile.Add<ColorAdjustments>(true);

        ajustes.postExposure.overrideState = true;
        ajustes.postExposure.value = valor;
    }
}