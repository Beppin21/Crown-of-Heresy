using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class MenuButtonHover : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler
{
    public TMP_Text texto;

    public Color colorNormal = new Color32(160, 157, 151, 255);
    public Color colorHover = new Color32(235, 224, 196, 255);

    // Si en la escena quedó sin asignar, usa el texto del propio botón (si no, tiraba
    // NullReferenceException cada vez que el mouse pasaba por arriba)
    private void Awake()
    {
        if (texto == null) texto = GetComponentInChildren<TMP_Text>(true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (texto != null) texto.color = colorHover;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (texto != null) texto.color = colorNormal;
    }
}
