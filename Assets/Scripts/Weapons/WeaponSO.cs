using UnityEngine;

// Arma particular: pertenece a un tipo (que define la animación) y tiene su propio daño y peso.
// El peso decide qué tan rápido se reproduce la animación del tipo.
//
// Es un LootData: el nombre, la descripción y el precio son los del ítem, así que un arma se
// puede vender en la tienda, juntar como loot y guardar en el inventario.
//
// El modelo (opcional) se arma como un prefab en tamaño real, con la empuñadura en el origen,
// la hoja hacia +Y y la guarda a lo ancho en X: así lo cuelga PlayerWeaponSheath en la mano del
// caballero. Si trae un WeaponHitbox (con su Collider en modo trigger) se pega con ese; si no,
// PlayerCombat le arma uno con el tamaño del modelo.
[CreateAssetMenu(fileName = "Weapon", menuName = "Scriptable Objects/Weapon")]
public class WeaponSO : LootData
{
    [Header("Arma")]
    [SerializeField] private WeaponTypeSO weaponType;

    [Header("Estadísticas")]
    [SerializeField] private float damage = 25f;
    [SerializeField] private float weight = 4f;

    [Header("Modelo (opcional)")]
    [Tooltip("Se cuelga en la mano (y en la cintura al envainar). Si tiene un WeaponHitbox, se usa ese hitbox.")]
    [SerializeField] private GameObject modelPrefab;

    public string WeaponName => ItemName;
    public WeaponTypeSO WeaponType => weaponType;
    public float Damage => damage;
    public float Weight => weight;
    public GameObject ModelPrefab => modelPrefab;

    // Multiplicador de velocidad de la animación según el peso de esta arma
    public float AttackSpeed => weaponType != null ? weaponType.GetSpeedMultiplier(weight) : 1f;

    // Segundos de recuperación después del ataque, según el peso de esta arma
    public float RecoveryTime => weaponType != null ? weaponType.GetRecoveryTime(weight) : 0f;

    // Multiplicador de la estamina que gasta cada golpe, según el peso de esta arma
    public float StaminaMultiplier => weaponType != null ? weaponType.GetStaminaMultiplier(weight) : 1f;

    // Duración real del ataque (en segundos) ya aplicada la velocidad
    public float AttackDuration => weaponType != null && weaponType.AttackClip != null
        ? weaponType.AttackClip.length / AttackSpeed
        : 0f;
}
