using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Barra de vida o de estamina del jugador, estilo souls. Las arma sola PlayerStats al arrancar:
// la de vida en el lugar donde estaba el texto de vida (que queda adentro mostrando "73 / 100") y
// la de estamina justo arriba. El jefe usa la misma barra, más ancha y abajo (CreateBossBar).
//
//   - Al bajar el valor, el color baja al instante, pero la parte perdida queda marcada en un color
//     claro ("chip") durante chipDelay segundos; después se va consumiendo hasta el valor real:
//     rápido al principio y más lento al final.
//   - Si vuelve a bajar mientras la parte marcada todavía no se consumió, se acumula y la espera
//     vuelve a empezar (en la estamina, correr la mantiene marcada hasta que se suelta).
//   - Al subir (curarse), se ve en verde hasta dónde va a llegar y el color sube de a poco. La
//     estamina sube al instante, porque ya se regenera de a poco.
//   - Los golpes fuertes hacen un destello blanco y sacuden la barra. Con poca vida, el rojo late.
//
// Las barras son Images sin sprite (rectángulos de color) y su largo se maneja con el anchorMax
// de su RectTransform: no hace falta ningún sprite ni tocar el Canvas a mano.
public class StatBarUI : MonoBehaviour
{
    [Header("Tamaño")]
    [SerializeField] private Vector2 size = new Vector2(560f, 34f);
    [SerializeField] private float border = 3f;
    [SerializeField] private float labelFontSize = 34f;

    [Header("Colores")]
    [SerializeField] private Color frameColor = new Color(0.02f, 0.02f, 0.02f, 0.9f);
    [SerializeField] private Color backgroundColor = new Color(0.11f, 0.03f, 0.05f, 0.9f);
    [SerializeField] private Color fillColor = new Color(0.45f, 0.03f, 0.1f, 1f);   // bordó
    [SerializeField] private Color chipColor = new Color(1f, 0.82f, 0.45f, 1f);
    [SerializeField] private Color riseColor = new Color(0.45f, 0.9f, 0.45f, 0.85f);
    [SerializeField] private Color lowColor = new Color(0.78f, 0.12f, 0.2f, 1f);

    [Header("Animación")]
    [Tooltip("Segundos que la parte perdida queda marcada antes de empezar a bajar.")]
    [SerializeField] private float chipDelay = 0.6f;
    [Tooltip("Qué tan rápido baja la parte marcada (más alto = más rápido).")]
    [SerializeField] private float chipSpeed = 2.5f;
    [Tooltip("Cuánta barra sube por segundo cuando el valor aumenta (1 = la barra entera). 0 = al instante.")]
    [SerializeField] private float riseSpeed = 0.6f;
    [Tooltip("Una bajada de al menos esta fracción de la barra hace destello y sacudón (0.02 = 2%).")]
    [SerializeField] private float hitThreshold = 0.02f;
    [SerializeField] private float flashDuration = 0.15f;
    [SerializeField] private float shakeDuration = 0.25f;
    [SerializeField] private float shakeStrength = 6f;
    [Tooltip("Por debajo de esta fracción, el color late. 0 = nunca.")]
    [SerializeField] private float lowThreshold = 0.25f;

    private RectTransform rect;
    private RectTransform fill;
    private RectTransform chip;
    private RectTransform rise;
    private Image fillImage;
    private TextMeshProUGUI label;
    private Vector2 basePosition;

    private bool initialized;
    private float target;     // valor real (0..1)
    private float shown;      // lo que muestra el color (sube de a poco)
    private float chipValue;  // hasta dónde llega la parte marcada
    private float chipTimer;
    private float flashTimer;
    private float shakeTimer;

    // Barra de vida: se arma en el lugar del texto de vida y mete el texto adentro (con la fuente
    // indicada, si hay una)
    public static StatBarUI CreateHealthBar(TextMeshProUGUI healthText, TMP_FontAsset font)
    {
        RectTransform textRect = healthText.rectTransform;
        StatBarUI bar = CreateObject("HealthBar");
        bar.Build(textRect.parent, textRect.GetSiblingIndex(), textRect.anchorMin, textRect.anchorMax,
                  textRect.pivot, textRect.anchoredPosition);
        bar.SetLabel(healthText, font);
        return bar;
    }

