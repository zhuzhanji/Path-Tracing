static const float EPSILON = 1e-8;
static const float PI = 3.14159265f;
static const float PiInv = 1.0/3.1415926f;

static const float2 Halton_2_3[8] =
{
	float2(0.0f, -1.0f / 3.0f),
	float2(-1.0f / 2.0f, 1.0f / 3.0f),
	float2(1.0f / 2.0f, -7.0f / 9.0f),
	float2(-3.0f / 4.0f, -1.0f / 9.0f),
	float2(1.0f / 4.0f, 5.0f / 9.0f),
	float2(-1.0f / 4.0f, -5.0f / 9.0f),
	float2(3.0f / 4.0f, 1.0f / 9.0f),
	float2(-7.0f / 8.0f, 7.0f / 9.0f)
};

/*========== Utility ============*/



float sdot(float3 x, float3 y, float f = 1.0f) {
    return saturate(dot(x, y) * f);
}


float absDot(float3 a, float3 b) {
    return abs(dot(a, b));
}

float satDot(float3 a, float3 b) {
        return max(dot(a, b), 0.f);
}

// From pixar - https://graphics.pixar.com/library/OrthonormalB/paper.pdf
void basis(float3 n, out float3 b1, out float3 b2) 
{
    if(n.z<0.){
        float a = 1.0 / (1.0 - n.z);
        float b = n.x * n.y * a;
        b1 = float3(1.0 - n.x * n.x * a, -b, n.x);
        b2 = float3(b, n.y * n.y*a - 1.0, -n.y);
    }
    else{
        float a = 1.0 / (1.0 + n.z);
        float b = -n.x * n.y * a;
        b1 = float3(1.0 - n.x * n.x * a, b, -n.x);
        b2 = float3(b, 1.0 - n.y * n.y * a, -n.y);
    }
}

float3x3 GetTangentSpace(float3 normal)
{
    // Choose a helper vector for the cross product
    float3 helper = float3(1, 0, 0);
    if (abs(normal.x) > 0.99f)
        helper = float3(0, 0, 1);
    // Generate vectors
    float3 tangent = normalize(cross(normal, helper));
    float3 binormal = normalize(cross(normal, tangent));
    return float3x3(tangent, binormal, normal);
}

float3 toWorld(float3 x, float3 y, float3 z, float3 v)
{
    return v.x*x + v.y*y + v.z*z;
}

float3 toLocal(float3 x, float3 y, float3 z, float3 v)
{
    return float3(dot(v, x), dot(v, y), dot(v, z));
}

float luma(float3 color) {
    return dot(color, float3(0.299, 0.587, 0.114));
}

float energy(float3 color)
{
    return dot(color, 1.0f / 3.0f);
}

float2 toConcentricDisk(float x, float y) {
    float r = sqrt(x);
    float theta = y * PI * 2.0f;
    return float2(cos(theta), sin(theta)) * r;
}

float SmoothnessToPhongAlpha(float s)
{
    return pow(1000.0f, s * s);
}

// ---------------------------------------------
// Microfacet
// ---------------------------------------------
float schlickG(float cosTheta, float alpha) {
    float a = alpha * .5f;
    return cosTheta / (cosTheta * (1.f - a) + a);
}

float smithG(float cosWo, float cosWi, float alpha) {
    return schlickG(abs(cosWo), alpha) * schlickG(abs(cosWi), alpha);
}

float3 F_Schlick(float3 f0, float theta) {
    return f0 + (1.-f0) * pow(1.0-theta, 5.);
}

float F_Schlick(float f0, float f90, float theta) {
    return f0 + (f90 - f0) * pow(1.0-theta, 5.0);
}

float schlickEquation(float ior, float n, float theta) {
    float r0 = (n - ior) / (n + ior);
    r0 = r0 * r0;
    return r0 + (1.f - r0) * pow(1.f - theta, 5.f);
}

float D_GTR(float alpha, float NoH, float k) {
    float a2 = pow(alpha, 2.);
    return a2 / (PI * pow((NoH*NoH)*(a2*a2-1.)+1., k));
}

float SmithG(float NDotV, float alphaG)
{
    float a = alphaG * alphaG;
    float b = NDotV * NDotV;
    return (2.0 * NDotV) / (NDotV + sqrt(a + b - a * b));
}

