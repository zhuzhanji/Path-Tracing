using UnityEngine;

public class RayTraceableSphere : MonoBehaviour
{
    // Start is called before the first frame update
    public Color albedo, specular, emission;
    [Range(0.1f, 1)]
    public float roughness;
    public float metallic;
    [Range(0, 2)]
    public int type = 0;

    public Sphere ToSphere() {
        return new Sphere() {
            center = transform.position,
            radius = transform.localScale.x * transform.parent.localScale.x  / 2f,
            albedo = (Vector3)(Vector4)albedo,
            specular = (Vector3)(Vector4)specular,
            emission = (Vector3)(Vector4)emission,
            roughness = (float)roughness,
            metallic = (float)metallic,
            type = (int)type,
            matid = -1

        };
    }
}
