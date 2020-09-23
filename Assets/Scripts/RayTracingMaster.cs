using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RayTracingMaster : MonoBehaviour
{
    [Header("GPU Raytracing Variables")]
    public ComputeShader RayTracingShader;

    public Texture SkyboxTexture;

    private RenderTexture _target;

    private Camera _camera;

    private uint _currentSample = 0;

    private Material _addMaterial;

    public Light dirLight;

    [Header("Sphere Placement")]
    //Public parameters to control sphere placement
    public Vector2 SphereRadius = new Vector2(3.0f, 8.0f);
    public uint SpheresMax = 100;
    public float SpherePlacementRadius = 100.0f;

    private ComputeBuffer _sphereBuffer; //communciate with the raytracing compute shader 

    [Header("Moving Light")]
    public bool isLightMoving;

    public float secondsInFullDay = 120f;
    [Range(0, 1)]
    public float currentTimeOfDay = 0;
    [HideInInspector]
    public float timeMultiplier = 1f;

    float dirLightIntensity;

    struct Sphere
    {
        public Vector3 position;
        public float radius;
        public Vector3 albedo;
        public Vector3 specular;
    };

    private void OnEnable()
    {
        _currentSample = 0;
        SetUpScene();
        
    }

    private void OnDisable()
    {
        if (_sphereBuffer != null)
            _sphereBuffer.Release(); //release the buffer
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }


    private void Start()
    {
        dirLightIntensity = dirLight.intensity;
    }

    private void SetUpScene()
    {
        List<Sphere> spheres = new List<Sphere>();
        // Add a number of random spheres
        for (int i = 0; i < SpheresMax; i++)
        {
            Sphere sphere = new Sphere();
            // Radius and radius
            sphere.radius = SphereRadius.x + Random.value * (SphereRadius.y - SphereRadius.x);
            Vector2 randomPos = Random.insideUnitCircle * SpherePlacementRadius;
            sphere.position = new Vector3(randomPos.x, sphere.radius, randomPos.y);
            // Reject spheres that are intersecting others
            foreach (Sphere other in spheres)
            {
                float minDist = sphere.radius + other.radius;
                if (Vector3.SqrMagnitude(sphere.position - other.position) < minDist * minDist)
                    goto SkipSphere;
            }
            // Albedo and specular color
            Color color = Random.ColorHSV();
            bool metal = Random.value < 0.5f;
            sphere.albedo = metal ? Vector3.zero : new Vector3(color.r, color.g, color.b);
            sphere.specular = metal ? new Vector3(color.r, color.g, color.b) : Vector3.one * 0.04f;
            // Add the sphere to the list
            spheres.Add(sphere);
        SkipSphere:
            continue;
        }
        // Assign to compute buffer
        _sphereBuffer = new ComputeBuffer(spheres.Count, 40);
        _sphereBuffer.SetData(spheres);
    }

    private void SetShaderParameters()
    {
        RayTracingShader.SetTexture(0, "_SkyboxTexture", SkyboxTexture);
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        RayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
        RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));

        Vector3 l = dirLight.transform.forward;
        RayTracingShader.SetVector("_DirectionalLight", new Vector4(l.x, l.y, l.z, dirLight.intensity));
        RayTracingShader.SetBuffer(0, "_Spheres", _sphereBuffer); //set the buffer for the spheres in the shader
    }

    /// <summary>
    /// The  OnRenderImage function is automatically called by Unity whenever the camera has finished rendering. 
    /// To render, we first create a render target of appropriate dimensions and tell the compute shader about it. 
    /// The 0 is the index of the compute shader’s kernel function – we have only one.
    /// </summary>

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        SetShaderParameters();

        Render(destination);
    }


    /// <summary>
    /// Next, we dispatch the shader. 
    /// This means that we are telling the GPU to get busy with a number of thread groups executing our shader code. 
    /// Each thread group consists of a number of threads which is set in the shader itself. 
    /// </summary>

    private void Render(RenderTexture destination)
    {
        //Make sure we have a current render target
        InitRenderTexture();

        //Set the target and dispatch the compute shader
        RayTracingShader.SetTexture(0, "Result", _target);
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        //Blit the result texture to the screen
        if (_addMaterial == null)
            _addMaterial = new Material(Shader.Find("Raytracing / Anti - Aliasing"));

        _addMaterial.SetFloat("_Sample", _currentSample);
        Graphics.Blit(_target, destination, _addMaterial);
        _currentSample++;
    }

    private void InitRenderTexture()
    {
        if (_target == null || _target.width!= Screen.width || _target.height != Screen.height)
        {
            //Release render texture if we already have one
            if (_target != null)
            {
                _target.Release();
            }

            _currentSample = 0;

            //Get a render target for Ray Tracing
            _target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _target.enableRandomWrite = true;
            _target.Create();
        }
    }

    private void Update()
    {
        if (transform.hasChanged)
        {
            _currentSample = 0;
            transform.hasChanged = false;
        }
        if (dirLight.transform.hasChanged)
        {
            _currentSample = 0;
            dirLight.transform.hasChanged = false;
        }

        if (Input.GetKey(KeyCode.R))
        {
            if (_sphereBuffer != null)
                _sphereBuffer.Release(); //release the buffer for garbage collection
            SetUpScene();
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            if (!isLightMoving)
            {
                isLightMoving = true;
            }
            else
            {
                isLightMoving = false;
            }

        }

        if (Input.GetKey(KeyCode.Escape))
        {
            Application.Quit();
        }

        if (isLightMoving)
        {

            currentTimeOfDay += (Time.deltaTime / secondsInFullDay) * timeMultiplier;

            if (currentTimeOfDay >= 1)
            {
                currentTimeOfDay = 0;
            }

            MoveLight();
        }
    }

    void MoveLight()
    {
        dirLight.transform.localRotation = Quaternion.Euler((currentTimeOfDay * 360f) - 90, 170, 0);

        float intensityMultiplier = 1;
        if (currentTimeOfDay <= 0.23f || currentTimeOfDay >= 0.75f)
        {
            intensityMultiplier = 0;
        }
        else if (currentTimeOfDay <= 0.25f)
        {
            intensityMultiplier = Mathf.Clamp01((currentTimeOfDay - 0.23f) * (1 / 0.02f));
        }
        else if (currentTimeOfDay >= 0.73f)
        {
            intensityMultiplier = Mathf.Clamp01(1 - ((currentTimeOfDay - 0.73f) * (1 / 0.02f)));
        }

        dirLight.intensity = dirLightIntensity * intensityMultiplier;
    }
}
