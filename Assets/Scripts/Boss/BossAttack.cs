using System.Collections;
using UnityEngine;

// Base de los ataques del jefe. Cada ataque es un componente en el objeto "Boss", con sus propios
// números en el Inspector. El BossController elige uno de su fase al azar (según el peso y sin
// repetir el anterior si hay otro disponible), lo corre entero y espera un rato antes del siguiente.
public abstract class BossAttack : MonoBehaviour
{
    [Tooltip("Probabilidad relativa de elegir este ataque entre los de su fase.")]
    [SerializeField] protected float weight = 1f;
    [Tooltip("Segundos mínimos antes de volver a usarlo.")]
    [SerializeField] protected float cooldown = 3f;

    // En qué fase lo usa el jefe (1: el esqueleto, 2: Astraía)
    public abstract int Phase { get; }
    public float Weight => weight;
    public float Cooldown => cooldown;
    public float ReadyTime { get; set; }

    // El ataque entero, de la carga al final. Lo corre el BossController con StartCoroutine.
    public abstract IEnumerator Run(BossController boss);

    // Se llama si el ataque se corta a la mitad (cambio de fase o muerte del jefe): tiene que
    // soltar todo lo que estaba moviendo. Lo que creó en la escena lo borra el BossController.
    public virtual void Cancel(BossController boss) { }
}
