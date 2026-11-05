using UnityEngine;

// Opcional, en el jugador: guarda al entrar a la escena (además de las hogueras). Ubicar al jugador al
// cargar una partida ya no depende de este componente: lo hace SaveSystem apenas carga la escena.
public class PlayerSave : MonoBehaviour
{
    [Tooltip("Si está tildado, guarda también al entrar a cada escena (además de las hogueras)")]
    public bool autoguardarAlEntrar = false;

    void Start()
    {
        if (autoguardarAlEntrar && !SaveSystem.CargandoPartida) GuardarPartida();
    }

    public void GuardarPartida()
    {
        SaveSystem.GuardarPartida(transform, null);
    }
}
