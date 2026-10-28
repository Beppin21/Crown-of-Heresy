using System.Collections.Generic;
using UnityEngine;

public class WeaponManager : MonoBehaviour {
    [Header("Socket & References")]
    [SerializeField] private Transform weaponSocket;
    [SerializeField] private Animator animator; // Drag character's Animator here

    [Header("Weapon Inventory")]
    [SerializeField] private List<GameObject> weaponPrefabs = new List<GameObject>();

    private GameObject currentWeaponInstance;
    private int currentWeaponIndex = -1;

    private void Awake() {
        // Auto-get Animator if not assigned
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    private void Update() {
        // Weapon Switching Controls
        if (Input.GetKeyDown(KeyCode.Alpha1)) EquipWeapon(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) EquipWeapon(1);
        if (Input.GetKeyDown(KeyCode.Alpha0)) UnequipCurrentWeapon();

        // Attack Input (Left Mouse Button)
        if (Input.GetMouseButtonDown(0)) {
            PerformAttack();
        }
    }

    public void PerformAttack() {
        // Only attack if holding a weapon
        if (currentWeaponInstance == null) return;

        // Fire animator trigger
        if (animator != null) {
            animator.SetTrigger("Attack");
        }
    }

    public void EquipWeapon(int index) {
        if (index < 0 || index >= weaponPrefabs.Count) return;
        if (index == currentWeaponIndex && currentWeaponInstance != null) return;

        UnequipCurrentWeapon();

        GameObject newWeapon = Instantiate(weaponPrefabs[index]);
        newWeapon.transform.SetParent(weaponSocket, false);
        newWeapon.transform.localPosition = Vector3.zero;
        newWeapon.transform.localRotation = Quaternion.identity;
        newWeapon.transform.localScale = Vector3.one;

        currentWeaponInstance = newWeapon;
        currentWeaponIndex = index;
    }

    public void UnequipCurrentWeapon() {
        if (currentWeaponInstance != null) {
            Destroy(currentWeaponInstance);
            currentWeaponInstance = null;
            currentWeaponIndex = -1;
        }
    }
}