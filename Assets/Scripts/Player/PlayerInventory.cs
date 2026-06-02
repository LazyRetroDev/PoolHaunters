using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInventory : MonoBehaviour
{
    public int inventorySize = 4;
    public float pickupRange = 2.5f;
    public Transform holdPoint;

    private Item[] slots;
    private int selectedSlot = 0;
    private PlayerStatus playerStatus;

    void Start()
    {
        slots = new Item[inventorySize];
        playerStatus = GetComponent<PlayerStatus>();
    }

    public void OnInteract(InputValue value)
    {
        if (!value.isPressed) return;

        Ray ray = Camera.main.ScreenPointToRay(new Vector2(Screen.width / 2, Screen.height / 2));
        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange))
        {
            Debug.Log("Hit: " + hit.collider.gameObject.name);
            Item item = hit.collider.GetComponent<Item>();
            if (item != null) TryPickup(item);
        }
        else
        {
            Debug.Log("Raycast hit nothing");
        }
    }

    public void OnUse(InputValue value)
    {
        if (!value.isPressed) return;
        UseSelectedItem();
    }

    public void OnPrevious(InputValue value) => SelectSlot(0);
    public void OnNext(InputValue value) => SelectSlot(1);

    void Update()
    {
        // Number keys 1-4
        for (int i = 0; i < inventorySize; i++)
        {
            if (Keyboard.current[Key.Digit1 + i].wasPressedThisFrame)
                SelectSlot(i);
        }
    }

    void TryPickup(Item item)
    {
        WaterItem waterItem = item.GetComponent<WaterItem>();
        if (waterItem != null && waterItem.useImmediatelyOnPickup && waterItem.TryApply(playerStatus))
        {
            Debug.Log($"Used {item.itemName} on pickup. Water: {playerStatus.GetCurrentWater():0}/{playerStatus.maxWater:0} ({playerStatus.GetWaterQuality()})");

            if (waterItem.destroyAfterUse)
                Destroy(item.gameObject);
            else
                item.gameObject.SetActive(false);

            return;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
            {
                slots[i] = item;
                item.gameObject.SetActive(false);
                Debug.Log($"Picked up {item.itemName} into slot {i + 1}");
                return;
            }
        }
        Debug.Log("Inventory full!");
    }

    void UseSelectedItem()
    {
        if (slots == null || selectedSlot < 0 || selectedSlot >= slots.Length) return;

        Item item = slots[selectedSlot];
        if (item == null)
        {
            Debug.Log("No item selected.");
            return;
        }

        UsableItem usableItem = item.GetComponent<UsableItem>();
        if (usableItem == null)
        {
            Debug.Log($"{item.itemName} cannot be used yet.");
            return;
        }

        bool used = usableItem.Use(this, playerStatus);
        if (!used)
        {
            Debug.Log($"{item.itemName} had no effect.");
            return;
        }

        Debug.Log($"Used {item.itemName}.");
        if (usableItem.consumeOnUse)
        {
            slots[selectedSlot] = null;
            Destroy(item.gameObject);
        }
    }

    void SelectSlot(int index)
    {
        selectedSlot = Mathf.Clamp(index, 0, inventorySize - 1);
        Debug.Log($"Selected slot {selectedSlot + 1}");
    }

    public Item[] GetSlots() => slots;
    public int GetSelectedSlot() => selectedSlot;
}
