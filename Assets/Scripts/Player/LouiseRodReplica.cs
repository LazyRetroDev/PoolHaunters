using UnityEngine;

// Presentation only: remote players never execute the owner's cleaning or water usage.
public class LouiseRodReplica : MonoBehaviour
{
    Transform rod, lure;
    LineRenderer line;
    Material material;
    Vector3 rodLocal, tipLocal, lureWorld;
    Quaternion rodRotation;
    float lastMessage;
    bool visible;

    public void SetPose(bool show, Vector3 position, Quaternion rotation, Vector3 tip, Vector3 endpoint)
    {
        if (show && rod == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return;
            material = new Material(shader) { color = new Color(0.2f, 0.9f, 0.8f) };
            rod = CreatePart(PrimitiveType.Cube, new Vector3(0.035f, 0.85f, 0.035f));
            lure = CreatePart(PrimitiveType.Sphere, Vector3.one * 0.14f);
            line = new GameObject("Remote Louise Line").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.sharedMaterial = material;
            line.positionCount = 2;
            line.startWidth = line.endWidth = 0.015f;
            line.useWorldSpace = true;
        }
        rodLocal = transform.InverseTransformPoint(position);
        tipLocal = transform.InverseTransformPoint(tip);
        rodRotation = Quaternion.Inverse(transform.rotation) * rotation;
        lureWorld = endpoint;
        if (show && !visible && lure != null) lure.position = endpoint;
        visible = show;
        lastMessage = Time.unscaledTime;
    }

    Transform CreatePart(PrimitiveType type, Vector3 scale)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = "Remote Louise " + type;
        part.transform.SetParent(transform, false);
        part.transform.localScale = scale;
        part.GetComponent<Collider>().enabled = false;
        Destroy(part.GetComponent<Collider>());
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part.transform;
    }

    void LateUpdate()
    {
        if (rod == null) return;
        bool show = visible && Time.unscaledTime - lastMessage < 2f;
        rod.gameObject.SetActive(show);
        lure.gameObject.SetActive(show);
        line.enabled = show;
        if (!show) return;
        rod.localPosition = rodLocal;
        rod.localRotation = rodRotation;
        lure.position = Vector3.Lerp(lure.position, lureWorld, 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime));
        line.SetPosition(0, transform.TransformPoint(tipLocal));
        line.SetPosition(1, lure.position);
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (rod != null) Destroy(rod.gameObject);
        if (lure != null) Destroy(lure.gameObject);
        if (line != null) Destroy(line.gameObject);
    }
}
