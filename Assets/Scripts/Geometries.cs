using UnityEngine;
using System.Collections.Generic;
public struct Sphere {
        public Vector3 center;
        public float radius;
        public Vector3 albedo, specular, emission;
        public float roughness;
        public float metallic;
    public int matid;
    public int type;


        public static Sphere CreateRandomSphere(  // TODO: move to separate file
            (float minR, float maxR) radiusRange,
            float posRange
        ) {
            Sphere s = new Sphere();
            var xz = Random.insideUnitCircle * posRange;
            s.radius = Random.Range(radiusRange.minR, radiusRange.maxR);
            s.center = new Vector3(xz.x, s.radius * 1.01f, xz.y);
            (s.albedo, s.specular) = Sphere.GenRandomMat();
            
            bool isEmissive = Random.value < 0.3;
            s.emission = isEmissive ? RayTracingController.GetRandColor() : Vector3.zero;
            if (isEmissive)
                 s.emission = Vector3.one * .8f;

            return s;
        }

        public static (Vector3 albedo, Vector3 specular) GenRandomMat() {
            // totally random, don't account for metal / diffuse
            // Color c1 = Random.ColorHSV();
            // Color c2 = Random.ColorHSV();
            // return (new Vector3(c1.r, c1.g, c1.b), new Vector3(c2.r, c2.g, c2.b));

            // Color color = Random.ColorHSV();
            var color = RandomColor();

            bool metal = Random.value < 0.5f;
        //bool metal = true;
        // bool metal = Random.value < 0.0f;
        // Vector3 albedo = metal ? Vector3.zero : new Vector3(color.r, color.g, color.b);
        
            Vector3 albedo = metal ? Vector3.zero : color;
        
            // Vector3 specular = metal ? new Vector3(color.r, color.g, color.b) : Vector3.one * 0.1f;
            Vector3 specular = metal ? color : Vector3.one * 0.1f;
            return (albedo, specular);
        }

        public static Vector3 RandomColor() {
            float m = .35f;
            return new Vector3(
                (Random.value * (1 - m)) + m,
                (Random.value * (1 - m)) + m,
                (Random.value * (1 - m)) + m
            );
        }

        public static bool intersect(Sphere s1, Sphere s2) {
            return ((s1.center - s2.center).magnitude < (s1.radius + s2.radius));
        }
    }


public class Triangle {
    public Vector3 v0, v1, v2;
    public int i0, i1, i2;  // indices into global indices array (for all mesh objects)
    public int m0, m1, m2;  // mesh indices
    public BBox bb;

    public Triangle(
        Vector3 v0, Vector3 v1, Vector3 v2,
        int i0, int i1, int i2,
        int m0, int m1, int m2
    ) {
        this.v0 = v0;
        this.v1 = v1;
        this.v2 = v2;
        this.i0 = i0;
        this.i1 = i1;
        this.i2 = i2;
        this.bb = BBox.BBoxFromTri(this);
        
        this.m0 = m0;
        this.m1 = m1;
        this.m2 = m2;
    }

    public int[] GetIndices() {
        return new int[] {i0, i1, i2};
    }

    public int[] GetMeshIndices() {
        return new int[] {m0, m1, m2};
    }
}

public class BBox {
    public Vector3 bot = float.MaxValue * Vector3.one, top = float.MinValue * Vector3.one;
    public static BBox EmptyBBox() {
        BBox bb = new BBox();
        bb.bot = float.MaxValue * Vector3.one;
        bb.top = float.MinValue * Vector3.one;
        return bb;
    }

    public static BBox BBoxFromTri(Triangle t) {
        return new BBox(
            Vector3.Min(Vector3.Min(t.v0, t.v1), t.v2),
            Vector3.Max(Vector3.Max(t.v0, t.v1), t.v2)
        );
    }

    public static BBox BBoxFromTris(List<Triangle> tris) {
        BBox bb = BBox.EmptyBBox();
        foreach (var t in tris)
            bb = bb.Union(BBox.BBoxFromTri(t));
        
        return bb;
    }

    public BBox() {
        bot = float.MaxValue * Vector3.one; 
        top = float.MinValue * Vector3.one;
    }

    public BBox(Vector3 bot, Vector3 top) {
        this.bot = bot;
        this.top = top;
    }

    public BBox Union(BBox bb) {
        return new BBox(
            Vector3.Min(this.bot, bb.bot),
            Vector3.Max(this.top, bb.top)
        );
    }

    public float SA() {
        Vector3 sl = this.top - this.bot;
        return (sl.x * sl.y) + (sl.x * sl.z) + (sl.y * sl.z);
    }

    public float getWidth(Axis axis) {
        switch (axis) {
            case Axis.X:
                return top.x - bot.x;
            case Axis.Y:
                return top.y - bot.y;
            case Axis.Z:
                return top.z - bot.z;
            default:
                return 0f;
        }
    }

    public Vector3 getCentroid() {
        return (top + bot) / 2;
    }

    public float getCenter(Axis axis) {
        Vector3 centroid = this.getCentroid();
        switch (axis) {
            case Axis.X:
                return centroid.x;
            case Axis.Y:
                return centroid.y;
            case Axis.Z:
                return centroid.z;
            default:
                return 0f;
        }
    }

    public float GetAxisStart(Axis axis) {
        switch (axis) {
            case Axis.X:
                return bot.x;
            case Axis.Y:
                return bot.y;
            case Axis.Z:
                return bot.z;
            default:
                return 0f;
        }
    }

    public void Draw(Color? c) {
        c ??= Color.white;
        Vector3 
            v0 = bot,
            v1 = new Vector3(top.x, bot.y, bot.z),
            v2 = new Vector3(top.x, top.y, bot.z),
            v3 = new Vector3(bot.x, top.y, bot.z),

            v4 = new Vector3(bot.x, bot.y, top.z),
            v5 = new Vector3(top.x, bot.y, top.z),
            v7 = new Vector3(bot.x, top.y, top.z),
            v6 = top;


        Debug.DrawLine(v0, v1, c.Value);
        Debug.DrawLine(v1, v2, c.Value);
        Debug.DrawLine(v2, v3, c.Value);
        Debug.DrawLine(v3, v0, c.Value);

        Debug.DrawLine(v4, v5, c.Value);
        Debug.DrawLine(v5, v6, c.Value);
        Debug.DrawLine(v6, v7, c.Value);
        Debug.DrawLine(v7, v4, c.Value);

        Debug.DrawLine(v0, v4, c.Value);
        Debug.DrawLine(v1, v5, c.Value);
        Debug.DrawLine(v2, v6, c.Value);
        Debug.DrawLine(v3, v7, c.Value);
    }
}

public struct TriMeshMaterial {
    public Vector3 albedo, specular, emission;
    public int type;
}