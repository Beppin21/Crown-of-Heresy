using UnityEngine;

// Tipo de arma (espada, hacha, daga...): define QUÉ animación de ataque se ejecuta y cómo
// el peso de cada arma concreta de este tipo afecta la velocidad de esa animación.
// Las armas particulares (WeaponSO) apuntan a uno de estos assets.
[CreateAssetMenu(fileName = "WeaponType", menuName = "Scriptable Objects/Weapon Type")]
public class WeaponTypeSO : ScriptableObject
{
    [SerializeField] private string typeName;

    [Header("Animación")]
    [Tooltip("Animación de ataque propia de este tipo de arma.")]
    [SerializeField] private AnimationClip attackClip;

    [Tooltip("Momento (0..1 del clip) en que se prende el hitbox.")]
    [SerializeField] [Range(0f, 1f)] private float hitboxStart = 0.4f;
    [Tooltip("Momento (0..1 del clip) en que se apaga el hitbox.")]
    [SerializeField] [Range(0f, 1f)] private float hitboxEnd = 0.65f;

    [Header("Peso → Velocidad")]
    [Tooltip("Con este peso (o menos) la animación va a la velocidad máxima.")]
    [SerializeField] private float lightWeight = 1f;
    [Tooltip("Con este peso (o más) la animación va a la velocidad mínima.")]
    [SerializeField] private float heavyWeight = 10f;
    [SerializeField] private float maxSpeedMultiplier = 1.5f;
    [SerializeField] private float minSpeedMultiplier = 0.6f;

    [Header("Peso → Recuperación")]
    [Tooltip("Segundos de recuperación después del ataque con un arma de peso lightWeight (o menos).")]
    [SerializeField] private float lightRecoveryTime = 0.05f;
    [Tooltip("Segundos de recuperación después del ataque con un arma de peso heavyWeight (o más).")]
    [SerializeField] private float heavyRecoveryTime = 0.6f;

    public string TypeName => typeName;
    public AnimationClip AttackClip => attackClip;
    public float HitboxStart => hitboxStart;
    public float HitboxEnd => Mathf.Max(hitboxStart, hitboxEnd);

    // Cuanto más pesada el arma, más lenta la animación (interpolación lineal entre los dos pesos)
    public float GetSpeedMultiplier(float weight)
    {
        float t = Mathf.InverseLerp(lightWeight, heavyWeight, weight);
        return Mathf.Lerp(maxSpeedMultiplier, minSpeedMultiplier, t);
    }

    // Cuanto más pesada el arma, más tarda en recuperarse después del golpe
    public float GetRecoveryTime(float weight)
    {
        float t = Mathf.InverseLerp(lightWeight, heavyWeight, weight);
        return Mathf.Lerp(lightRecoveryTime, heavyRecoveryTime, t);
    }
}
