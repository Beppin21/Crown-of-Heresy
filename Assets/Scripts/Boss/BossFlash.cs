using UnityEngine;

// Apaga de a poco la luz de un destello y lo borra
public class BossFlash : MonoBehaviour
{
    private Light flashLight;
    private float startIntensity;
    private float duration;
    private float elapsed;

    public void Init(float intensity, float seconds)
    {
        flashLight = GetComponent<Light>();
        startIntensity = intensity;
        duration = Mathf.Max(0.01f, seconds);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float k = Mathf.Clamp01(elapsed / duration);
        if (flashLight != null) flashLight.intensity = startIntensity * (1f - k) * (1f - k);
        if (k >= 1f) Destroy(gameObject);
    }
}
