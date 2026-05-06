using UnityEngine;

public class PlayerStatus : MonoBehaviour
{
    public float maxWater = 100f;
    public float fillRate = 10f;

    private float currentWater = 0f;
    private bool inWater = false;

    private PlayerMovement movement;

    void Start()
    {
        movement = GetComponent<PlayerMovement>();
    }

    public void SetInWater(bool value) => inWater = value;
    public float GetWaterPercent() => currentWater / maxWater;
    public bool IsSprinting() => movement != null && movement.IsSprinting();

    void Update()
    {
        if (inWater && currentWater < maxWater)
        {
            currentWater += fillRate * Time.deltaTime;
            currentWater = Mathf.Clamp(currentWater, 0f, maxWater);
        }
    }
}