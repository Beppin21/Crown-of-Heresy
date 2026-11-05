using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Aviso en pantalla al guardar la partida ("GAME SAVED"): aparece en el centro, un poco arriba, y se
// va solo. Usa la fuente de los títulos de la tienda (ShopCatalog, en Resources).
public class SaveNotice : MonoBehaviour
{
    private const float FadeIn = 0.35f;
    private const float Hold = 1.6f;
    private const float FadeOut = 0.9f;

    private TextMeshProUGUI text;
    private float time;

    public static void Show(string message)
    {
        var canvasObject = new GameObject("Aviso de guardado", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var textObject = new GameObject("Texto", typeof(RectTransform));
        textObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.64f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1400f, 120f);

        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        ShopCatalog catalog = Resources.Load<ShopCatalog>("ShopCatalog");
        if (catalog != null && catalog.TitleFont != null) label.font = catalog.TitleFont;
        label.text = message;
        label.fontSize = 56f;
        label.characterSpacing = 8f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.94f, 0.83f, 0.55f);
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color32(0, 0, 0, 220);
        label.raycastTarget = false;
        label.alpha = 0f;

        canvasObject.AddComponent<SaveNotice>().text = label;
    }

    private void Update()
    {
        time += Time.unscaledDeltaTime;
        float alpha = time < FadeIn ? time / FadeIn
                    : time < FadeIn + Hold ? 1f
                    : 1f - (time - FadeIn - Hold) / FadeOut;
        text.alpha = Mathf.Clamp01(alpha);
        if (time > FadeIn + Hold + FadeOut) Destroy(gameObject);
    }
}
