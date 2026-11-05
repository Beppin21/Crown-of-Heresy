using UnityEngine;

// Recibe los golpes del jugador y se los pasa al jefe. PlayerCombat busca un IDamageable en el
// collider que toca o en sus padres, así que los colliders del cuerpo del jefe van como hijos de
// este objeto. El BossController lo lleva cada frame a donde está el cuerpo de la fase activa:
// así el lock-on apunta al cuerpo y no a los pies del objeto "Boss".
public class BossHurtbox : MonoBehaviour, IDamageable
{
    [SerializeField] private BossController boss;

    public void SetBoss(BossController owner) => boss = owner;

    public void TakeDamage(float amount)
    {
        if (boss != null) boss.ReceiveDamage(amount);
    }
}
