using UnityEngine;


[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class RayTraceable : MonoBehaviour
{
    // public Vector3 albedo, specular, emission;
    public Color albedo, specular;
    public Vector4 emission;
    [Range(0.1f, 1)]
    public float roughness;
    public float metallic;

    [Range(0, 2)]
    public int type = 0;

    private void OnEnable()
    {
        RayTracingController.RegisterObject(this);
    }
    private void OnDisable()
    {
        RayTracingController.UnregisterObject(this);
    }

    public TriMeshMaterial GetMeshMaterial() {
        // convert Color to vec3
        Vector4 albVec4 = albedo, specVec4 = specular, emisVec4 = emission;

        return new TriMeshMaterial() {
            albedo = albVec4,
            specular = specVec4,
            emission = emisVec4,
            type = type,
            roughness = roughness,
            metallic = metallic,
            matid = -1
        };
    }
}