    // Barra de estamina: más finita y azul, justo arriba de la de vida, alineada a la izquierda
    public static StatBarUI CreateStaminaBar(StatBarUI healthBar)
    {
        StatBarUI bar = CreateObject("StaminaBar");
        bar.size = new Vector2(healthBar.size.x * 0.8f, 18f);
        bar.border = 2f;
        bar.backgroundColor = new Color(0.03f, 0.06f, 0.14f, 0.9f);
        bar.fillColor = new Color(0.18f, 0.42f, 0.9f, 1f);
        bar.riseSpeed = 0f;
        bar.hitThreshold = 0.1f;   // solo la rodada o un golpe se notan; correr no sacude
        bar.shakeStrength = 3f;
        bar.lowThreshold = 0f;

        RectTransform health = healthBar.rect;
        float gap = 8f;
        // Mismo borde izquierdo que la barra de vida, un poco más arriba
        Vector2 leftEdge = health.anchoredPosition + new Vector2(-health.pivot.x * health.sizeDelta.x, 0f);
        Vector2 position = leftEdge + new Vector2(bar.size.x * health.pivot.x,
                                                  health.sizeDelta.y * (1f - health.pivot.y) + gap + bar.size.y * health.pivot.y);
        bar.Build(health.parent, health.GetSiblingIndex() + 1, health.anchorMin, health.anchorMax, health.pivot, position);
        return bar;
    }

    // Barra de vida de un jefe: ancha, arriba al centro de la pantalla (abajo a la izquierda están
    // las del jugador), con el nombre arriba y sin números. Va en un Canvas propio (la raíz de la
    // barra), que se borra con la barra. Si se crea en 0 y después se llena, el color sube de a
    // poco (la parte que "va a subir" es invisible), como cuando aparece un jefe.
    public static StatBarUI CreateBossBar(string name, TMP_FontAsset font)
    {
        GameObject canvasObject = new GameObject("BossBarCanvas", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        StatBarUI bar = CreateObject("BossBar");
        bar.size = new Vector2(1100f, 22f);
        bar.riseSpeed = 0.8f;          // se llena en algo más de un segundo
        bar.riseColor = Color.clear;
        bar.hitThreshold = 0.01f;
        bar.shakeStrength = 3f;
        bar.lowThreshold = 0f; // la del jefe no late
        Vector2 topCenter = new Vector2(0.5f, 1f);
        bar.Build(canvasObject.transform, 0, topCenter, topCenter, topCenter, new Vector2(0f, -80f));

        // Nombre arriba de la barra, alineado a la izquierda: mayúsculas espaciadas con contorno
        // negro, para que se lea sobre cualquier fondo
        GameObject titleObject = new GameObject("BossName", typeof(RectTransform));
        RectTransform titleRect = (RectTransform)titleObject.transform;
        titleRect.SetParent(bar.rect, false);
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0f, 0f);
        titleRect.anchoredPosition = new Vector2(0f, 6f);
        titleRect.sizeDelta = new Vector2(0f, 50f);
        TextMeshProUGUI title = titleObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            title.font = font;
        title.fontSize = 38f;
        title.fontStyle = FontStyles.UpperCase;
        title.characterSpacing = 6f;
        title.color = new Color(0.95f, 0.9f, 0.8f);
        title.outlineWidth = 0.18f;
        title.outlineColor = new Color32(0, 0, 0, 255);
        title.alignment = TextAlignmentOptions.BottomLeft;
        title.raycastTarget = false;
        title.text = name;
        return bar;
    }

