using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class RayTracingController : MonoBehaviour
{
    [Header("Compute Shaders")]
    public ComputeShader RayTracingShader;
    public ComputeShader DenoiserShader;
    public ComputeShader TemporalAccumulationShader;
    public ComputeShader EstimateVarianceShader;
    public ComputeShader FilterVarianceShader;
    public ComputeShader EAWaveletFilterShader;
    public ComputeShader PostProcessShader;

    private bool SkyboxEnabled = false;

    
    //, _convergedIndirect, _convergedDirect;
    private Camera _camera;
    private Matrix4x4 _LastVP;

    private RenderTexture _pingpng;
    private RenderTexture _DevTmpTarget, _preconverged;
    private RenderTexture _DirectIllumination, _IndirectIllumination;
    private RenderTexture _DirectConverged, _IndirectConverged;
    private RenderBuffer[] GBuffers;
    private RenderBuffer[] GBuffersLast;
    private RenderTexture[] GBufferTextures;
    private RenderTexture DepthTexure;
    private int DepthTextureID = Shader.PropertyToID("_DepthTexture");

    private RenderTexture[] devAccumColorDirect;
    private RenderTexture[] devAccumMomentDirect;
    private RenderTexture[] devAccumColorInDirect;
    private RenderTexture[] devAccumMomentInDirect;

    private RenderTexture devVariance, devTmpVariance, devFilteredVariance;

    // image effect - screen shader
    private int _CurrentFrame = 0;
    private int _CurrentIndex = 0;
    private bool _FirstFrame = true;

    private Material _addMaterial;
    private Material _LDRtoHDR;
    private Material _accumulateMat;

    [Header("Path Tracer")]
    public int MaxReflections = 4;
    [Range(1, 32)]
    public int SamplePerPixel = 1;
    public int maxReflectionsLocked = 4;
    public int maxReflectionsUnlocked = 4;
    public int randomSeed;


    [System.Serializable]
    public enum DenoiserEnum
    {
        None,
        EWA,
        SVGF
    }
    [Header("Denoiser")]
    [SerializeField]
    public DenoiserEnum DenoiserMethod;

    [Range(0.0001f, 20)]
    public float p_phi = 0.2f;
    [Range(0.0001f, 20)]
    public float n_phi = 0.35f;
    [Range(0.0001f, 20)]
    public float c_phi = 0.45f;

    [Range(0.0001f, 200)]
    public float sigNormal = 128.0f;
    [Range(0.0001f, 200)]
    public float sigDepth = 1.0f;
    [Range(0.0001f, 200)]
    public float sigLuminance = 4.0f;

    [Header("Depth of Filed")]
    [Range(5, 100)]
    public int focal_length = 50;
    [Range(0.01f, 0.99f)]
    public float aperture_radius = 0.4f;

    /*======Lighting======*/
    [Header("Lighting")]
    public Light directionalLight;
    public Color GroundAlbedo, GroundSpecular, GroundEmission;
    private Vector3 _GroundAlbedo, _GroundSpecular, _GroundEmission;
    public Texture[] SkyBoxTextures;


    /*======Traceable Geometries======*/
    private List<Sphere> sphereList;
    private ComputeBuffer _SphereBuffer;

    /*========Triangle Meshes========*/
    // This section *heavily* modified from http://three-eyed-games.com/2019/03/18/gpu-path-tracing-in-unity-part-3/
    private static bool _meshObjectsNeedRebuilding = false;
    private static List<RayTraceable> _rayTracingObjects = new List<RayTraceable>();
    private static List<Vector3> _vertices = new List<Vector3>();  // world space mesh vertices
    private static List<Vector3> _normals = new List<Vector3>();  // mesh vertex normals

    private static List<int> _indices = new List<int>();
    private static List<Triangle> _triangles = new List<Triangle>();
    private static List<TriMeshMaterial> _triMeshMats = new List<TriMeshMaterial>();
    private static List<int> _matIndices = new List<int>();

    private static GPU_BVH_Node[] _gpuBVHNodes = null;
    private static BVH_Node BVHRoot = null, selectedBVHNode = null;
    private ComputeBuffer
        _vertexBuffer,
        _normalBuffer,
        _indexBuffer,
        _matIndicesBuffer,
        _triMeshMatBuffer,
        _BVH_Nodes_Buffer,
        _LightSourceBuffer;

    public struct LightSource
    {
        Vector3 position;
        Vector3 normal;
        Vector3 size;
    }

    public LightSource Illuminator;
    //private List<LightSource> _LightSources;

    public Material DeferredGPass;
    public Material TaaPass;
    private Vector2 _JitterOffset;

    public bool enableTAA = false;

    public static void RegisterObject(RayTraceable obj)
    {
        Debug.Log("Register" + obj.name);
        _rayTracingObjects.Add(obj);
        _meshObjectsNeedRebuilding = true;
    }
    public static void UnregisterObject(RayTraceable obj)
    {
        _rayTracingObjects.Remove(obj);
        _meshObjectsNeedRebuilding = true;
    }

    private void ClearMeshLists() {
        _vertices.Clear();
        _normals.Clear();
        _indices.Clear();
        _triangles.Clear();
        _matIndices.Clear();
        _triMeshMats.Clear();
        _gpuBVHNodes = null;
    }

    private void RebuildMeshObjectBuffers()
    {

        if (!_meshObjectsNeedRebuilding)
        {
            return;
        }

        Debug.Log("rebuildng mesh objects");
        _meshObjectsNeedRebuilding = false;
        _CurrentFrame = 0;

        // Clear all lists
        ClearMeshLists();

        // Loop over all objects and gather their data
        // foreach (RayTraceable obj in FindObjectsOfType<RayTraceable>())
        int objCounter = 0;
        foreach (RayTraceable obj in _rayTracingObjects)
        {
            // add mesh material data
            var meshMat = obj.GetMeshMaterial();
            meshMat.matid = 1000 + objCounter;
            _triMeshMats.Add(meshMat);

            Mesh mesh = obj.GetComponent<MeshFilter>().sharedMesh;

            int firstVertex = _vertices.Count;

            // add triangles for BVH Construction
            // convert localspace mesh vertices to world space
            Vector3[] vertices = mesh.vertices.Select(v => obj.transform.TransformPoint(v)).ToArray();
            var indices = mesh.GetIndices(0);
            for (int i = 0; i < indices.Length; i += 3) {
                _triangles.Add(new Triangle(
                    vertices[indices[i]],  // world space tri verts
                    vertices[indices[i + 1]],
                    vertices[indices[i + 2]],
                    indices[i] + firstVertex,  // index offsets
                    indices[i + 1] + firstVertex,
                    indices[i + 2] + firstVertex,
                    objCounter, objCounter, objCounter  // mesh indices
                ));
            }

            // normals
            // Vector3[] normals = mesh.normals;
            // Debug.Log("normals: " + normals.Length + " vertices: " + vertices.Length);
            _vertices.AddRange(vertices);

            
            foreach(Vector3 norm in mesh.normals)
            {
                Vector3 normal = obj.transform.localToWorldMatrix * new Vector4(norm.x, norm.y, norm.z, 0.0f);
                
                _normals.Add((normal.normalized));
            }
            //_normals.AddRange(mesh.normals);

            ++objCounter;
        }

        // rebuild the BVH
        var before = System.DateTime.Now;
        BVHRoot = BVH_Node.BuildBVH(_triangles);
        var after = System.DateTime.Now;
        System.TimeSpan duration = after.Subtract(before);

        // copy the BVH to GPU-friendly BVH Nodes
        int BVHSize = BVHRoot.Size();
        _gpuBVHNodes = new GPU_BVH_Node[BVHSize];
        int gpu_bvh_node_idx = 0;
        Create_GPU_BVH_Node(BVHRoot, ref gpu_bvh_node_idx);
        Debug.Log(string.Format("BVH Build Stats\n time: {0}ms \n Layers: {1} \n #Nodes: {2}",
            duration.Milliseconds,
            BVHRoot.Depth(),
            BVHSize
        ));

        // test DFS traversal
        // DFS_Traverse_GPU_BVH();

        // write BVH Data to GPU
        Debug.Log(string.Format("triangles: {0}", _triangles.Count));

        CreateComputeBuffer(ref _BVH_Nodes_Buffer, _gpuBVHNodes.ToList(), System.Runtime.InteropServices.Marshal.SizeOf(typeof(GPU_BVH_Node)));
        CreateComputeBuffer(ref _vertexBuffer, _vertices, 12);
        CreateComputeBuffer(ref _normalBuffer, _normals, 12);
        CreateComputeBuffer(ref _indexBuffer, _indices, 4);
        CreateComputeBuffer(ref _matIndicesBuffer, _matIndices, 4);
        CreateComputeBuffer(ref _triMeshMatBuffer, _triMeshMats, System.Runtime.InteropServices.Marshal.SizeOf(typeof(TriMeshMaterial)));
        //CreateComputeBuffer(ref _LightSourceBuffer, _LightSources, System.Runtime.InteropServices.Marshal.SizeOf(typeof(LightSource)));

        //_LightSources.Add(Illuminator);

        // set triangle mesh buffers
        SetComputeBuffer("_BVH_Nodes", _BVH_Nodes_Buffer);
        SetComputeBuffer("_Vertices", _vertexBuffer);
        SetComputeBuffer("_Normals", _normalBuffer);
        SetComputeBuffer("_Indices", _indexBuffer);
        SetComputeBuffer("_MatIndices", _matIndicesBuffer);
        SetComputeBuffer("_TriMeshMats", _triMeshMatBuffer);
        SetComputeBuffer("_LightSource", _LightSourceBuffer);

        RayTracingShader.SetInt("_NumIndices", _indices.Count);
        RayTracingShader.SetInt("_BVHSize", BVHSize);
        //RayTracingShader.SetInt("_NumLightSource", 1);

        // triangle vertex material indices
    }

    // make sure _indices is cleared before calling this function
    private void Create_GPU_BVH_Node(BVH_Node node, ref int idx) {
        GPU_BVH_Node curNode = new GPU_BVH_Node() {
            bot = node.bb.bot,
            top = node.bb.top,
            isLeaf = node.leaf ? 1 : 0
        };
        int curIdx = idx;

        // add current node to GPU_BVH_Node buffer
        _gpuBVHNodes[curIdx] = curNode;

        // recursively add the rest of the BVH, in depth first order
        if (node.leaf) {
            int indices_offset = _indices.Count;

            List<Triangle> tris = node.triangles;
            foreach (var tri in tris) {
                _indices.AddRange(tri.GetIndices());
                _matIndices.AddRange(tri.GetMeshIndices());
            } // update indices buffer

            _gpuBVHNodes[curIdx].rightOrOffset = indices_offset;
            _gpuBVHNodes[curIdx].leftOrCount = tris.Count * 3;  // 3 indices per triangle
            // Debug.Log(
            //     System.Convert.ToString(_gpuBVHNodes[curIdx].leftOrCount, 2)
            // );
        } else {  // inner node
            idx++;
            _gpuBVHNodes[curIdx].leftOrCount = idx;
            Create_GPU_BVH_Node(node.left, ref idx);
            idx++;
            _gpuBVHNodes[curIdx].rightOrOffset = idx;
            Create_GPU_BVH_Node(node.right, ref idx);
        }
    }

    void DFS_Traverse_GPU_BVH() {  // to debug the infinite loop on compute shader
        int[] nodeStack = new int[64];  // stack for performing BVH Traversal
        int stackTop = 0;

        nodeStack[0] = 0; // place root node on stack
        int counter = 0;

        while (stackTop >= 0) {  // assume ray intersects bbox of curnode
            counter++;
            // if (counter > 1000) break;
            Debug.Log(string.Format("stackTop: {0}, _gpuBVHNodes[stackTop]: {1}, counter: {2}", stackTop, nodeStack[stackTop], counter));

            GPU_BVH_Node node = _gpuBVHNodes[nodeStack[stackTop--]]; // pop top node off stack

            if (node.isLeaf == 1) { // leaf node
                Debug.Log("found leaf at idx: " + (nodeStack[stackTop + 1]));

                int indexCount = node.leftOrCount;
                int indexOff = node.rightOrOffset;
                Debug.Log(string.Format("indexCount: {0}, indexOff: {1}", indexCount, indexOff));
            } else { // inner node
                Debug.Log("found inner node leaf at idx: " + (nodeStack[stackTop + 1]));
                int leftIndex = node.leftOrCount;
                int rightIndex = node.rightOrOffset;

                // push nodes onto stack. traverse left branches first
                nodeStack[++stackTop] = rightIndex;
                nodeStack[++stackTop] = leftIndex;
                Debug.Log(string.Format("leftIndex: {0}, rightIndex: {1}", leftIndex, rightIndex));
            }
        }
    }


    private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride)
    where T : struct
    {
        // Do we already have a compute buffer?
        if (buffer != null)
        {
            buffer.Release();
            buffer = null;
        }
        if (data.Count == 0) return;
        buffer = new ComputeBuffer(data.Count, stride);
        buffer.SetData(data);
    }
    private void SetComputeBuffer(string name, ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            RayTracingShader.SetBuffer(0, name, buffer);
        }
    }
    /*========End triangle mesh boilerplate========*/

    private void ReleaseComputeBuffer(ref ComputeBuffer buf) {
        if (buf == null) return;
        buf.Release();
        buf = null;
    }

    public static Vector3 GetRandColor() {
        Color c = Random.ColorHSV();
        return new Vector3(c.r, c.g, c.b);
    }

    private void Awake() {
        _camera = GetComponent<Camera>();
        Debug.Log("compute shaders supported: " + SystemInfo.supportsComputeShaders);

        // seed random
        Random.InitState(randomSeed);

        // initialize screen shader material (for anti aliasing)
        _addMaterial = new Material(Shader.Find("Hidden/AddShader"));
        _LDRtoHDR = new Material(Shader.Find("Hidden/LDRtoHDR"));
    }

    private void InitSpheres(bool randomize = false) {
        sphereList ??= new List<Sphere>();

        int matid = 1;
        foreach (var sphere in FindObjectsOfType<RayTraceableSphere>())
        {
            var sp = sphere.ToSphere();
            sp.matid = matid;
            sphereList.Add(sp);
            matid++;
        }
        Debug.Log("sphere number: " + (sphereList.Count));
        // copy sphere structs to compute buffer
        Debug.Log("sphere size" + System.Runtime.InteropServices.Marshal.SizeOf(typeof(Sphere)));
        _SphereBuffer = new ComputeBuffer(sphereList.Count, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Sphere)));
        //Debug.Log("sphere struct size: " + System.Runtime.InteropServices.Marshal.SizeOf(typeof(Sphere)));
        _SphereBuffer.SetData(sphereList);

        // sphere objects
        if (sphereList.Count > 0)
            RayTracingShader.SetBuffer(0, "_Spheres", _SphereBuffer);
        RayTracingShader.SetInt("_NumSpheres", sphereList.Count);
    }

    public Vector3 ColorToVec3(Color c) {
        return (Vector3)(Vector4)(c);
    }


    private void OnEnable() {
        _CurrentFrame = 0;
        InitSpheres(false);
    }

    private void OnDisable() {
        // release compute buffers
        ReleaseComputeBuffer(ref _SphereBuffer);
        ReleaseComputeBuffer(ref _vertexBuffer);
        ReleaseComputeBuffer(ref _normalBuffer);
        ReleaseComputeBuffer(ref _indexBuffer);
        ReleaseComputeBuffer(ref _BVH_Nodes_Buffer);
        ReleaseComputeBuffer(ref _matIndicesBuffer);
        ReleaseComputeBuffer(ref _triMeshMatBuffer);

        _pingpng.Release();
        _DevTmpTarget.Release();

        devVariance.Release();
        devTmpVariance.Release();
        devFilteredVariance.Release();

        _DirectIllumination.Release();
        _IndirectIllumination.Release();
        _DirectConverged.Release();
        _IndirectConverged.Release();

        for (int i = 0; i < GBufferTextures.Length; i++)
        {
            GBufferTextures[i].Release();
        }
        for (int i = 0; i < 2; i++)
        {
            devAccumColorDirect[i].Release();
            devAccumColorInDirect[i].Release();
            devAccumMomentDirect[i].Release();
            devAccumMomentInDirect[i].Release();
        }

    }

    private void SetShaderParametersPerUpdate()
    {
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        // rng
        RayTracingShader.SetFloat("_Seed", Random.value);
        RayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
        RayTracingShader.SetMatrix("_LastVP", _LastVP);

        //var mat0 = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, false);
        //var mat1 = _camera.projectionMatrix;
        //Debug.Log(mat0);
        //Debug.Log(mat1);
        _JitterOffset = new Vector2((Random.value - 0.5f) * 1.99f, (Random.value - 0.5f) * 1.99f);
        
        RayTracingShader.SetVector("_JitterOffset", _JitterOffset);


        RayTracingShader.SetInt("_MaxReflections", MaxReflections);

        // lighting params
        //Vector3 l = directionalLight.transform.forward;
        //RayTracingShader.SetVector(
        //    "_DirectionalLight", new Vector4(l.x, l.y, l.z, directionalLight.intensity)
        //);

        // ground plane
        RayTracingShader.SetVector("_GroundAlbedo", ColorToVec3(GroundAlbedo));
        RayTracingShader.SetVector("_GroundSpecular", ColorToVec3(GroundSpecular));
        RayTracingShader.SetVector("_GroundEmission", ColorToVec3(GroundEmission));



        // tri mesh mats
        // update every frame to allow for hot reloading of material
        //UpdateTriMeshMats();

    }

    // public void RandomizeTriMeshMats() {
    // }

    private void UpdateTriMeshMats() {
        _triMeshMats.Clear();
        foreach (RayTraceable obj in _rayTracingObjects)
            _triMeshMats.Add(obj.GetMeshMaterial());

        CreateComputeBuffer(ref _triMeshMatBuffer, _triMeshMats, System.Runtime.InteropServices.Marshal.SizeOf(typeof(TriMeshMaterial)));
        SetComputeBuffer("_TriMeshMats", _triMeshMatBuffer);
    }

    //相机绘制
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {

        this.RebuildMeshObjectBuffers();  // populate meshobject compute buffers
        this.SetShaderParametersPerUpdate();

    //   {
    //       Camera cam = Camera.current;
    //        if(_CurrentIndex == 0)
    //            Graphics.SetRenderTarget(GBuffers, DepthTexure.depthBuffer);
    //        else
    //            Graphics.SetRenderTarget(GBuffersLast, DepthTexure.depthBuffer);
    //        //Shader.SetGlobalTexture(DepthTextureID, DepthTexure);
    // 
    //        GL.Clear(true, true, Color.black, 1.0f);
    // 
    //        //Material mtcube = cube.GetComponent<Renderer>().material;
    //        DeferredGPass.SetPass(0);
    //        DeferredGPass.SetMatrix("_LastVP", _LastVP);
    //        //DeferredGPass.SetVector("_Jitter", new Vector2(_JitterOffset.x / Screen.width, _JitterOffset.y / Screen.height));
    //        DeferredGPass.SetVector("_Jitter", new Vector2(0, 0));
    //
    //        int matcounter = 0;
    //        foreach (var obj in _rayTracingObjects)
    //        {
    //            DeferredGPass.SetInt("matid", matcounter);
    //            matcounter++;
    //            DeferredGPass.SetVector("_Albedo", obj.albedo);
    //            Graphics.DrawMeshNow(obj.GetComponent<MeshFilter>().sharedMesh, obj.GetComponent<Transform>().localToWorldMatrix);
    //        }
    //        foreach (var obj in FindObjectsOfType<RayTraceableSphere>())
    //        {
    //            DeferredGPass.SetInt("matid", matcounter);
    //            matcounter++;
    //            DeferredGPass.SetVector("_Albedo", obj.albedo);
    //            Graphics.DrawMeshNow(obj.GetComponent<MeshFilter>().sharedMesh, obj.GetComponent<Transform>().localToWorldMatrix);
    //        }
    // 
    //        //Graphics.DrawMeshNow()
    //        //Graphics.Blit(DepthTexure, destination);
    //    }

        //Ray tracing

        {
            int _NeedGbuffer = 1;
            for (int i = 0; i < this.SamplePerPixel; i++)
            {
                this.SetShaderParametersPerUpdate();

                this.PathTracing(_NeedGbuffer);
                this._CurrentFrame++;
                _NeedGbuffer = 0;
            }

        }


        if (DenoiserMethod == DenoiserEnum.None)
        {
            InitRenderTexture(ref _pingpng);
            //   InitRenderTexture(ref _preconverged);
            //
            //   TaaPass.SetTexture("currentTex", _converged);
            //   TaaPass.SetTexture("preTex", _preconverged);
            //   TaaPass.SetTexture("velocityTexture", GBufferTextures[3]);
            //   TaaPass.SetTexture("currentDepth", DepthTexure);
            //   TaaPass.SetVector("_ScreenSize", new Vector2(Screen.width, Screen.height));
            //   TaaPass.SetInt("firstFrame", _FirstFrame ? 1 : 0);
            //
            //   Graphics.Blit(null, _pingpng, TaaPass);
            //
            //   Graphics.Blit(_pingpng, _preconverged);
            //
            //   Graphics.Blit(_pingpng, destination, _LDRtoHDR);
            _LDRtoHDR.SetTexture("_DirectIllumination", _DirectConverged);
            _LDRtoHDR.SetTexture("_IndirectIllumination", _IndirectConverged);
            Graphics.Blit(null, destination, _LDRtoHDR);
            return;
        }
        else if (DenoiserMethod == DenoiserEnum.EWA)
        {
            this.EAWDenoise(_DirectConverged, _DirectIllumination);
            this.EAWDenoise(_IndirectConverged, _IndirectIllumination);
            _LDRtoHDR.SetTexture("_DirectIllumination", _DirectIllumination);
            _LDRtoHDR.SetTexture("_IndirectIllumination", _IndirectIllumination);
            Graphics.Blit(null, destination, _LDRtoHDR);
        }
        else
        {
            this.SVGFDenoise(_DirectConverged, devAccumColorDirect, devAccumMomentDirect, _DirectIllumination);
            this.SVGFDenoise(_IndirectConverged, devAccumColorInDirect, devAccumMomentInDirect, _IndirectIllumination);

            _LDRtoHDR.SetTexture("_DirectIllumination", _DirectIllumination);
            _LDRtoHDR.SetTexture("_IndirectIllumination", _IndirectIllumination);
            Graphics.Blit(null, destination, _LDRtoHDR);
        }

        

        this._LastVP = this._camera.projectionMatrix * this._camera.worldToCameraMatrix;
        this._FirstFrame = false;
        this._CurrentIndex = this._CurrentIndex ^ 1;

    }

    private void OnPostRender()
    {



    }

    //public void Trace() {
    //    _camera ??= GetComponent<Camera>();
    //    this.Render(_camera.targetTexture);
    //}

    private void PathTracing(int _needGbuffer)
    {
        // Make sure we have a current render target
        InitRenderTexture(ref _DevTmpTarget);

        // Set the target and dispatch the compute shader
        RayTracingShader.SetTexture(0, "DirectIllumination", _DirectIllumination);
        RayTracingShader.SetTexture(0, "IndirectIllumination", _IndirectIllumination);

        RayTracingShader.SetTexture(0, "GbufferNormal",   _CurrentIndex == 0 ? GBufferTextures[0] : GBufferTextures[4]);
        RayTracingShader.SetTexture(0, "GbufferPosition", _CurrentIndex == 0 ? GBufferTextures[1] : GBufferTextures[5]);
        RayTracingShader.SetTexture(0, "GbufferAlbedo", GBufferTextures[2]);
        RayTracingShader.SetTexture(0, "GbufferMotion", GBufferTextures[3]);

        RayTracingShader.SetInt("focal_length", focal_length);
        RayTracingShader.SetInt("need_gbuffer", _needGbuffer);
        RayTracingShader.SetFloat("aperture_radius", aperture_radius);
        // spawn a thread group per 8x8 pixel region
        // default thread group consists of 8x8 threads
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        // screen shader (anti aliasing via alpha blending jittered ray traces)
        _addMaterial.SetFloat("_Sample", _CurrentFrame);

        // Blit the result texture to _converged
        Graphics.Blit(_DirectIllumination, _DirectConverged, _addMaterial);  // apply screen shader
        Graphics.Blit(_IndirectIllumination, _IndirectConverged, _addMaterial);  // apply screen shader


    }
    private void TemporalAccumulation(RenderTexture src,
        RenderTexture devAccumColorIn, RenderTexture devAccumColorOut,
        RenderTexture devAccumMomentIn, RenderTexture devAccumMomentOut)
    {
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);

        // Set the target and dispatch the compute shader

        TemporalAccumulationShader.SetTexture(0, "devColorIn", src);
        TemporalAccumulationShader.SetTexture(0, "_NormalTexture",     _CurrentIndex == 0 ? GBufferTextures[0] : GBufferTextures[4]);
        TemporalAccumulationShader.SetTexture(0, "_LastNormalTexture", _CurrentIndex == 0 ? GBufferTextures[4] : GBufferTextures[0]);
        TemporalAccumulationShader.SetTexture(0, "_PositionTexture",   _CurrentIndex == 0 ? GBufferTextures[1] : GBufferTextures[5]);
        TemporalAccumulationShader.SetTexture(0, "_LastPositionTexture", _CurrentIndex == 0 ? GBufferTextures[5] : GBufferTextures[1]);
     
        TemporalAccumulationShader.SetTexture(0, "_MotionTexture", GBufferTextures[3]);

        TemporalAccumulationShader.SetTexture(0, "devAccumColorIn", devAccumColorIn);
        TemporalAccumulationShader.SetTexture(0, "devAccumColorOut", devAccumColorOut);
        
        TemporalAccumulationShader.SetTexture(0, "devAccumMomentIn", devAccumMomentIn);
        TemporalAccumulationShader.SetTexture(0, "devAccumMomentOut", devAccumMomentOut);
        TemporalAccumulationShader.SetBool("_FirstFrame", _FirstFrame);

        TemporalAccumulationShader.SetMatrix("_ViewMatrix", _camera.worldToCameraMatrix);
        TemporalAccumulationShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);


    }

    private void EstimateVariance(RenderTexture devAccumMoment)
    {
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);

        EstimateVarianceShader.SetTexture(0, "devMoment", devAccumMoment);
        EstimateVarianceShader.SetTexture(0, "devVariance", devVariance);
        //EstimateVarianceShader.SetTexture(0, "GbufferNormal", _CurrentIndex == 0 ? GBufferTextures[0] : GBufferTextures[4]);
        EstimateVarianceShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
    }

    private void FilterVariance(RenderTexture devVarianceIn, RenderTexture devVarianceOut)
    {
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        FilterVarianceShader.SetTexture(0, "devVarianceIn", devVarianceIn);
        FilterVarianceShader.SetTexture(0, "devFilteredVariance", devVarianceOut);

        FilterVarianceShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
    }

    private void EAWaveletFilter(RenderTexture devColorOut, RenderTexture devColorIn,
        RenderTexture devVarianceOut, RenderTexture devVarianceIn, RenderTexture devFilteredVar, int level)
    {
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);

        EAWaveletFilterShader.SetTexture(0, "devColorIn", devColorIn);
        EAWaveletFilterShader.SetTexture(0, "devVarainceIn", devVarianceIn);
        EAWaveletFilterShader.SetTexture(0, "devFilteredVariance", devFilteredVar);
        EAWaveletFilterShader.SetTexture(0, "_NormalTexture", _CurrentIndex == 0 ? GBufferTextures[0] : GBufferTextures[4]);
        EAWaveletFilterShader.SetTexture(0, "_PositionTexture", _CurrentIndex == 0 ? GBufferTextures[1] : GBufferTextures[5]);
        EAWaveletFilterShader.SetTexture(0, "devColorOut", devColorOut);
        EAWaveletFilterShader.SetTexture(0, "devVarianceOut", devVarianceOut);
        EAWaveletFilterShader.SetInt("level", level);

        EAWaveletFilterShader.SetFloat("sigNormal", sigNormal);
        EAWaveletFilterShader.SetFloat("sigDepth", sigDepth);
        EAWaveletFilterShader.SetFloat("sigLuminance", sigLuminance);

        EAWaveletFilterShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
    }

    private void Clamp(RenderTexture devColorIn, RenderTexture devColorOut)
    {
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        PostProcessShader.SetTexture(0, "devColorIn", devColorIn);
        PostProcessShader.SetTexture(0, "devColorOut", devColorOut);
        PostProcessShader.SetInt("_Radius", 1);
        PostProcessShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
    }

    private void SVGFDenoise(RenderTexture src, RenderTexture[] devAccumColor, RenderTexture[] devAccumMoment, RenderTexture dst)
    {
        InitRenderTexture(ref _pingpng);
        //from devAccumColor[_CurrentIndex] to devAccumColor[_CurrentIndex ^ 1]
        //from devAccumMoment[_CurrentIndex] to devAccumMoment[_CurrentIndex ^ 1]
        this.TemporalAccumulation(src, devAccumColor[_CurrentIndex], devAccumColor[_CurrentIndex ^ 1],
            devAccumMoment[_CurrentIndex], devAccumMoment[_CurrentIndex ^ 1]);

        //from devAccumMoment[_CurrentIndex ^ 1] to devVariance
        this.EstimateVariance(devAccumMoment[_CurrentIndex ^ 1]);


        //
        this.FilterVariance(devVariance, devFilteredVariance);
        this.EAWaveletFilter(_DevTmpTarget, devAccumColor[_CurrentIndex ^ 1], devTmpVariance, devVariance, devFilteredVariance, 0);
        //For accumulation of next frame
        Graphics.Blit(_DevTmpTarget, devAccumColor[_CurrentIndex ^ 1]);

        this.FilterVariance(devTmpVariance, devFilteredVariance);
        this.EAWaveletFilter(_DevTmpTarget, devAccumColor[_CurrentIndex ^ 1], devVariance, devTmpVariance, devFilteredVariance, 1);
        //Graphics.Blit(devTmpVariance, devVariance);
        
        this.FilterVariance(devVariance, devFilteredVariance);
        this.EAWaveletFilter(_pingpng, _DevTmpTarget, devTmpVariance, devVariance, devFilteredVariance, 2);
        //Graphics.Blit(devTmpVariance, devVariance);
        
        this.FilterVariance(devTmpVariance, devFilteredVariance);
        this.EAWaveletFilter(_DevTmpTarget, _pingpng, devVariance, devTmpVariance, devFilteredVariance, 3);
        //Graphics.Blit(devTmpVariance, devVariance);
        
        this.FilterVariance(devVariance, devFilteredVariance);
        this.EAWaveletFilter(_pingpng, _DevTmpTarget, devTmpVariance, devVariance, devFilteredVariance, 4);

        this.Clamp(_pingpng, _DevTmpTarget);
        this.Clamp(_DevTmpTarget, dst);
        
        

    }

    private void EAWDenoise(RenderTexture src, RenderTexture dst)
    {
        InitRenderTexture(ref _pingpng);
        
        // spawn a thread group per 8x8 pixel region
        // default thread group consists of 8x8 threads
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);

        // Set the target and dispatch the compute shader

        DenoiserShader.SetTexture(0, "_RayTracingTexture", src);
        DenoiserShader.SetTexture(0, "_NormalTexture", GBufferTextures[0]);
        DenoiserShader.SetTexture(0, "_PositionTexture", GBufferTextures[1]);
        DenoiserShader.SetTexture(0, "Result", _pingpng);
        DenoiserShader.SetInt("level", 0);
        DenoiserShader.SetFloat("p_phi", p_phi);
        DenoiserShader.SetFloat("n_phi", n_phi);
        DenoiserShader.SetFloat("c_phi", c_phi);
        DenoiserShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        DenoiserShader.SetTexture(0, "_RayTracingTexture", _pingpng);
        DenoiserShader.SetTexture(0, "_NormalTexture", GBufferTextures[0]);
        DenoiserShader.SetTexture(0, "_PositionTexture", GBufferTextures[1]);
        DenoiserShader.SetTexture(0, "Result", _DevTmpTarget);
        DenoiserShader.SetInt("level", 1);
        DenoiserShader.SetFloat("p_phi", p_phi);
        DenoiserShader.SetFloat("n_phi", n_phi);
        DenoiserShader.SetFloat("c_phi", c_phi);
        DenoiserShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        
        DenoiserShader.SetTexture(0, "_RayTracingTexture", _DevTmpTarget);
        DenoiserShader.SetTexture(0, "_NormalTexture", GBufferTextures[0]);
        DenoiserShader.SetTexture(0, "_PositionTexture", GBufferTextures[1]);
        DenoiserShader.SetTexture(0, "Result", _pingpng);
        DenoiserShader.SetInt("level", 2);
        DenoiserShader.SetFloat("p_phi", p_phi);
        DenoiserShader.SetFloat("n_phi", n_phi);
        DenoiserShader.SetFloat("c_phi", c_phi);
        DenoiserShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        DenoiserShader.SetTexture(0, "_RayTracingTexture", _pingpng);
        DenoiserShader.SetTexture(0, "_NormalTexture", GBufferTextures[0]);
        DenoiserShader.SetTexture(0, "_PositionTexture", GBufferTextures[1]);
        DenoiserShader.SetTexture(0, "Result", _DevTmpTarget);
        DenoiserShader.SetInt("level", 3);
        DenoiserShader.SetFloat("p_phi", p_phi);
        DenoiserShader.SetFloat("n_phi", n_phi);
        DenoiserShader.SetFloat("c_phi", c_phi);
        DenoiserShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        DenoiserShader.SetTexture(0, "_RayTracingTexture", _DevTmpTarget);
        DenoiserShader.SetTexture(0, "_NormalTexture", GBufferTextures[0]);
        DenoiserShader.SetTexture(0, "_PositionTexture", GBufferTextures[1]);
        DenoiserShader.SetTexture(0, "Result", _pingpng);
        DenoiserShader.SetInt("level", 4);
        DenoiserShader.SetFloat("p_phi", p_phi);
        DenoiserShader.SetFloat("n_phi", n_phi);
        DenoiserShader.SetFloat("c_phi", c_phi);
        DenoiserShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        //this.Clamp(_DevTmpTarget, _pingpng);
        //this.Clamp(_pingpng, _DevTmpTarget);

        if (enableTAA)
        {
            InitRenderTexture(ref _preconverged);

            TaaPass.SetTexture("currentTex", _pingpng);
            TaaPass.SetTexture("preTex", _preconverged);
            TaaPass.SetTexture("velocityTexture", GBufferTextures[3]);
            TaaPass.SetTexture("currentDepth", DepthTexure);
            TaaPass.SetVector("_ScreenSize", new Vector2(Screen.width, Screen.height));
            TaaPass.SetInt("firstFrame", _FirstFrame ? 1 : 0);

            Graphics.Blit(null, _DevTmpTarget, TaaPass);

            Graphics.Blit(_DevTmpTarget, dst);
        }
        else
        {
            Graphics.Blit(_pingpng, dst);
            
        }
    }

    private void InitRenderTexture(ref RenderTexture tex, RenderTextureFormat ft = RenderTextureFormat.ARGBFloat)
    {
        if (tex == null || tex.width != Screen.width || tex.height != Screen.height)
        {
            // Release render texture if we already have one
            if (tex != null)
                tex.Release();

            // Get a render target for Ray Tracing
            tex = new RenderTexture(Screen.width, Screen.height, 0,
                ft, RenderTextureReadWrite.Linear);
            tex.enableRandomWrite = true;
            tex.Create();
        }
    }

    void Start() {

        GBufferTextures = new RenderTexture[]
        {
            //normal + matid
            new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear),
            //position
            new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear),
            //albedo
            new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear),
            //motion
            new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear),


            //last normal + matid 
            new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear),
            //position
            new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear),
        };


        GBuffers = new RenderBuffer[4];

        for (int i = 0; i < GBufferTextures.Length; i++)
        {
            GBufferTextures[i].enableRandomWrite = true;
        }
        GBuffers = new RenderBuffer[4];
        GBuffersLast = new RenderBuffer[4];
        GBuffers[0] = GBufferTextures[0].colorBuffer;
        GBuffers[1] = GBufferTextures[1].colorBuffer;
        GBuffers[2] = GBufferTextures[2].colorBuffer;
        GBuffers[3] = GBufferTextures[3].colorBuffer;
        GBuffersLast[0] = GBufferTextures[4].colorBuffer;
        GBuffersLast[1] = GBufferTextures[5].colorBuffer;
        GBuffersLast[2] = GBufferTextures[2].colorBuffer;
        GBuffersLast[3] = GBufferTextures[3].colorBuffer;

        DepthTexure = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);


        devAccumColorDirect = new RenderTexture[] { null, null };
        devAccumMomentDirect = new RenderTexture[] { null, null };
        devAccumColorInDirect = new RenderTexture[] { null, null };
        devAccumMomentInDirect = new RenderTexture[] { null, null };
        for (int i = 0; i < 2; i++)
        {
            InitRenderTexture(ref devAccumColorDirect[i]);
            InitRenderTexture(ref devAccumMomentDirect[i]);
            InitRenderTexture(ref devAccumColorInDirect[i]);
            InitRenderTexture(ref devAccumMomentInDirect[i]);
        }
        InitRenderTexture(ref devVariance, RenderTextureFormat.RFloat);
        InitRenderTexture(ref devFilteredVariance, RenderTextureFormat.RFloat);
        InitRenderTexture(ref devTmpVariance, RenderTextureFormat.RFloat);

        InitRenderTexture(ref _DirectConverged);
        InitRenderTexture(ref _IndirectConverged);

        InitRenderTexture(ref _DirectIllumination);
        InitRenderTexture(ref _IndirectIllumination);

        RebuildMeshObjectBuffers();  // populate meshobject compute buffers
        selectedBVHNode = BVHRoot;

        UpdateSkybox(0);
        RayTracingShader.SetInt("_SkyboxEnabled", SkyboxEnabled ? 1 : 0);

    }

    private void ToggleSkybox() {
        SkyboxEnabled = !SkyboxEnabled;
        RayTracingShader.SetInt("_SkyboxEnabled", SkyboxEnabled ? 1 : 0);
    }

    private void UpdateSkybox(int i) {
        if (i < SkyBoxTextures.Length)
            RayTracingShader.SetTexture(0, "_SkyboxTexture", SkyBoxTextures[i]);
    }

    private void Update() {
        if (transform.hasChanged)
        {
            _CurrentFrame = 0;
            transform.hasChanged = false;
        }

        // draw BVH bound boxes
        /*
        selectedBVHNode.Draw();

        if (Input.GetKeyDown(KeyCode.LeftArrow))
            selectedBVHNode = selectedBVHNode.left ?? selectedBVHNode;
            
        if (Input.GetKeyDown(KeyCode.RightArrow))
            selectedBVHNode = selectedBVHNode.right ?? selectedBVHNode;

        if (Input.GetKeyDown(KeyCode.UpArrow))
            selectedBVHNode = selectedBVHNode.parent ?? selectedBVHNode;
        
        // display Samples per pixel of current image
        if (Input.GetKeyDown(KeyCode.P))
            Debug.Log("Samples per pixel: " + _currentSample + " | Reflections: " + MaxReflections);
        */
        // update skybox
        for (int i = 0; i < SkyBoxTextures.Length; i++)
        {
            if ( Input.GetKeyDown( "" + i ) )
            {
                UpdateSkybox(i);
                _CurrentFrame = 0;
            }
        }

        if (Input.GetKeyDown(KeyCode.T)) {
            ToggleSkybox();
        }
    }
}