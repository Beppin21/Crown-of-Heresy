using UnityEngine;

// Va en el modelo del personaje (el mismo GameObject que tiene el Animator).
//
// Las animaciones de caminar/correr de Starter Assets traen "Animation Events": en el cuadro
// exacto en que cada pie toca el piso llaman a OnFootstep, y al caer de un salto a OnLand.
// Unity busca esos métodos en los scripts del GameObject del Animator; si no hay ninguno,
// tira el error "AnimationEvent 'OnFootstep' has no receiver". Este script los recibe y
// reproduce el sonido del paso.
public class PlayerFootsteps : MonoBehaviour
{
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private AudioClip landingClip;
    [Range(0f, 1f)] [SerializeField] private float volume = 0.3f;

    // Como las animaciones se mezclan (blend tree), varias pueden disparar el mismo evento a la
    // vez: solo suena el de la animación que más pesa en la mezcla, para no duplicar pasos.
    private void OnFootstep(AnimationEvent animationEvent)
    {
        if (animationEvent.animatorClipInfo.weight <= 0.5f) return;
        if (footstepClips == null || footstepClips.Length == 0) return;

        AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];
        if (clip != null)
            AudioSource.PlayClipAtPoint(clip, transform.position, volume);
    }

    private void OnLand(AnimationEvent animationEvent)
    {
        if (animationEvent.animatorClipInfo.weight <= 0.5f) return;
        if (landingClip != null)
            AudioSource.PlayClipAtPoint(landingClip, transform.position, volume);
    }
}
