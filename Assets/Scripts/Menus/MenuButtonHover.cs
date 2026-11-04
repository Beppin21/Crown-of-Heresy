

using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class MenuButtonHover : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler
{
    public TMP_Text texto;

    public Color colorNormal = new Color32(160, 157, 151, 255);
    public Color colorHover = new Color32(235, 224, 196, 255);

    public void OnPointerEnter(PointerEventData eventData)
    {
        texto.color = colorHover;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        texto.color = colorNormal;
    }
}