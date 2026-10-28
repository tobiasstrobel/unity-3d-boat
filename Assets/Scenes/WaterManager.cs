using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class WaterManager : MonoBehaviour
{
    private Mesh mesh;
    private MeshRenderer meshRenderer;
    BoatForces boat;

    // Yacht-local footprint, wider than the hull so the slope up to a crest
    // stays outside the deck.
    const float HullHalfBeam = 4.5f;
    const float HullStern = -8f;
    const float HullBow = 9f;

    private void Awake()
    {
        mesh = GetComponent<MeshFilter>().mesh;
        meshRenderer = GetComponent<MeshRenderer>();
        // The distant plane shares the stock water material and must stay on it.
        var tint = Resources.Load<Material>("WaterHeight");
        if (tint != null && meshRenderer != null)
            meshRenderer.sharedMaterial = tint;
    }

    void Update()
    {
        if (boat == null) boat = FindObjectOfType<BoatForces>();
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = transform.TransformPoint(vertices[i].x, 0f, vertices[i].z);
            float height = WaweManager.GetWaweHeight(world.x) + HullWave.Height(world, boat);
            if (OverDeck(world))
                height = Mathf.Min(height, YachtsManager.DeckClearY);
            Vector3 local = transform.InverseTransformPoint(new Vector3(world.x, height, world.z));
            vertices[i].y = local.y;
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
    }

    bool OverDeck(Vector3 world)
    {
        if (boat == null) return false;
        Vector3 local = boat.transform.InverseTransformPoint(world);
        return Mathf.Abs(local.x) < HullHalfBeam && local.z > HullStern && local.z < HullBow;
    }
}
