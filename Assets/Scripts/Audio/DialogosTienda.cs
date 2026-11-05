using UnityEngine;
using UnityEngine.Audio;

// Voz de la mercader (bloque F del parcial de audio). Va en cualquier objeto de Town, por ejemplo la
// "Mercader". La tienda (ShopTrigger y ShopManager) la llama en cada momento:
//   - al abrir: la bienvenida la primera vez, después una de "Abrir la Tienda";
//   - al comprar, al no alcanzar la plata y al cerrar;
//   - si la tienda queda abierta sin que nadie hable un rato, un comentario ambiental.
// Lógica: de cada grupo elige un clip al azar sin repetir el anterior, y las líneas importantes
// (compra, plata, cerrar) cortan a las que no lo son (comentarios), pero no al revés.
[RequireComponent(typeof(AudioSource))]
public class DialogosTienda : MonoBehaviour
{
    public static DialogosTienda Instancia { get; private set; }

    [Header("Salida (el grupo Voz del mixer, cuando esté)")]
    [SerializeField] private AudioMixerGroup grupoVoz;

    [Header("Clips (Assets/Audio/Dialogos)")]
    [SerializeField] private AudioClip bienvenida;
    [SerializeField] private AudioClip[] abrir;
    [SerializeField] private AudioClip[] cerrar;
    [SerializeField] private AudioClip[] compra;
    [SerializeField] private AudioClip[] sinPlata;
    [SerializeField] private AudioClip[] comentarios;

    [Header("Comentarios ambientales")]
    [SerializeField] private float silencioMinimo = 12f; // segundos callada antes de comentar algo
    [SerializeField] private float silencioMaximo = 20f;

    private AudioSource fuente;
    private bool yaSaludo;          // la bienvenida suena una sola vez por partida
    private bool tiendaAbierta;
    private bool hablandoImportante;
    private float proximoComentario;
    private AudioClip ultimoClip;   // para no repetir el mismo clip dos veces seguidas

    private void Awake()
    {
        Instancia = this;
        fuente = GetComponent<AudioSource>();
        fuente.playOnAwake = false;
        fuente.spatialBlend = 0f; // 2D: es diálogo de la interfaz, se tiene que entender siempre
        if (grupoVoz != null) fuente.outputAudioMixerGroup = grupoVoz;
    }

    private void Update()
    {
        if (!fuente.isPlaying) hablandoImportante = false;

        // Comentario ambiental: solo con la tienda abierta y si hace rato que no habla
        if (tiendaAbierta && !fuente.isPlaying && Time.unscaledTime >= proximoComentario)
            Decir(comentarios, false);
    }

    // Llamadas desde la tienda
    public void AlAbrir()
    {
        tiendaAbierta = true;
        if (!yaSaludo && bienvenida != null)
        {
            yaSaludo = true;
            Sonar(bienvenida, true);
        }
        else
            Decir(abrir, true);
    }

    public void AlCerrar()
    {
        tiendaAbierta = false;
        Decir(cerrar, true);
    }

    public void AlComprar() => Decir(compra, true);
    public void AlNoAlcanzar() => Decir(sinPlata, true);

    private void Decir(AudioClip[] grupo, bool importante)
    {
        if (grupo == null || grupo.Length == 0) return;

        // Un comentario no interrumpe a una línea importante
        if (!importante && hablandoImportante) return;

        // Al azar, sin repetir el último (si hay más de uno para elegir)
        AudioClip clip = grupo[Random.Range(0, grupo.Length)];
        if (clip == ultimoClip && grupo.Length > 1)
            clip = grupo[(System.Array.IndexOf(grupo, clip) + Random.Range(1, grupo.Length)) % grupo.Length];

        Sonar(clip, importante);
    }

    private void Sonar(AudioClip clip, bool importante)
    {
        if (clip == null) return;
        fuente.Stop(); // corta lo que estuviera diciendo
        fuente.clip = clip;
        fuente.Play();
        ultimoClip = clip;
        hablandoImportante = importante;
        // El próximo comentario se cuenta desde que termina esta línea
        proximoComentario = Time.unscaledTime + clip.length + Random.Range(silencioMinimo, silencioMaximo);
    }

#if UNITY_EDITOR
    // Al agregar el componente (o con Reset en su menú) carga los clips de Assets/Audio/Dialogos por nombre
    private void Reset()
    {
        AudioClip[] saludo = Cargar("Bienvenida");
        bienvenida = saludo.Length > 0 ? saludo[0] : null;
        abrir = Cargar("Abrir la Tienda");
        cerrar = Cargar("Cerrar Tienda");
        compra = Cargar("Compra Exitosa");
        sinPlata = Cargar("Dinero Insuficiente");
        comentarios = Cargar("Comentario Ambiental");
    }

    private static AudioClip[] Cargar(string prefijo)
    {
        var clips = new System.Collections.Generic.List<AudioClip>();
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/Dialogos" }))
        {
            string ruta = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(ruta).StartsWith(prefijo))
                clips.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(ruta));
        }
        clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return clips.ToArray();
    }
#endif
}
