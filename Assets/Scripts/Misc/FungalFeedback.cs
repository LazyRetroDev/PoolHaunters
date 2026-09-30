using TMPro;
using UnityEngine;

// Detached, short-lived presentation survives the network mushroom despawning.
public sealed class FungalFeedback : MonoBehaviour
{
    Material material;
    TMP_Text label;

    public static FungalFeedback Show(Vector3 position, Color color, float radius, float duration, string message = null)
    {
        var root = new GameObject("Fungal Feedback");
        root.transform.position = position + Vector3.up * 0.25f;
        var feedback = root.AddComponent<FungalFeedback>();
        var particles = root.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = Mathf.Max(0.1f, duration);
        main.startLifetime = Mathf.Max(0.1f, duration);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.2f, radius * 0.4f);
        main.startColor = color;
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius * 0.8f;
        var emission = particles.emission;
        emission.enabled = false;
        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        Shader shader = Resources.Load<Shader>("PoolHauntersWater");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (shader != null)
        {
            feedback.material = new Material(shader);
            renderer.sharedMaterial = feedback.material;
        }
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.SetActive(false);
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = sphere.GetComponent<MeshFilter>().sharedMesh;
        Destroy(sphere);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Play();
        particles.Emit(duration > 1f ? 60 : 20);
        if (!string.IsNullOrEmpty(message))
        {
            var text = new GameObject("Fungal Result").AddComponent<TextMeshPro>();
            text.transform.SetParent(root.transform, false);
            text.transform.localPosition = Vector3.up;
            text.fontSize = 3f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.yellow;
            text.text = message;
            text.rectTransform.sizeDelta = new Vector2(6f, 1f);
            feedback.label = text;
        }
        Destroy(root, Mathf.Max(0.1f, duration));
        return feedback;
    }

    void LateUpdate()
    {
        if (label != null && Camera.main != null)
            label.transform.rotation = Camera.main.transform.rotation;
    }

    void OnDestroy() { if (material != null) Destroy(material); }
}
