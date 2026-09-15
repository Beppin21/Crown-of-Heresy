using UnityEngine;

[RequireComponent(typeof(Collider))]
public class WeaponHitbox : MonoBehaviour
{
    // DELEGATE + EVENT 
    public delegate void HitDetected(Collider other);
    public event HitDetected OnHit;

    private Collider hitboxCollider;

    private void Awake()
    {
        hitboxCollider = GetComponent<Collider>();
        hitboxCollider.isTrigger = true; // por las dudas, nos aseguramos en código de que sea un trigger
        hitboxCollider.enabled = false;  // arranca apagado: PlayerCombat lo prende solo durante el golpe
    }

    private void OnTriggerEnter(Collider other)
    {
        OnHit?.Invoke(other); // "?." = invocá el evento solo si hay alguien suscripto (evita un NullReferenceException)
    }

    // Lo llama PlayerCombat para "prender" el hitbox justo cuando el arma empieza a conectar,
    // y para apagarlo apenas termina esa ventana. Queda encapsulado acá adentro: a PlayerCombat
    // no le interesa CÓMO se prende un Collider, solo que este objeto sepa hacerlo.
    public void SetHitboxEnabled(bool active)
    {
        hitboxCollider.enabled = active;
    }
}
