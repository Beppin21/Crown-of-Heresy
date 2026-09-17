using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTeleportTown : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            SceneManager.LoadScene("Town");
        }
    }
}