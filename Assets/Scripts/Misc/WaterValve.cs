using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class WaterValve : NetworkBehaviour
{
    class VisualTurnState
    {
        public Transform transform;
        public Vector3 initialLocalPosition;
        public Quaternion initialLocalRotation;
    }

    [Header("State")]
    [SerializeField] private bool activated;

    [Header("Visuals")]
    [SerializeField] private Transform rotatingHandle;
    [SerializeField] private Vector3 activatedLocalEulerAngles = new Vector3(0f, 0f, 90f);
    [SerializeField, Min(0f)] private float activationTurnDuration = 0.5f;

    private readonly NetworkVariable<bool> networkActivated =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private Quaternion initialHandleLocalRotation;
    private Coroutine turnRoutine;
    private Transform backMarker;
    private Transform frontMarker;
    private readonly List<VisualTurnState> visualTurnStates = new List<VisualTurnState>();

    public bool Activated
    {
        get { return IsSpawned ? networkActivated.Value : activated; }
    }

    private void Awake()
    {
        backMarker = FindChildRecursive(transform, "Back");
        frontMarker = FindChildRecursive(transform, "Front");

        if (rotatingHandle == null)
            rotatingHandle = transform.Find("JointPoint");

        if (HasUsableRotatingHandle())
            initialHandleLocalRotation = rotatingHandle.localRotation;
        else
            CacheMarkerDrivenVisuals();
    }

    private void Start()
    {
        if (!IsSpawned)
            ApplyActivatedState(activated, notifyObjective: activated, animate: false);
    }

    public override void OnNetworkSpawn()
    {
        networkActivated.OnValueChanged += HandleNetworkActivatedChanged;

        if (IsServer && activated && !networkActivated.Value)
            networkActivated.Value = true;

        ApplyActivatedState(
            networkActivated.Value,
            notifyObjective: networkActivated.Value,
            animate: false);
    }

    public override void OnNetworkDespawn()
    {
        networkActivated.OnValueChanged -= HandleNetworkActivatedChanged;
    }

    public void Interact(PlayerInventory interactor)
    {
        if (Activated)
            return;

        if (IsSpawned && !IsServer)
        {
            ActivateServerRpc();
            return;
        }

        ActivateAuthoritative();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ActivateServerRpc()
    {
        ActivateAuthoritative();
    }

    private void ActivateAuthoritative()
    {
        if (Activated)
            return;

        if (IsSpawned)
            networkActivated.Value = true;

        ApplyActivatedState(true, notifyObjective: true, animate: true);
    }

    private void HandleNetworkActivatedChanged(bool previousValue, bool newValue)
    {
        ApplyActivatedState(newValue, notifyObjective: newValue, animate: newValue);
    }

    private void ApplyActivatedState(bool isActivated, bool notifyObjective, bool animate)
    {
        activated = isActivated;

        if (isActivated && CanApplyVisualTurn())
        {
            if (HasUsableRotatingHandle())
            {
                Quaternion targetRotation =
                    initialHandleLocalRotation * Quaternion.Euler(activatedLocalEulerAngles);

                if (animate && activationTurnDuration > 0f && gameObject.activeInHierarchy)
                    StartTurnAnimation(targetRotation);
                else
                    SetHandleRotationInstantly(targetRotation);
            }
            else
            {
                if (animate && activationTurnDuration > 0f && gameObject.activeInHierarchy)
                    StartMarkerDrivenTurnAnimation();
                else
                    SetMarkerDrivenTurnInstantly(1f);
            }
        }

        if (notifyObjective && LevelObjectiveManager.Instance != null)
            LevelObjectiveManager.Instance.ActivateWaterValve();
    }

    private bool CanApplyVisualTurn()
    {
        return HasUsableRotatingHandle() ||
            (backMarker != null && frontMarker != null && visualTurnStates.Count > 0);
    }

    private void StartTurnAnimation(Quaternion targetRotation)
    {
        if (turnRoutine != null)
            StopCoroutine(turnRoutine);

        turnRoutine = StartCoroutine(TurnHandleRoutine(targetRotation));
    }

    private void SetHandleRotationInstantly(Quaternion targetRotation)
    {
        if (turnRoutine != null)
        {
            StopCoroutine(turnRoutine);
            turnRoutine = null;
        }

        rotatingHandle.localRotation = targetRotation;
    }

    private IEnumerator TurnHandleRoutine(Quaternion targetRotation)
    {
        Quaternion startRotation = rotatingHandle.localRotation;
        float elapsed = 0f;

        while (elapsed < activationTurnDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / activationTurnDuration);
            rotatingHandle.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);
            yield return null;
        }

        rotatingHandle.localRotation = targetRotation;
        turnRoutine = null;
    }

    private bool HasUsableRotatingHandle()
    {
        if (rotatingHandle == null)
            return false;

        return rotatingHandle != backMarker && rotatingHandle != frontMarker;
    }

    private void CacheMarkerDrivenVisuals()
    {
        visualTurnStates.Clear();

        if (backMarker == null || frontMarker == null)
            return;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == backMarker || child == frontMarker)
                continue;

            if (child.GetComponentInChildren<Renderer>(true) == null)
                continue;

            visualTurnStates.Add(new VisualTurnState
            {
                transform = child,
                initialLocalPosition = child.localPosition,
                initialLocalRotation = child.localRotation
            });
        }
    }

    private void StartMarkerDrivenTurnAnimation()
    {
        if (turnRoutine != null)
            StopCoroutine(turnRoutine);

        turnRoutine = StartCoroutine(MarkerDrivenTurnRoutine());
    }

    private void SetMarkerDrivenTurnInstantly(float normalizedTurn)
    {
        if (turnRoutine != null)
        {
            StopCoroutine(turnRoutine);
            turnRoutine = null;
        }

        ApplyMarkerDrivenTurn(normalizedTurn);
    }

    private IEnumerator MarkerDrivenTurnRoutine()
    {
        float elapsed = 0f;

        while (elapsed < activationTurnDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / activationTurnDuration);
            ApplyMarkerDrivenTurn(t);
            yield return null;
        }

        ApplyMarkerDrivenTurn(1f);
        turnRoutine = null;
    }

    private void ApplyMarkerDrivenTurn(float normalizedTurn)
    {
        if (backMarker == null || frontMarker == null || visualTurnStates.Count == 0)
            return;

        Vector3 localPivot = backMarker.localPosition;
        Vector3 localAxis = frontMarker.localPosition - backMarker.localPosition;
        if (localAxis.sqrMagnitude <= 0.0001f)
            return;

        localAxis.Normalize();
        Quaternion delta = Quaternion.AngleAxis(90f * normalizedTurn, localAxis);

        for (int i = 0; i < visualTurnStates.Count; i++)
        {
            VisualTurnState state = visualTurnStates[i];
            if (state.transform == null)
                continue;

            state.transform.localPosition =
                localPivot + delta * (state.initialLocalPosition - localPivot);
            state.transform.localRotation = delta * state.initialLocalRotation;
        }
    }

    private Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Transform result = FindChildRecursive(child, childName);
            if (result != null)
                return result;
        }

        return null;
    }
}
