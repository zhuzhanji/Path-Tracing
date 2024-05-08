using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class RayTracingController : MonoBehaviour
{
    public int randomSeed;
    public ComputeShader RayTracingShader;
    public ComputeShader DenoiserShader;

    public Texture[] SkyBoxTextures;
    private bool SkyboxEnabled = true;

    private RenderTexture _target, _converged;
    private Camera _camera;
    private RenderTexture _pingpng;

    private RenderBuffer[] GBuffers;
    private RenderTexture[] GBufferTextures;
    private int[] GBufferIDs;
    private RenderTexture DepthTexure;
    private int DepthTextureID = Shader.PropertyToID("_DepthTexture");
    //self defined pipeline test
    //public Transform cubeTransform;
    public Transform cube;
    //public Mesh cubemesh;
    public Material cubematerial;


    // image effect - screen shader
    private uint _currentSample = 0;
    private Material _addMaterial;

    public int MaxReflections = 4;
    public int maxReflectionsLocked = 4;
    public int maxReflectionsUnlocked = 4;
    [Range(5, 100)]
    public int focal_length = 50;
    [Range(0.01f, 0.99f)]
    public float aperture_radius = 0.4f;

    /*======Lighting======*/
    public Light directionalLight;

    public Color GroundAlbedo, GroundSpecular, GroundEmission;
    private Vector3 _GroundAlbedo, _GroundSpecular, _GroundEmission;

    /*======Traceable Geometries======*/
    public List<Sphere> sphereList;
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
        _BVH_Nodes_Buffer;

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
        _currentSample = 0;
        
        // Clear all lists
        ClearMeshLists();
        
        // Loop over all objects and gather their data
        // foreach (RayTraceable obj in FindObjectsOfType<RayTraceable>())
        int objCounter = 0;
        foreach (RayTraceable obj in _rayTracingObjects)
        {
            // add mesh material data
            _triMeshMats.Add(obj.GetMeshMaterial());

            Mesh mesh = obj.GetComponent<MeshFilter>().sharedMesh;
            
            int firstVertex = _vertices.Count;

            // add triangles for BVH Construction
                // convert localspace mesh vertices to world space
            Vector3[] vertices = mesh.vertices.Select(v => obj.transform.TransformPoint(v)).ToArray();  
            var indices = mesh.GetIndices(0);
            for (int i = 0; i < indices.Length; i += 3) {
                _triangles.Add(new Triangle(
                    vertices[indices[i]],  // world space tri verts
                    vertices[indices[i+1]],
                    vertices[indices[i+2]],
                    indices[i] + firstVertex,  // index offsets
                    indices[i+1] + firstVertex,
                    indices[i+2] + firstVertex,
                    objCounter, objCounter, objCounter  // mesh indices
                ));
            }

            // normals
            // Vector3[] normals = mesh.normals;
            // Debug.Log("normals: " + normals.Length + " vertices: " + vertices.Length);
            _vertices.AddRange(vertices);
            _normals.AddRange(mesh.normals);

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

        // set triangle mesh buffers
        SetComputeBuffer("_BVH_Nodes", _BVH_Nodes_Buffer);
        SetComputeBuffer("_Vertices", _vertexBuffer);
        SetComputeBuffer("_Normals", _normalBuffer);
        SetComputeBuffer("_Indices", _indexBuffer);
        SetComputeBuffer("_MatIndices", _matIndicesBuffer);
        SetComputeBuffer("_TriMeshMats", _triMeshMatBuffer);
        RayTracingShader.SetInt("_NumIndices", _indices.Count);
        RayTracingShader.SetInt("_BVHSize", BVHSize);

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

            _gpuBVHNodes[curIdx].rightOrOffset  = indices_offset;
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

    void DFS_Traverse_GPU_BVH()  {  // to debug the infinite loop on compute shader
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
                Debug.Log("found leaf at idx: " + (nodeStack[stackTop+1]));

                int indexCount = node.leftOrCount;
                int indexOff = node.rightOrOffset;
                Debug.Log(string.Format("indexCount: {0}, indexOff: {1}", indexCount, indexOff));
            } else { // inner node
                Debug.Log("found inner node leaf at idx: " + (nodeStack[stackTop+1]));
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
    }

    private void InitSpheres(bool randomize = false) {
        sphereList ??= new List<Sphere>();
        //if (randomize) {
        //    const int maxNumSpheres = 10;
        //    (float, float) radiusRange = (5f, 20f);
        //    const float posRange = 200f;
        //
        //
        //    for (int i = 0; i < maxNumSpheres; i++)
        //    {
        //        bool shouldAdd = true;
        //        Sphere s = Sphere.CreateRandomSphere(radiusRange, posRange);
        //        // subtract spheres within center radius
        //        if (new Vector2(s.center.x, s.center.z).magnitude < 75f)
        //            continue;
        //        foreach (var sphere in sphereList) // check intersection
        //        {
        //            if (Sphere.intersect(s, sphere)) {
        //                shouldAdd = false;
        //                break;
        //            }
        //        }
        //
        //        if (shouldAdd)
        //            sphereList.Add(s);
        //    }
        //}
        

        foreach (var sphere in FindObjectsOfType<RayTraceableSphere>())
        {
            sphereList.Add(sphere.ToSphere());
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
        _currentSample = 0;
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
    }

    private void SetShaderParametersPerUpdate()
    {
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        RayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
        RayTracingShader.SetVector("_JitterOffset", new Vector2(Random.value, Random.value));
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

        // rng
        RayTracingShader.SetFloat("_Seed", Random.value);

        // tri mesh mats
            // update every frame to allow for hot reloading of material
        UpdateTriMeshMats();
        
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
        {
            Camera cam = Camera.current;
            Graphics.SetRenderTarget(GBuffers, DepthTexure.depthBuffer);
            //Shader.SetGlobalTexture(DepthTextureID, DepthTexure);

            GL.Clear(true, true, Color.black, 1.0f);

            //Material mtcube = cube.GetComponent<Renderer>().material;
            cubematerial.SetPass(0);
            //cubematerial.color = new Color(1, 0.0f, 0.0f);
            foreach (var obj in _rayTracingObjects)
            {
                Graphics.DrawMeshNow(obj.GetComponent<MeshFilter>().sharedMesh, obj.GetComponent<Transform>().localToWorldMatrix);
            }
            foreach (var obj in FindObjectsOfType<RayTraceableSphere>())
            {
                Graphics.DrawMeshNow(obj.GetComponent<MeshFilter>().sharedMesh, obj.GetComponent<Transform>().localToWorldMatrix);
            }

            //Graphics.DrawMeshNow()
            //Graphics.Blit(DepthTexure, destination);
        }

        {
            RebuildMeshObjectBuffers();  // populate meshobject compute buffers
            this.SetShaderParametersPerUpdate();
        }


        InitRenderTexture(ref _converged);
        {
            this.Render(_converged);
            //Graphics.Blit(_converged, destination);
            //return;
        }

        {
            //Graphics.Blit(_converged, destination);
            this.Denoise(_converged, destination);
        }
    }

    private void OnPostRender()
    {


        
    }

    //public void Trace() {
    //    _camera ??= GetComponent<Camera>();
    //    this.Render(_camera.targetTexture);
    //}

    private void Render(RenderTexture _converged)
    {
        // Make sure we have a current render target
        InitRenderTexture(ref _target);
        

        // Set the target and dispatch the compute shader
        RayTracingShader.SetTexture(0, "Result", _target);
        RayTracingShader.SetInt("focal_length", focal_length);
        RayTracingShader.SetFloat("aperture_radius", aperture_radius);
        // spawn a thread group per 8x8 pixel region
            // default thread group consists of 8x8 threads
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        // screen shader (anti aliasing via alpha blending jittered ray traces)
        _addMaterial.SetFloat("_Sample", _currentSample++);

        // Blit the result texture to _converged
        Graphics.Blit(_target, _converged, _addMaterial);  // apply screen shader

        // Graphics.Blit(_target, _converged);
        // use _converged because destination is not HDR texture
        //
        //Graphics.Blit(_converged, destination);
    }

    private void Denoise(RenderTexture src, RenderTexture dst)
    {

        InitRenderTexture(ref _pingpng);
        InitRenderTexture(ref _target);
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
        DenoiserShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        DenoiserShader.SetTexture(0, "_RayTracingTexture", _pingpng);
        DenoiserShader.SetTexture(0, "_NormalTexture", GBufferTextures[0]);
        DenoiserShader.SetTexture(0, "_PositionTexture", GBufferTextures[1]);
        DenoiserShader.SetTexture(0, "Result", _target);
        DenoiserShader.SetInt("level", 1);
        DenoiserShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        /*
        DenoiserShader.SetTexture(0, "_RayTracingTexture", _target);
        DenoiserShader.SetTexture(0, "_NormalTexture", GBufferTextures[0]);
        DenoiserShader.SetTexture(0, "_PositionTexture", GBufferTextures[1]);
        DenoiserShader.SetTexture(0, "Result", _pingpng);
        DenoiserShader.SetInt("level", 2);
        DenoiserShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        DenoiserShader.SetTexture(0, "_RayTracingTexture", _pingpng);
        DenoiserShader.SetTexture(0, "_NormalTexture", GBufferTextures[0]);
        DenoiserShader.SetTexture(0, "_PositionTexture", GBufferTextures[1]);
        DenoiserShader.SetTexture(0, "Result", _target);
        DenoiserShader.SetInt("level", 3);
        DenoiserShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        DenoiserShader.SetTexture(0, "_RayTracingTexture", _target);
        DenoiserShader.SetTexture(0, "_NormalTexture", GBufferTextures[0]);
        DenoiserShader.SetTexture(0, "_PositionTexture", GBufferTextures[1]);
        DenoiserShader.SetTexture(0, "Result", _pingpng);
        DenoiserShader.SetInt("level", 4);
        DenoiserShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        */
        Graphics.Blit(_target, dst);
    }

    private void InitRenderTexture(ref RenderTexture tex)
    {
        if (tex == null || tex.width != Screen.width || tex.height != Screen.height)
        {
            // Release render texture if we already have one
            if (tex != null)
                tex.Release();

            // Get a render target for Ray Tracing
            tex = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            tex.enableRandomWrite = true;
            tex.Create();

            // for screen shader
            _currentSample = 0;
        }
    }

    void Start() {
        GBufferTextures = new RenderTexture[]
        {
            new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear),
            new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear),
            //new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear),
            //new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        };
        GBuffers = new RenderBuffer[GBufferTextures.Length];
        for (int i = 0; i < GBufferTextures.Length; i++)
        {
            GBuffers[i] = GBufferTextures[i].colorBuffer;
        }
        GBufferIDs = new int[]
        {
            Shader.PropertyToID("_GBuffer0"),
            Shader.PropertyToID("_GBuffer1"),
            //Shader.PropertyToID("_GBuffer2"),
            //Shader.PropertyToID("_GBuffer3"),
        };

        DepthTexure = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);

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
            _currentSample = 0;
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
                _currentSample = 0;
            }
        }

        if (Input.GetKeyDown(KeyCode.T)) {
            ToggleSkybox();
        }
    }
}