using UnityEngine;

// Va en el modelo del personaje (el mismo GameObject que tiene el Animator) y hace sonar los pasos.
//
// Antes los pasos salían de los "Animation Events" OnFootstep, pero de las animaciones del caballero
// solo los trae la carrera sin espada (Starter Assets): con la espada afuera (DoubleL) o caminando
// (Cautious Walk) no sonaba nada. Ahora (detectFootfalls) cada pie se sigue con los huesos del
// Avatar humanoide: suena cuando el pie baja hasta el piso después de haberse levantado, con
// cualquier animación. Solo mientras camina o corre y se desplaza de verdad (no en el lugar, ni al
// rodar o atacar).
//
// OnFootstep y OnLand siguen recibiendo los eventos de las animaciones (si no, Unity tira el error
// "AnimationEvent 'OnFootstep' has no receiver"); OnFootstep solo suena con detectFootfalls apagado.
public class PlayerFootsteps : MonoBehaviour
{
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private AudioClip landingClip;
    [Range(0f, 1f)] [SerializeField] private float volume = 0.3f;
    [Tooltip("Grupo del Audio Mixer (SFX). Lo asigna solo Scripts/Editor/AudioMixerSetup.cs.")]
    [SerializeField] private UnityEngine.Audio.AudioMixerGroup outputGroup;

    [Header("Pasos por los pies")]
    [Tooltip("Tildado: los pasos salen de los pies del Avatar (sirve con cualquier animación). Destildado: de los eventos OnFootstep de las animaciones.")]
    [SerializeField] private bool detectFootfalls = true;
    [Tooltip("Cuánto tiene que subir el pie (metros, para un personaje de 1,8 m) para contar como levantado.")]
    [SerializeField] private float liftHeight = 0.07f;
    [Tooltip("A cuánto del punto más bajo del pie (metros) cuenta como apoyado: ahí suena.")]
    [SerializeField] private float plantHeight = 0.03f;
    [Tooltip("Velocidad mínima (m/s) para que suenen: quieto o casi en el lugar no hay pasos.")]
    [SerializeField] private float minSpeed = 0.5f;

    // El punto más bajo de cada pie se va "olvidando" de a poco (escalones, rampas)
    private const float ForgetPerSecond = 0.05f;

    private Animator animator;
    private PlayerController controller;
    private Transform[] feet;
    private readonly float[] lowest = new float[2];
    private readonly bool[] lifted = new bool[2];
    private float bodyScale = 1f;
    private Vector3 lastPosition;

    private void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponentInParent<PlayerController>();
        if (animator != null && animator.isHuman)
        {
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (left != null && right != null) feet = new[] { left, right };
            if (head != null) bodyScale = Mathf.Max(0.3f, (head.position.y - transform.position.y) / 1.65f);
        }
        if (feet == null && detectFootfalls)
            Debug.LogWarning("[PlayerFootsteps] El modelo no es humanoide (o no tiene pies): los pasos salen de los eventos de las animaciones.", this);

        lastPosition = transform.position;
        for (int i = 0; feet != null && i < feet.Length; i++)
            lowest[i] = FootHeight(i);
    }

    private void LateUpdate()
    {
        if (!detectFootfalls || feet == null) return;

        Vector3 position = transform.position;
        Vector3 moved = position - lastPosition;
        moved.y = 0f;
        lastPosition = position;
        float speed = Time.deltaTime > 0f ? moved.magnitude / Time.deltaTime : 0f;
        // Atacando también: se puede caminar atacando (el golpe va en la capa UpperBody) y las
        // piernas dan pasos de verdad; quieto no suena porque no se mueve
        bool walking = controller == null ||
                       controller.CurrentState == PlayerState.Moving || controller.CurrentState == PlayerState.Sprinting ||
                       controller.CurrentState == PlayerState.Attacking;
        bool moving = walking && speed > minSpeed;

        for (int i = 0; i < feet.Length; i++)
        {
            float height = FootHeight(i);
            lowest[i] = Mathf.Min(lowest[i] + ForgetPerSecond * Time.deltaTime, height);
            if (!lifted[i])
            {
                if (height > lowest[i] + liftHeight * bodyScale) lifted[i] = true;
            }
            else if (height < lowest[i] + plantHeight * bodyScale)
            {
                lifted[i] = false;
                if (moving) PlayStep();
            }
        }
    }

    // Altura del pie sobre los pies del personaje (el origen del modelo)
    private float FootHeight(int index) => feet[index].position.y - transform.position.y;

    private void PlayStep()
    {
        if (footstepClips == null || footstepClips.Length == 0) return;
        AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];
        if (clip != null)
            PlayAt(clip);
    }

    // Como las animaciones se mezclan (blend tree), varias pueden disparar el mismo evento a la
    // vez: solo suena el de la animación que más pesa en la mezcla, para no duplicar pasos.
    private void OnFootstep(AnimationEvent animationEvent)
    {
        if (detectFootfalls && feet != null) return; // los pasos ya salen de los pies
        if (animationEvent.animatorClipInfo.weight <= 0.5f) return;
        PlayStep();
    }

    private void OnLand(AnimationEvent animationEvent)
    {
        if (animationEvent.animatorClipInfo.weight <= 0.5f) return;
        if (landingClip != null)
            PlayAt(landingClip);
    }

    // Como AudioSource.PlayClipAtPoint, pero saliendo por el grupo del mixer (PlayClipAtPoint no
    // deja elegir la salida): una fuente 3D temporal en el lugar del paso
    private void PlayAt(AudioClip clip)
    {
        GameObject sound = new GameObject("Paso");
        sound.transform.position = transform.position;
        AudioSource source = sound.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.spatialBlend = 1f;
        source.outputAudioMixerGroup = outputGroup;
        source.Play();
        Destroy(sound, clip.length + 0.1f);
    }
}