float GeometryTerm(float NoL, float NoV, float roughness)
{
    float a2 = roughness*roughness;
    float G1 = SmithG(NoV, a2);
    float G2 = SmithG(NoL, a2);
    return G1*G2;
}

float3 SampleGGXVNDF(float3 V, float ax, float ay, float r1, float r2)
{
    float3 Vh = normalize(float3(ax * V.x, ay * V.y, V.z));

    float lensq = Vh.x * Vh.x + Vh.y * Vh.y;
    float3 T1 = lensq > 0. ? float3(-Vh.y, Vh.x, 0) * 1.0 / sqrt(lensq) : float3(1, 0, 0);
    float3 T2 = cross(Vh, T1);

    float r = sqrt(r1);
    float phi = 2.0 * PI * r2;
    float t1 = r * cos(phi);
    float t2 = r * sin(phi);
    float s = 0.5 * (1.0 + Vh.z);
    t2 = (1.0 - s) * sqrt(1.0 - t1 * t1) + s * t2;

    float3 Nh = t1 * T1 + t2 * T2 + sqrt(max(0.0, 1.0 - t1 * t1 - t2 * t2)) * Vh;

    return normalize(float3(ax * Nh.x, ay * Nh.y, max(0.0, Nh.z)));
}

float GGXVNDFPdf(float NoH, float NoV, float alpha)
{
 	float D = D_GTR(alpha, NoH, 2.);
    float G1 = SmithG(NoV, alpha*alpha);
    return (D * G1) / max(0.00001, 4.0f * NoV);
    //return (G1) / max(0.00001, 4.0f * NoV);
}


float3 GTR2Sample(float3 n, float3 wo, float alpha, float2 r) {
    float3x3 transMat = GetTangentSpace(n);
    float3x3 transInv = transpose(transMat);

    float3 vh = normalize(mul(transInv , wo) * float3(alpha, alpha, 1.f));

    float lenSq = vh.x * vh.x + vh.y * vh.y;
    float3 t = lenSq > 0.f ? float3(-vh.y, vh.x, 0.f) / sqrt(lenSq) : float3(1.f, 0.f, 0.f);
    float3 b = cross(vh, t);

    float2 p = toConcentricDisk(r.x, r.y);
    float s = 0.5f * (vh.z + 1.f);
    p.y = (1.f - s) * sqrt(1.f - p.x * p.x) + s * p.y;

    float3 h = t * p.x + b * p.y + vh * sqrt(max(0.f, 1.f - dot(p, p)));
    h = float3(h.x * alpha, h.y * alpha, max(0.f, h.z));
    return normalize(mul(transMat , h));
}

float GTR2Distrib(float cosTheta, float alpha) {
    if (cosTheta < 1e-6f) {
        return 0.f;
    }
    float aa = alpha * alpha;
    float nom = aa;
    float denom = cosTheta * cosTheta * (aa - 1.f) + 1.f;
    denom = denom * denom * PI;
    return nom / denom;
}
float GTR2Pdf(float3 n, float3 m, float3 wo, float alpha) {
    return GTR2Distrib(dot(n, m), alpha) * schlickG(dot(n, wo), alpha) *
        absDot(m, wo) / absDot(n, wo);
}

float powerHeuristic(float f, float g)
{
    float f2 = f * f;
    return f2 / (f2 + g * g);
}

/*
p(w) = dist^2 / (cos(theta) * A)
pdf = 1/A
*/
float GetLightPdf(float invA, float3 x, float3 y, float3 ny)
{
    float3 yx = x - y;
    return invA * dot(yx, yx) / absDot(ny, normalize(yx));
}

bool refract(float3 n, float3 wi, float ior, out float3 wt) {
        float cosIn = dot(n, wi);
        if (cosIn < 0) {
            ior = 1.f / ior;
        }
        float sin2In = max(0.f, 1.f - cosIn * cosIn);
        float sin2Tr = sin2In / (ior * ior);

        if (sin2Tr >= 1.f) {
            return false;
        }
        float cosTr = sqrt(1.f - sin2Tr);
        if (cosIn < 0) {
            cosTr = -cosTr;
        }
        wt = normalize(-wi / ior + n * (cosIn / ior - cosTr));
        return true;
    }