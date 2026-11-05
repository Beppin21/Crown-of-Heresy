using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerSave : MonoBehaviour
{
    [Tooltip("Si está tildado, guarda también al entrar a cada escena (además de las fogatas)")]
    public bool autoguardarAlEntrar = false;

    void Start()
    {
        if (SaveSystem.CargandoPartida)
        {
            SaveSystem.CargandoPartida = false;
            SaveData datos = SaveSystem.Cargar();
            if (datos != null) Ubicar(datos);
        }
        else if (autoguardarAlEntrar)
        {
            GuardarPartida();
        }
    }

    public void GuardarPartida()
    {
        Vector3 p = transform.position;
        SaveData datos = new SaveData
        {
            escena = SceneManager.GetActiveScene().name,
            posicion = new float[] { p.x, p.y, p.z },
            rotacionY = transform.eulerAngles.y
        };
        SaveSystem.Guardar(datos);
        Debug.Log("Partida guardada en " + datos.escena);
    }

    void Ubicar(SaveData datos)
    {
        Vector3 pos = new Vector3(datos.posicion[0], datos.posicion[1], datos.posicion[2]);
        Quaternion rot = Quaternion.Euler(0f, datos.rotacionY, 0f);

        // Si hubiera CharacterController, se apaga un instante porque si no pisa la posición nueva
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.SetPositionAndRotation(pos, rot);

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = pos;
            rb.rotation = rot;
            rb.linearVelocity = Vector3.zero;
        }

        if (cc != null) cc.enabled = true;
    }
}
