using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTeleportDungeon : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            SceneManager.LoadScene("Dungeon");
        }
    }
}