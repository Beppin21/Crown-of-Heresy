using UnityEngine;

// Arma particular: pertenece a un tipo (que define la animación) y tiene su propio daño y peso.
// El peso decide qué tan rápido se reproduce la animación del tipo.
[CreateAssetMenu(fileName = "Weapon", menuName = "Scriptable Objects/Weapon")]
public class WeaponSO : ScriptableObject
{
    [SerializeField] private string weaponName;
    [SerializeField] private WeaponTypeSO weaponType;

    [Header("Estadísticas")]
    [SerializeField] private float damage = 25f;
    [SerializeField] private float weight = 4f;

    [Header("Modelo (opcional)")]
    [Tooltip("Se instancia en el socket del arma. Si tiene un WeaponHitbox, se usa ese hitbox.")]
    [SerializeField] private GameObject modelPrefab;

    public string WeaponName => weaponName;
    public WeaponTypeSO WeaponType => weaponType;
    public float Damage => damage;
    public float Weight => weight;
    public GameObject ModelPrefab => modelPrefab;

    // Multiplicador de velocidad de la animación según el peso de esta arma
    public float AttackSpeed => weaponType != null ? weaponType.GetSpeedMultiplier(weight) : 1f;

    // Segundos de recuperación después del ataque, según el peso de esta arma
    public float RecoveryTime => weaponType != null ? weaponType.GetRecoveryTime(weight) : 0f;

    // Duración real del ataque (en segundos) ya aplicada la velocidad
    public float AttackDuration => weaponType != null && weaponType.AttackClip != null
        ? weaponType.AttackClip.length / AttackSpeed
        : 0f;
}
