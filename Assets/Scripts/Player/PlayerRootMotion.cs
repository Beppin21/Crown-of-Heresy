using UnityEngine;

// Va en el modelo del personaje (el mismo GameObject que tiene el Animator).
//
// Las animaciones de caminar/correr traen su propio avance ("root motion"): cuánto se mueve el
// cuerpo en cada paso. Si el código moviera al personaje a una velocidad inventada, los pies
// "patinarían" sobre el piso. En cambio, Unity llama a OnAnimatorMove cada vez que el Animator
// calcula ese avance, y acá se lo pasamos al PlayerController para que mueva el Rigidbody
// exactamente lo que avanza la animación.
//
// Tener este método hace que Unity NO mueva el modelo por su cuenta: el que se mueve es el
// Rigidbody del jugador (y el modelo, como es hijo, lo acompaña).
[RequireComponent(typeof(Animator))]
public class PlayerRootMotion : MonoBehaviour
{
    private Animator animator;
    private PlayerController controller;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponentInParent<PlayerController>();
    }

    private void OnAnimatorMove()
    {
        if (controller != null)
            controller.ApplyRootMotion(animator.deltaPosition);
    }
}
