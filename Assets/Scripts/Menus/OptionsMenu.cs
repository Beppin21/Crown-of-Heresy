using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using TMPro;

public class OptionsMenu : MonoBehaviour
{
    [Header("Sliders")]
    public Slider sliderVolumen;
    public Slider sliderBrillo;

    [Header("Textos de valor (opcional)")]
    public TMP_Text textoVolumen;
    public TMP_Text textoBrillo;

    [Header("FMOD")]
    public string busMaster = "bus:/";

    [Header("Brillo (opcional en esta escena)")]
    public Volume volumenGlobal;

    void Start()
    {
        float vol = PlayerPrefs.GetFloat(BrilloGlobal.KEY_VOLUMEN, 1f);
        float bri = PlayerPrefs.GetFloat(BrilloGlobal.KEY_BRILLO, 0f);

        if (sliderVolumen != null)
        {
            sliderVolumen.minValue = 0f;
            sliderVolumen.maxValue = 1f;
            sliderVolumen.SetValueWithoutNotify(vol);
            sliderVolumen.onValueChanged.AddListener(CambiarVolumen);
        }

        if (sliderBrillo != null)
        {
            sliderBrillo.minValue = -1f;
            sliderBrillo.maxValue = 1f;
            sliderBrillo.SetValueWithoutNotify(bri);
            sliderBrillo.onValueChanged.AddListener(CambiarBrillo);
        }

        AplicarVolumen(vol);
        ActualizarTextos(vol, bri);
        BrilloGlobal.Aplicar(volumenGlobal, bri);
    }

    public void CambiarVolumen(float valor)
    {
        PlayerPrefs.SetFloat(BrilloGlobal.KEY_VOLUMEN, valor);
        AplicarVolumen(valor);
        ActualizarTextos(valor, sliderBrillo != null ? sliderBrillo.value : 0f);
    }

    public void CambiarBrillo(float valor)
    {
        PlayerPrefs.SetFloat(BrilloGlobal.KEY_BRILLO, valor);
        BrilloGlobal.Aplicar(volumenGlobal, valor);
        ActualizarTextos(sliderVolumen != null ? sliderVolumen.value : 1f, valor);
    }

    void AplicarVolumen(float valor)
    {
        try
        {
            FMODUnity.RuntimeManager.GetBus(busMaster).setVolume(valor);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("No se pudo cambiar el volumen de FMOD: " + e.Message);
        }
    }

    void ActualizarTextos(float vol, float bri)
    {
        if (textoVolumen != null) textoVolumen.text = Mathf.RoundToInt(vol * 100) + "%";
        if (textoBrillo != null) textoBrillo.text = Mathf.RoundToInt((bri + 1f) * 50f) + "%";
    }
}