    private static StatBarUI CreateObject(string name)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image));
        return root.AddComponent<StatBarUI>();
    }

    private void Build(Transform parent, int siblingIndex, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position)
    {
        rect = (RectTransform)transform;
        rect.SetParent(parent, false);
        rect.SetSiblingIndex(siblingIndex);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        basePosition = position;
        Image frame = GetComponent<Image>();
        frame.color = frameColor;
        frame.raycastTarget = false;

        // Interior: fondo y, encima, de atrás para adelante: lo que sube, la parte marcada y el valor
        RectTransform inner = CreateBar("Inner", rect, backgroundColor);
        inner.offsetMin = new Vector2(border, border);
        inner.offsetMax = new Vector2(-border, -border);

        rise = CreateBar("Rise", inner, riseColor);
        chip = CreateBar("Chip", inner, chipColor);
        fill = CreateBar("Fill", inner, fillColor);
        fillImage = fill.GetComponent<Image>();

        // Brillo en la mitad de arriba, para que no se vea tan plana
        RectTransform shine = CreateBar("Shine", inner, new Color(1f, 1f, 1f, 0.08f));
        shine.anchorMin = new Vector2(0f, 0.5f);
    }

    // El texto pasa a ir centrado adentro de la barra
    private void SetLabel(TextMeshProUGUI text, TMP_FontAsset font)
    {
        label = text;
        if (font != null)
            label.font = font;
        RectTransform labelRect = label.rectTransform;
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        label.enableAutoSizing = false;
        label.fontSize = labelFontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
    }

    private static RectTransform CreateBar(string name, RectTransform parent, Color color)
    {
        GameObject bar = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform barRect = (RectTransform)bar.transform;
        barRect.SetParent(parent, false);
        barRect.anchorMin = Vector2.zero;
        barRect.anchorMax = Vector2.one;
        barRect.offsetMin = Vector2.zero;
        barRect.offsetMax = Vector2.zero;

        Image image = bar.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return barRect;
    }

    // La llama PlayerStats cada vez que cambia el valor
    public void SetValue(float current, float max)
    {
        float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        if (label != null)
            label.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";

        if (!initialized)
        {
            initialized = true;
            shown = chipValue = fraction;
        }
        else if (fraction < target)
        {
            // Bajó: el color baja de golpe y la parte perdida queda marcada un rato
            chipValue = Mathf.Max(chipValue, shown);
            if (target - fraction >= hitThreshold)
            {
                flashTimer = flashDuration;
                shakeTimer = shakeDuration;
            }
            shown = fraction;
            chipTimer = chipDelay;
        }
        target = fraction;
        if (riseSpeed <= 0f)
            shown = Mathf.Max(shown, target);
        ApplyWidths();
    }

    private void Update()
    {
        if (!initialized) return;
        float dt = Time.deltaTime;

        // Subida: el color sube de a poco hasta el valor real
        if (shown < target)
            shown = Mathf.MoveTowards(shown, target, riseSpeed * dt);

        // La parte marcada espera y después baja: rápido al principio, lento al final
        if (chipTimer > 0f)
            chipTimer -= dt;
        else if (chipValue > shown)
            chipValue = Mathf.MoveTowards(chipValue, shown, Mathf.Max(0.05f, (chipValue - shown) * chipSpeed) * dt);
        chipValue = Mathf.Max(chipValue, shown);

        ApplyWidths();

        // Destello al bajar de golpe y latido con poco valor
        Color color = fillColor;
        if (target <= lowThreshold && target > 0f)
            color = Color.Lerp(fillColor, lowColor, (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f);
        if (flashTimer > 0f)
        {
            flashTimer -= dt;
            color = Color.Lerp(color, Color.white, Mathf.Clamp01(flashTimer / flashDuration));
        }
        fillImage.color = color;

        // Sacudón que se va apagando
        if (shakeTimer > 0f)
        {
            shakeTimer -= dt;
            float strength = shakeStrength * Mathf.Clamp01(shakeTimer / shakeDuration);
            rect.anchoredPosition = basePosition + Random.insideUnitCircle * strength;
        }
        else
        {
            rect.anchoredPosition = basePosition;
        }
    }

    private void ApplyWidths()
    {
        fill.anchorMax = new Vector2(shown, 1f);
        chip.anchorMax = new Vector2(chipValue, 1f);
        rise.anchorMax = new Vector2(target, 1f);
    }
}
