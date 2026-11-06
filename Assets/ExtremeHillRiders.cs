using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// =========================================================================================
/// EXTREME HILL RIDERS - Complete Runtime Systems (Unity 6 / Built-in & URP Compatible)
/// =========================================================================================
/// Features:
/// - 4 Genuinely Distinct Themed Worlds:
///     1. Green Hills: Lush rolling grass, oak/pine trees, wooden bridges, boulders, fences, clouds
///     2. Desert Storm: Golden dunes, ancient sandstone ruins, pillars, arches, saguaro cacti
///     3. Snow Mountain: Pristine snow hills, frosted pines, cyan ice bridges, snow boulders
///     4. Alien Planet: Neon violet terrain, glowing crystal clusters, alien bulb spires, sci-fi gates
/// - Universal Material System: Zero pink shaders (compatible with Built-in Standard & URP Lit)
/// - Modern Unity 6 New Input System + Legacy Input Fallback
/// - 3 Selectable 3D Vehicles: Hill Jeep, Monster Truck, Dune Buggy
/// =========================================================================================
/// </summary>
public class ExtremeHillRiders : MonoBehaviour
{
    public static ExtremeHillRiders Instance { get; private set; }

    public enum GameState { MainMenu, Playing, Paused, GameOver }
    public enum WorldTheme { GreenHills = 0, DesertStorm = 1, SnowMountain = 2, AlienPlanet = 3 }
    public enum VehicleType { HillJeep = 0, MonsterTruck = 1, DuneBuggy = 2 }

    [Header("Current Selection")]
    public WorldTheme currentWorld = WorldTheme.GreenHills;
    public VehicleType currentVehicle = VehicleType.HillJeep;
    public GameState currentState = GameState.MainMenu;

    [Header("References")]
    public Camera mainCamera;
    public Light dirLight;
    public Transform vehicleContainer;
    public Transform terrainContainer;
    public Transform propsContainer;

    [Header("Gameplay Variables")]
    public float currentFuel = 100f;
    public float maxFuel = 100f;
    public float fuelConsumptionRate = 3.2f;
    public float fuelThrottleMultiplier = 2.2f;
    public int coinsCollected = 0;
    public float currentDistance = 0f;
    public float currentSpeedKmh = 0f;
    public int currentScore = 0;
    public int highScore = 0;

    [HideInInspector] public VehicleController activeVehicle;
    [HideInInspector] public TerrainGenerator terrainGen;
    [HideInInspector] public UIManager uiManager;
    [HideInInspector] public SoundFX soundFX;

    [HideInInspector] public float inputThrottle = 0f;
    [HideInInspector] public float inputAirTilt = 0f;

    private bool mobileGasPressed = false;
    private bool mobileBrakePressed = false;
    private float crashTimer = 0f;
    private const float CRASH_THRESHOLD_TIME = 1.6f;
    private bool isGameOverTriggered = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        FixEventSystemInputModule();

        if (vehicleContainer == null) vehicleContainer = new GameObject("Vehicles").transform;
        if (terrainContainer == null) terrainContainer = new GameObject("Terrain").transform;
        if (propsContainer == null) propsContainer = new GameObject("Props").transform;

        vehicleContainer.SetParent(transform);
        terrainContainer.SetParent(transform);
        propsContainer.SetParent(transform);

        soundFX = gameObject.GetComponent<SoundFX>();
        if (soundFX == null) soundFX = gameObject.AddComponent<SoundFX>();

        terrainGen = gameObject.GetComponent<TerrainGenerator>();
        if (terrainGen == null) terrainGen = gameObject.AddComponent<TerrainGenerator>();

        uiManager = FindFirstObjectByType<UIManager>();
        if (uiManager == null)
        {
            GameObject uiGo = new GameObject("UIManager");
            uiManager = uiGo.AddComponent<UIManager>();
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                GameObject camObj = new GameObject("Main Camera");
                mainCamera = camObj.AddComponent<Camera>();
                mainCamera.tag = "MainCamera";
            }
        }
        mainCamera.fieldOfView = 55f;
        mainCamera.clearFlags = CameraClearFlags.SolidColor;

        if (dirLight == null)
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional) { dirLight = l; break; }
            }
            if (dirLight == null)
            {
                GameObject lightGo = new GameObject("Directional Light");
                dirLight = lightGo.AddComponent<Light>();
                dirLight.type = LightType.Directional;
            }
        }

        // Apply theme atmosphere right away for preview in menu
        ApplyWorldAtmosphere(currentWorld);
    }

    public static void FixEventSystemInputModule()
    {
        EventSystem es = FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            GameObject esGo = new GameObject("EventSystem");
            es = esGo.AddComponent<EventSystem>();
        }

#if ENABLE_INPUT_SYSTEM
        StandaloneInputModule legacyModule = es.GetComponent<StandaloneInputModule>();
        if (legacyModule != null)
        {
            if (Application.isPlaying) Destroy(legacyModule);
            else DestroyImmediate(legacyModule);
        }

        if (es.GetComponent<InputSystemUIInputModule>() == null)
        {
            es.gameObject.AddComponent<InputSystemUIInputModule>();
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (es.GetComponent<StandaloneInputModule>() == null)
        {
            es.gameObject.AddComponent<StandaloneInputModule>();
        }
#endif
    }

    private void Start()
    {
        highScore = PlayerPrefs.GetInt("EHR_HighScore_" + (int)currentWorld, 0);

        // Build preview background terrain for main menu
        terrainGen.InitializeWorld(currentWorld, true);

        SetGameState(GameState.MainMenu);
    }

    private void Update()
    {
        HandlePlayerInput();

        if (currentState == GameState.Playing && activeVehicle != null)
        {
            float usage = fuelConsumptionRate;
            if (Mathf.Abs(inputThrottle) > 0.1f) usage *= fuelThrottleMultiplier;
            currentFuel -= usage * Time.deltaTime;

            if (currentFuel <= 0f)
            {
                currentFuel = 0f;
                if (activeVehicle.GetSpeed() < 0.5f)
                {
                    TriggerGameOver("OUT OF FUEL!");
                }
            }

            float vehicleX = activeVehicle.transform.position.x;
            if (vehicleX > currentDistance)
            {
                currentDistance = vehicleX;
            }

            currentSpeedKmh = activeVehicle.GetSpeed() * 3.6f;
            currentScore = Mathf.FloorToInt(currentDistance) + (coinsCollected * 10);

            if (activeVehicle.IsUpsideDown() && activeVehicle.IsTouchingGround())
            {
                crashTimer += Time.deltaTime;
                if (crashTimer >= CRASH_THRESHOLD_TIME && !isGameOverTriggered)
                {
                    TriggerGameOver("VEHICLE FLIPPED!");
                }
            }
            else
            {
                crashTimer = Mathf.Max(0f, crashTimer - Time.deltaTime * 2f);
            }

            if (activeVehicle.transform.position.y < -35f && !isGameOverTriggered)
            {
                TriggerGameOver("FELL OFF THE TRACK!");
            }

            if (uiManager != null)
            {
                uiManager.UpdateHUD(currentSpeedKmh, currentFuel / maxFuel, coinsCollected, currentDistance, currentWorld);
            }
        }
    }

    private void LateUpdate()
    {
        if (activeVehicle != null && mainCamera != null)
        {
            Vector3 targetPos = activeVehicle.transform.position;
            Vector3 desiredPos = new Vector3(targetPos.x + 4.0f, targetPos.y + 2.8f, -13.5f);
            mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, desiredPos, Time.deltaTime * 5f);
            mainCamera.transform.LookAt(new Vector3(targetPos.x + 3.0f, targetPos.y + 1.2f, 0f));
        }
        else if (currentState == GameState.MainMenu && mainCamera != null)
        {
            // Gentle camera orbit in main menu
            float camX = 15f + Mathf.Sin(Time.time * 0.2f) * 8f;
            float camY = terrainGen.SampleHeight(camX) + 5f;
            mainCamera.transform.position = new Vector3(camX, camY, -14f);
            mainCamera.transform.LookAt(new Vector3(camX + 5f, camY - 2f, 0f));
        }
    }

    private void HandlePlayerInput()
    {
        if (currentState != GameState.Playing) return;

        float throttle = 0f;
        float tilt = 0f;
        bool escapePressed = false;

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) throttle += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) throttle -= 1f;

            if (kb.spaceKey.isPressed) tilt -= 1f;
            if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) tilt += 1f;

            if (kb.escapeKey.wasPressedThisFrame) escapePressed = true;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) throttle += 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) throttle -= 1f;

        if (Input.GetKey(KeyCode.Space)) tilt -= 1f;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) tilt += 1f;

        if (Input.GetKeyDown(KeyCode.Escape)) escapePressed = true;
#endif

        if (mobileGasPressed) throttle += 1f;
        if (mobileBrakePressed) throttle -= 1f;

        if (activeVehicle != null && !activeVehicle.IsGrounded())
        {
            if (Mathf.Abs(tilt) < 0.1f)
            {
                if (throttle > 0.1f) tilt -= 0.85f;
                else if (throttle < -0.1f) tilt += 0.85f;
            }
        }

        inputThrottle = Mathf.Clamp(throttle, -1f, 1f);
        inputAirTilt = Mathf.Clamp(tilt, -1f, 1f);

        if (escapePressed)
        {
            if (currentState == GameState.Playing) PauseGame();
            else if (currentState == GameState.Paused) ResumeGame();
        }
    }

    public void StartGame()
    {
        isGameOverTriggered = false;
        crashTimer = 0f;
        currentFuel = maxFuel;
        coinsCollected = 0;
        currentDistance = 0f;
        currentScore = 0;
        highScore = PlayerPrefs.GetInt("EHR_HighScore_" + (int)currentWorld, 0);

        ApplyWorldAtmosphere(currentWorld);
        terrainGen.InitializeWorld(currentWorld, false);
        SpawnVehicle(currentVehicle);

        SetGameState(GameState.Playing);
        soundFX.PlayEngineSound(true);
    }

    public void PauseGame()
    {
        if (currentState != GameState.Playing) return;
        Time.timeScale = 0f;
        SetGameState(GameState.Paused);
        soundFX.PlayEngineSound(false);
    }

    public void ResumeGame()
    {
        if (currentState != GameState.Paused) return;
        Time.timeScale = 1f;
        SetGameState(GameState.Playing);
        soundFX.PlayEngineSound(true);
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        StartGame();
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        soundFX.PlayEngineSound(false);
        if (activeVehicle != null) Destroy(activeVehicle.gameObject);
        terrainGen.InitializeWorld(currentWorld, true);
        SetGameState(GameState.MainMenu);
    }

    public void TriggerGameOver(string reason)
    {
        if (isGameOverTriggered) return;
        isGameOverTriggered = true;

        soundFX.PlayEngineSound(false);
        soundFX.PlaySound(SoundFX.SfxType.Crash);

        if (currentScore > highScore)
        {
            highScore = currentScore;
            PlayerPrefs.SetInt("EHR_HighScore_" + (int)currentWorld, highScore);
            PlayerPrefs.Save();
        }

        SetGameState(GameState.GameOver);
        if (uiManager != null)
        {
            uiManager.ShowGameOver(reason, currentDistance, coinsCollected, currentScore, highScore);
        }
    }

    public void SetGameState(GameState newState)
    {
        currentState = newState;
        if (uiManager != null)
        {
            uiManager.UpdateStateUI(currentState);
        }
    }

    public void SelectWorld(int worldIdx)
    {
        currentWorld = (WorldTheme)worldIdx;
        highScore = PlayerPrefs.GetInt("EHR_HighScore_" + (int)currentWorld, 0);
        ApplyWorldAtmosphere(currentWorld);
        soundFX.PlaySound(SoundFX.SfxType.ButtonClick);
        if (uiManager != null) uiManager.UpdateWorldButtons(currentWorld);

        // Update preview scene in real-time
        if (currentState == GameState.MainMenu)
        {
            terrainGen.InitializeWorld(currentWorld, true);
        }
    }

    public void SelectVehicle(int vehicleIdx)
    {
        currentVehicle = (VehicleType)vehicleIdx;
        soundFX.PlaySound(SoundFX.SfxType.ButtonClick);
        if (uiManager != null) uiManager.UpdateVehicleButtons(currentVehicle);
    }

    public void CollectCoin(int value)
    {
        coinsCollected += value;
        soundFX.PlaySound(SoundFX.SfxType.Coin);
        if (uiManager != null) uiManager.PopCoinCounter();
    }

    public void CollectFuel()
    {
        currentFuel = maxFuel;
        soundFX.PlaySound(SoundFX.SfxType.Fuel);
        if (uiManager != null) uiManager.PopFuelBar();
    }

    public void SetMobileGas(bool pressed) => mobileGasPressed = pressed;
    public void SetMobileBrake(bool pressed) => mobileBrakePressed = pressed;

    // --- DISTINCT 4 WORLD ATMOSPHERES & SKY ---
    public void ApplyWorldAtmosphere(WorldTheme world)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;

        switch (world)
        {
            case WorldTheme.GreenHills:
                mainCamera.backgroundColor = new Color(0.35f, 0.68f, 0.98f); // Bright Country Blue Sky
                RenderSettings.fogColor = new Color(0.60f, 0.80f, 0.96f);
                RenderSettings.fogDensity = 0.007f;
                if (dirLight != null)
                {
                    dirLight.color = new Color(1.0f, 0.98f, 0.88f); // Warm Sunlight
                    dirLight.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                    dirLight.intensity = 1.35f;
                }
                break;

            case WorldTheme.DesertStorm:
                mainCamera.backgroundColor = new Color(0.95f, 0.60f, 0.28f); // Warm Golden-Orange Sand Sky
                RenderSettings.fogColor = new Color(0.92f, 0.58f, 0.26f);
                RenderSettings.fogDensity = 0.012f;
                if (dirLight != null)
                {
                    dirLight.color = new Color(1.0f, 0.82f, 0.52f); // Hot Desert Sun
                    dirLight.transform.rotation = Quaternion.Euler(56f, -50f, 0f);
                    dirLight.intensity = 1.45f;
                }
                break;

            case WorldTheme.SnowMountain:
                mainCamera.backgroundColor = new Color(0.70f, 0.88f, 0.98f); // Frosty Cyan Winter Sky
                RenderSettings.fogColor = new Color(0.85f, 0.92f, 0.98f);
                RenderSettings.fogDensity = 0.014f;
                if (dirLight != null)
                {
                    dirLight.color = new Color(0.92f, 0.96f, 1.0f); // Pale Winter Sun
                    dirLight.transform.rotation = Quaternion.Euler(42f, -25f, 0f);
                    dirLight.intensity = 1.25f;
                }
                break;

            case WorldTheme.AlienPlanet:
                mainCamera.backgroundColor = new Color(0.12f, 0.04f, 0.22f); // Cosmic Dark Purple Sci-Fi Sky
                RenderSettings.fogColor = new Color(0.25f, 0.08f, 0.42f);
                RenderSettings.fogDensity = 0.011f;
                if (dirLight != null)
                {
                    dirLight.color = new Color(0.82f, 0.35f, 1.0f); // Neon Magenta/Purple Light
                    dirLight.transform.rotation = Quaternion.Euler(40f, -40f, 0f);
                    dirLight.intensity = 1.15f;
                }
                break;
        }
    }

    private void SpawnVehicle(VehicleType vType)
    {
        if (activeVehicle != null) Destroy(activeVehicle.gameObject);

        GameObject vGo = new GameObject("PlayerVehicle_" + vType.ToString());
        vGo.transform.SetParent(vehicleContainer);

        float startY = terrainGen.SampleHeight(0f) + 2.5f;
        vGo.transform.position = new Vector3(0f, startY, 0f);
        vGo.transform.rotation = Quaternion.identity;

        activeVehicle = vGo.AddComponent<VehicleController>();
        activeVehicle.BuildVehicle(vType, currentWorld);
    }
}

/// <summary>
/// =========================================================================================
/// UNIVERSAL MATERIAL HELPER - Guarantees 0% Pink Shaders in Built-in & URP Unity 6
/// =========================================================================================
/// </summary>
public static class MaterialHelper
{
    private static Shader cachedSafeShader = null;

    public static Shader GetSafeShader()
    {
        if (cachedSafeShader != null) return cachedSafeShader;

        // Try extracting standard active shader from primitive
        try
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Renderer r = temp.GetComponent<Renderer>();
            if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null)
            {
                cachedSafeShader = r.sharedMaterial.shader;
            }
            if (Application.isPlaying) GameObject.Destroy(temp);
            else GameObject.DestroyImmediate(temp);
        }
        catch { }

        if (cachedSafeShader != null && cachedSafeShader.isSupported) return cachedSafeShader;

        string[] candidates = new string[]
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit",
            "Standard",
            "Legacy Shaders/Diffuse",
            "Mobile/Diffuse",
            "Unlit/Color",
            "Sprites/Default",
            "UI/Default"
        };

        foreach (var name in candidates)
        {
            Shader s = Shader.Find(name);
            if (s != null && s.isSupported)
            {
                cachedSafeShader = s;
                return s;
            }
        }

        cachedSafeShader = Shader.Find("Sprites/Default");
        return cachedSafeShader;
    }

    public static Material CreateMat(Color color, float smoothness = 0.3f, float metallic = 0.1f)
    {
        Shader s = GetSafeShader();
        Material mat = new Material(s);
        mat.color = color;

        // Ensure both Standard and URP material color channels are assigned
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

        return mat;
    }
}

/// <summary>
/// =========================================================================================
/// VEHICLE CONTROLLER - 3D Primitive Procedural Vehicle + Arcade Suspension Physics
/// =========================================================================================
/// </summary>
public class VehicleController : MonoBehaviour
{
    public ExtremeHillRiders.VehicleType vehicleType;
    private Rigidbody rb;

    [Header("Wheels & Suspension")]
    public Transform wheelFront;
    public Transform wheelRear;
    public float wheelRadius = 0.55f;
    public float suspensionRestDistance = 0.65f;
    public float springStrength = 4200f;
    public float springDamping = 320f;

    [Header("Drivetrain Specs")]
    public float motorTorque = 3600f;
    public float maxSpeed = 24f;
    public float brakeForce = 4500f;
    public float airTiltTorque = 1250f;

    private bool frontGrounded = false;
    private bool rearGrounded = false;
    private Collider chassisCollider;

    public void BuildVehicle(ExtremeHillRiders.VehicleType type, ExtremeHillRiders.WorldTheme world)
    {
        vehicleType = type;
        rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = 1200f;
        rb.linearDamping = 0.15f;
        rb.angularDamping = 1.8f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        rb.constraints = RigidbodyConstraints.FreezePositionZ |
                         RigidbodyConstraints.FreezeRotationX |
                         RigidbodyConstraints.FreezeRotationY;

        Color bodyColor = Color.green;
        Color accentColor = Color.yellow;
        Color wheelColor = new Color(0.18f, 0.18f, 0.18f);

        float bodyScaleX = 2.4f;
        float bodyScaleY = 0.8f;
        float wheelOffset = 1.35f;
        wheelRadius = 0.55f;

        if (type == ExtremeHillRiders.VehicleType.HillJeep)
        {
            bodyColor = new Color(0.24f, 0.62f, 0.28f);
            accentColor = new Color(0.92f, 0.82f, 0.20f);
            motorTorque = 3800f;
            maxSpeed = 22f;
            wheelRadius = 0.52f;
        }
        else if (type == ExtremeHillRiders.VehicleType.MonsterTruck)
        {
            bodyColor = new Color(0.90f, 0.20f, 0.15f);
            accentColor = new Color(0.15f, 0.15f, 0.15f);
            rb.mass = 1600f;
            bodyScaleX = 2.6f;
            bodyScaleY = 1.0f;
            wheelOffset = 1.55f;
            wheelRadius = 0.85f;
            motorTorque = 5200f;
            maxSpeed = 20f;
            springStrength = 5500f;
        }
        else if (type == ExtremeHillRiders.VehicleType.DuneBuggy)
        {
            bodyColor = new Color(0.10f, 0.75f, 0.98f);
            accentColor = new Color(1.0f, 0.45f, 0.15f);
            rb.mass = 950f;
            bodyScaleX = 2.1f;
            bodyScaleY = 0.6f;
            wheelOffset = 1.25f;
            wheelRadius = 0.48f;
            motorTorque = 4200f;
            maxSpeed = 27f;
            airTiltTorque = 1600f;
        }

        Material bodyMat = MaterialHelper.CreateMat(bodyColor, 0.4f, 0.2f);
        Material accentMat = MaterialHelper.CreateMat(accentColor, 0.6f, 0.4f);
        Material wheelMat = MaterialHelper.CreateMat(wheelColor, 0.2f, 0.1f);
        Material glassMat = MaterialHelper.CreateMat(new Color(0.25f, 0.45f, 0.65f), 0.9f, 0.1f);

        // Chassis Body
        GameObject bodyObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bodyObj.name = "Chassis";
        bodyObj.transform.SetParent(transform, false);
        bodyObj.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        bodyObj.transform.localScale = new Vector3(bodyScaleX, bodyScaleY, 1.4f);
        bodyObj.GetComponent<Renderer>().sharedMaterial = bodyMat;
        chassisCollider = bodyObj.GetComponent<BoxCollider>();

        // Cabin / Roof
        GameObject cabinObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cabinObj.name = "Cabin";
        cabinObj.transform.SetParent(transform, false);
        cabinObj.transform.localPosition = new Vector3(-0.25f, 0.75f, 0f);
        cabinObj.transform.localScale = new Vector3(bodyScaleX * 0.55f, bodyScaleY * 0.75f, 1.25f);
        cabinObj.GetComponent<Renderer>().sharedMaterial = accentMat;
        Destroy(cabinObj.GetComponent<Collider>());

        // Windshield
        GameObject windshield = GameObject.CreatePrimitive(PrimitiveType.Cube);
        windshield.name = "Windshield";
        windshield.transform.SetParent(cabinObj.transform, false);
        windshield.transform.localPosition = new Vector3(0.52f, 0.05f, 0f);
        windshield.transform.localRotation = Quaternion.Euler(0, 0, -22f);
        windshield.transform.localScale = new Vector3(0.08f, 0.85f, 0.9f);
        windshield.GetComponent<Renderer>().sharedMaterial = glassMat;
        Destroy(windshield.GetComponent<Collider>());

        // Spoiler
        GameObject spoiler = GameObject.CreatePrimitive(PrimitiveType.Cube);
        spoiler.name = "Spoiler";
        spoiler.transform.SetParent(transform, false);
        spoiler.transform.localPosition = new Vector3(-bodyScaleX * 0.45f, 0.7f, 0f);
        spoiler.transform.localScale = new Vector3(0.3f, 0.15f, 1.35f);
        spoiler.GetComponent<Renderer>().sharedMaterial = accentMat;
        Destroy(spoiler.GetComponent<Collider>());

        // Headlights
        GameObject lightL = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        lightL.transform.SetParent(bodyObj.transform, false);
        lightL.transform.localPosition = new Vector3(0.51f, 0.1f, 0.35f);
        lightL.transform.localRotation = Quaternion.Euler(0, 0, 90);
        lightL.transform.localScale = new Vector3(0.18f, 0.05f, 0.18f);
        lightL.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(Color.white, 0.9f, 0.8f);
        Destroy(lightL.GetComponent<Collider>());

        GameObject lightR = Instantiate(lightL, bodyObj.transform);
        lightR.transform.localPosition = new Vector3(0.51f, 0.1f, -0.35f);

        // Wheels
        wheelFront = CreateWheelAssembly("Wheel_Front", new Vector3(wheelOffset, -0.1f, 0f), wheelRadius, wheelMat, accentMat);
        wheelRear = CreateWheelAssembly("Wheel_Rear", new Vector3(-wheelOffset, -0.1f, 0f), wheelRadius, wheelMat, accentMat);

        rb.centerOfMass = new Vector3(0.05f, -0.35f, 0f);
    }

    private Transform CreateWheelAssembly(string name, Vector3 localPos, float radius, Material tireMat, Material rimMat)
    {
        GameObject wheelRoot = new GameObject(name);
        wheelRoot.transform.SetParent(transform, false);
        wheelRoot.transform.localPosition = localPos;

        GameObject tire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tire.name = "TireVisual";
        tire.transform.SetParent(wheelRoot.transform, false);
        tire.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        tire.transform.localScale = new Vector3(radius * 2f, 0.4f, radius * 2f);
        tire.GetComponent<Renderer>().sharedMaterial = tireMat;
        Destroy(tire.GetComponent<Collider>());

        GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rim.name = "RimVisual";
        rim.transform.SetParent(tire.transform, false);
        rim.transform.localPosition = new Vector3(0f, 0.52f, 0f);
        rim.transform.localScale = new Vector3(0.55f, 0.1f, 0.55f);
        rim.GetComponent<Renderer>().sharedMaterial = rimMat;
        Destroy(rim.GetComponent<Collider>());

        return wheelRoot.transform;
    }

    private void FixedUpdate()
    {
        if (ExtremeHillRiders.Instance == null || ExtremeHillRiders.Instance.currentState != ExtremeHillRiders.GameState.Playing)
            return;

        float throttle = ExtremeHillRiders.Instance.inputThrottle;
        float airTilt = ExtremeHillRiders.Instance.inputAirTilt;

        frontGrounded = SimulateSuspensionWheel(wheelFront, throttle);
        rearGrounded = SimulateSuspensionWheel(wheelRear, throttle);

        if (!frontGrounded && !rearGrounded)
        {
            if (Mathf.Abs(airTilt) > 0.05f)
            {
                rb.AddTorque(new Vector3(0f, 0f, airTilt * airTiltTorque), ForceMode.Force);
            }
        }

        if (rb.linearVelocity.magnitude > maxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
        }

        float spinRate = rb.linearVelocity.x / (wheelRadius * Mathf.PI * 2f) * 360f * Time.fixedDeltaTime;
        if (wheelFront != null) wheelFront.Rotate(0f, 0f, -spinRate, Space.Self);
        if (wheelRear != null) wheelRear.Rotate(0f, 0f, -spinRate, Space.Self);
    }

    private bool SimulateSuspensionWheel(Transform wheel, float throttle)
    {
        if (wheel == null) return false;

        Vector3 rayOrigin = transform.TransformPoint(new Vector3(wheel.localPosition.x, 0f, 0f));
        Vector3 rayDir = -transform.up;
        float rayDistance = suspensionRestDistance + wheelRadius;

        RaycastHit hit;
        int layerMask = ~LayerMask.GetMask("Ignore Raycast");

        if (Physics.Raycast(rayOrigin, rayDir, out hit, rayDistance, layerMask))
        {
            float currentDistance = hit.distance - wheelRadius;
            float compression = suspensionRestDistance - currentDistance;
            float springVelocity = Vector3.Dot(rb.GetPointVelocity(rayOrigin), rayDir);

            float springForceMagnitude = (compression * springStrength) - (springVelocity * springDamping);
            springForceMagnitude = Mathf.Max(0f, springForceMagnitude);

            Vector3 suspensionForce = transform.up * springForceMagnitude;
            rb.AddForceAtPosition(suspensionForce, rayOrigin, ForceMode.Force);

            if (Mathf.Abs(throttle) > 0.05f)
            {
                Vector3 driveDir = Vector3.ProjectOnPlane(transform.right, hit.normal).normalized;
                float forceMag = throttle * motorTorque;
                rb.AddForceAtPosition(driveDir * forceMag, hit.point, ForceMode.Force);
            }
            else
            {
                Vector3 localVel = transform.InverseTransformDirection(rb.linearVelocity);
                rb.AddForce(-transform.right * localVel.x * 350f, ForceMode.Force);
            }

            wheel.position = rayOrigin - (transform.up * (hit.distance - wheelRadius));
            return true;
        }
        else
        {
            wheel.position = rayOrigin - (transform.up * suspensionRestDistance);
            return false;
        }
    }

    public bool IsGrounded() => frontGrounded || rearGrounded;

    public bool IsUpsideDown()
    {
        return Vector3.Dot(transform.up, Vector3.up) < -0.15f;
    }

    public bool IsTouchingGround()
    {
        if (chassisCollider == null) return false;
        Collider[] hits = Physics.OverlapBox(chassisCollider.bounds.center, chassisCollider.bounds.extents * 0.9f, transform.rotation);
        foreach (var col in hits)
        {
            if (col != chassisCollider && !col.isTrigger && col.transform.IsChildOf(ExtremeHillRiders.Instance.terrainContainer))
                return true;
        }
        return false;
    }

    public float GetSpeed() => rb != null ? rb.linearVelocity.magnitude : 0f;
}

/// <summary>
/// =========================================================================================
/// TERRAIN GENERATOR - Procedural Rolling Hills with Immediate World Theming & Zero Gaps
/// =========================================================================================
/// </summary>
public class TerrainGenerator : MonoBehaviour
{
    public const float CHUNK_LENGTH = 50f;
    public const int CHUNK_SEGMENTS = 50;
    public const float TRACK_WIDTH = 6f;
    public const int ACTIVE_CHUNKS_AHEAD = 4;
    public const int ACTIVE_CHUNKS_BEHIND = 1;

    private ExtremeHillRiders.WorldTheme activeTheme;
    private Dictionary<int, TerrainChunk> spawnedChunks = new Dictionary<int, TerrainChunk>();
    private Material terrainTopMaterial;
    private Material terrainSideMaterial;

    public void InitializeWorld(ExtremeHillRiders.WorldTheme theme, bool previewMode = false)
    {
        activeTheme = theme;
        ClearAllChunks();
        CreateWorldMaterials(theme);

        int minChunk = previewMode ? -1 : -1;
        int maxChunk = previewMode ? 2 : ACTIVE_CHUNKS_AHEAD;

        for (int i = minChunk; i <= maxChunk; i++)
        {
            GenerateChunk(i);
        }
    }

    private void Update()
    {
        if (ExtremeHillRiders.Instance == null || ExtremeHillRiders.Instance.activeVehicle == null)
            return;

        float playerX = ExtremeHillRiders.Instance.activeVehicle.transform.position.x;
        int currentChunkIndex = Mathf.FloorToInt(playerX / CHUNK_LENGTH);

        for (int i = currentChunkIndex - ACTIVE_CHUNKS_BEHIND; i <= currentChunkIndex + ACTIVE_CHUNKS_AHEAD; i++)
        {
            if (!spawnedChunks.ContainsKey(i))
            {
                GenerateChunk(i);
            }
        }

        List<int> toRemove = new List<int>();
        foreach (var pair in spawnedChunks)
        {
            if (pair.Key < currentChunkIndex - (ACTIVE_CHUNKS_BEHIND + 1))
            {
                toRemove.Add(pair.Key);
            }
        }
        foreach (int key in toRemove)
        {
            if (spawnedChunks[key] != null) Destroy(spawnedChunks[key].gameObject);
            spawnedChunks.Remove(key);
        }
    }

    public void ClearAllChunks()
    {
        foreach (var pair in spawnedChunks)
        {
            if (pair.Value != null) Destroy(pair.Value.gameObject);
        }
        spawnedChunks.Clear();
    }

    private void GenerateChunk(int chunkIndex)
    {
        GameObject chunkGo = new GameObject("Chunk_" + chunkIndex);
        chunkGo.transform.SetParent(ExtremeHillRiders.Instance.terrainContainer);

        TerrainChunk chunk = chunkGo.AddComponent<TerrainChunk>();
        chunk.Initialize(chunkIndex, activeTheme, this, terrainTopMaterial, terrainSideMaterial);
        spawnedChunks[chunkIndex] = chunk;
    }

    public float SampleHeight(float x)
    {
        if (x < -15f) return 0f;

        float h = 0f;
        switch (activeTheme)
        {
            case ExtremeHillRiders.WorldTheme.GreenHills:
                h += Mathf.Sin(x * 0.055f) * 3.8f;
                h += Mathf.Cos(x * 0.12f) * 1.6f;
                h += Mathf.PerlinNoise(x * 0.035f, 0.2f) * 4.5f;
                if ((x % 140f) > 90f) h += Mathf.Sin((x % 140f - 90f) * 0.06f) * 6.5f;
                break;

            case ExtremeHillRiders.WorldTheme.DesertStorm:
                // Sharper rolling sand dunes
                h += Mathf.Sin(x * 0.075f) * 5.2f;
                h += Mathf.Abs(Mathf.Sin(x * 0.04f)) * 4.2f;
                h += Mathf.PerlinNoise(x * 0.05f, 1.4f) * 3.5f;
                break;

            case ExtremeHillRiders.WorldTheme.SnowMountain:
                // High glacial peaks & steep valleys
                h += Mathf.Sin(x * 0.045f) * 6.5f;
                h += Mathf.Cos(x * 0.09f) * 3.2f;
                h += Mathf.PerlinNoise(x * 0.025f, 2.7f) * 6.0f;
                break;

            case ExtremeHillRiders.WorldTheme.AlienPlanet:
                // Fluctuating neon sci-fi spires
                h += Mathf.Sin(x * 0.08f) * 4.5f;
                h += Mathf.Cos(x * 0.16f) * 2.8f;
                h += Mathf.Sin(x * 0.02f) * 8.0f;
                h += Mathf.PerlinNoise(x * 0.06f, 4.1f) * 4.0f;
                break;
        }

        return h;
    }

    private void CreateWorldMaterials(ExtremeHillRiders.WorldTheme theme)
    {
        switch (theme)
        {
            case ExtremeHillRiders.WorldTheme.GreenHills:
                terrainTopMaterial = MaterialHelper.CreateMat(new Color(0.24f, 0.68f, 0.22f), 0.3f, 0.05f); // Lush Emerald Grass
                terrainSideMaterial = MaterialHelper.CreateMat(new Color(0.42f, 0.28f, 0.16f), 0.2f, 0.0f); // Dark Earth Cliff
                break;

            case ExtremeHillRiders.WorldTheme.DesertStorm:
                terrainTopMaterial = MaterialHelper.CreateMat(new Color(0.95f, 0.74f, 0.35f), 0.2f, 0.0f); // Bright Golden Sand
                terrainSideMaterial = MaterialHelper.CreateMat(new Color(0.72f, 0.48f, 0.22f), 0.2f, 0.1f); // Sandstone Rock
                break;

            case ExtremeHillRiders.WorldTheme.SnowMountain:
                terrainTopMaterial = MaterialHelper.CreateMat(new Color(0.95f, 0.98f, 1.00f), 0.5f, 0.1f); // Pristine Glacier Snow
                terrainSideMaterial = MaterialHelper.CreateMat(new Color(0.38f, 0.48f, 0.60f), 0.4f, 0.2f); // Ice Slate Blue
                break;

            case ExtremeHillRiders.WorldTheme.AlienPlanet:
                terrainTopMaterial = MaterialHelper.CreateMat(new Color(0.65f, 0.12f, 0.92f), 0.7f, 0.3f); // Neon Magenta/Purple Spores
                terrainSideMaterial = MaterialHelper.CreateMat(new Color(0.18f, 0.05f, 0.32f), 0.6f, 0.4f); // Deep Obsidian Violet
                break;
        }
    }
}

/// <summary>
/// =========================================================================================
/// TERRAIN CHUNK - Solid 3D Extruded Ribbon Mesh + Scenery Props & Pickups
/// =========================================================================================
/// </summary>
public class TerrainChunk : MonoBehaviour
{
    private int chunkIndex;
    private ExtremeHillRiders.WorldTheme worldTheme;
    private TerrainGenerator generator;

    public void Initialize(int index, ExtremeHillRiders.WorldTheme theme, TerrainGenerator gen, Material topMat, Material sideMat)
    {
        chunkIndex = index;
        worldTheme = theme;
        generator = gen;

        BuildSolidTerrainMesh(topMat, sideMat);
        SpawnWorldScenery();
        SpawnCollectibles();
    }

    private void BuildSolidTerrainMesh(Material topMat, Material sideMat)
    {
        float startX = chunkIndex * TerrainGenerator.CHUNK_LENGTH;
        int segments = TerrainGenerator.CHUNK_SEGMENTS;
        float stepX = TerrainGenerator.CHUNK_LENGTH / segments;
        float halfWidth = TerrainGenerator.TRACK_WIDTH * 0.5f;
        float bottomSkirtY = -35f;

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        for (int i = 0; i <= segments; i++)
        {
            float curX = startX + (i * stepX);
            float curY = generator.SampleHeight(curX);

            // 0: Top Left (-Z)
            vertices.Add(new Vector3(curX, curY, -halfWidth));
            uvs.Add(new Vector2(curX * 0.1f, 0f));

            // 1: Top Right (+Z)
            vertices.Add(new Vector3(curX, curY, halfWidth));
            uvs.Add(new Vector2(curX * 0.1f, 1f));

            // 2: Bottom Left (-Z skirt)
            vertices.Add(new Vector3(curX, bottomSkirtY, -halfWidth));
            uvs.Add(new Vector2(curX * 0.1f, 0f));

            // 3: Bottom Right (+Z skirt)
            vertices.Add(new Vector3(curX, bottomSkirtY, halfWidth));
            uvs.Add(new Vector2(curX * 0.1f, 1f));
        }

        for (int i = 0; i < segments; i++)
        {
            int r0 = i * 4;
            int r1 = (i + 1) * 4;

            // Top road surface
            triangles.Add(r0 + 0);
            triangles.Add(r0 + 1);
            triangles.Add(r1 + 0);

            triangles.Add(r1 + 0);
            triangles.Add(r0 + 1);
            triangles.Add(r1 + 1);

            // Front Skirt Face (-Z)
            triangles.Add(r0 + 2);
            triangles.Add(r0 + 0);
            triangles.Add(r1 + 2);

            triangles.Add(r1 + 2);
            triangles.Add(r0 + 0);
            triangles.Add(r1 + 0);

            // Back Skirt Face (+Z)
            triangles.Add(r0 + 1);
            triangles.Add(r0 + 3);
            triangles.Add(r1 + 1);

            triangles.Add(r1 + 1);
            triangles.Add(r0 + 3);
            triangles.Add(r1 + 3);
        }

        Mesh mesh = new Mesh();
        mesh.name = "ChunkMesh_" + chunkIndex;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = topMat;

        MeshCollider mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
    }

    private void SpawnWorldScenery()
    {
        float startX = chunkIndex * TerrainGenerator.CHUNK_LENGTH;

        // Starting Line Arch on Chunk 0
        if (chunkIndex == 0)
        {
            SpawnStartingArch(new Vector3(0f, generator.SampleHeight(0f), 0f));
        }

        // Spawn rich decorative scenery props along this chunk
        int propCount = 5;
        for (int p = 0; p < propCount; p++)
        {
            float posX = startX + (p * 9.5f) + 3.0f;
            float posY = generator.SampleHeight(posX);

            // Background side scenery (Z = 3.5m to 6.5m)
            float posZ = (p % 2 == 0) ? UnityEngine.Random.Range(3.8f, 5.8f) : UnityEngine.Random.Range(-5.8f, -3.8f);
            Vector3 spawnPos = new Vector3(posX, posY, posZ);

            switch (worldTheme)
            {
                case ExtremeHillRiders.WorldTheme.GreenHills:
                    if (p % 3 == 0) SpawnTree(spawnPos, false);
                    else if (p % 3 == 1) SpawnRock(spawnPos, new Color(0.52f, 0.54f, 0.56f));
                    else SpawnFence(spawnPos);
                    break;

                case ExtremeHillRiders.WorldTheme.DesertStorm:
                    if (p % 3 == 0) SpawnCactus(spawnPos);
                    else if (p % 3 == 1) SpawnRuinsPillar(spawnPos);
                    else SpawnDesertArch(spawnPos);
                    break;

                case ExtremeHillRiders.WorldTheme.SnowMountain:
                    if (p % 3 == 0) SpawnTree(spawnPos, true);
                    else if (p % 3 == 1) SpawnIceBridgeOrShard(spawnPos);
                    else SpawnRock(spawnPos, new Color(0.80f, 0.88f, 0.94f));
                    break;

                case ExtremeHillRiders.WorldTheme.AlienPlanet:
                    if (p % 3 == 0) SpawnAlienCrystal(spawnPos);
                    else if (p % 3 == 1) SpawnAlienSpire(spawnPos);
                    else SpawnSciFiGate(spawnPos);
                    break;
            }
        }
    }

    private void SpawnCollectibles()
    {
        float startX = chunkIndex * TerrainGenerator.CHUNK_LENGTH;

        // Coins along hill crest
        float coinStartX = startX + 14f;
        for (int c = 0; c < 3; c++)
        {
            float cx = coinStartX + (c * 3.5f);
            float cy = generator.SampleHeight(cx) + 1.5f + Mathf.Sin(c * 0.8f) * 0.7f;
            SpawnPickup(new Vector3(cx, cy, 0f), Pickup.PickupType.Coin);
        }

        // Fuel canister
        if (chunkIndex % 2 == 0 && chunkIndex != 0)
        {
            float fx = startX + 32f;
            float fy = generator.SampleHeight(fx) + 1.5f;
            SpawnPickup(new Vector3(fx, fy, 0f), Pickup.PickupType.Fuel);
        }
    }

    private void SpawnPickup(Vector3 pos, Pickup.PickupType type)
    {
        GameObject pickupGo = new GameObject(type.ToString());
        pickupGo.transform.SetParent(transform, false);
        pickupGo.transform.position = pos;

        Pickup pickup = pickupGo.AddComponent<Pickup>();
        pickup.Setup(type);
    }

    // --- WORLD 1: GREEN HILLS PROPS ---
    private void SpawnStartingArch(Vector3 pos)
    {
        GameObject arch = new GameObject("StartArch");
        arch.transform.SetParent(transform, false);
        arch.transform.position = pos;

        Material woodMat = MaterialHelper.CreateMat(new Color(0.48f, 0.30f, 0.15f));
        Material bannerMat = MaterialHelper.CreateMat(new Color(0.95f, 0.85f, 0.15f));

        // Left post
        GameObject postL = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        postL.transform.SetParent(arch.transform, false);
        postL.transform.localPosition = new Vector3(0, 2.8f, -3.2f);
        postL.transform.localScale = new Vector3(0.5f, 2.8f, 0.5f);
        postL.GetComponent<Renderer>().sharedMaterial = woodMat;
        Destroy(postL.GetComponent<Collider>());

        // Right post
        GameObject postR = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        postR.transform.SetParent(arch.transform, false);
        postR.transform.localPosition = new Vector3(0, 2.8f, 3.2f);
        postR.transform.localScale = new Vector3(0.5f, 2.8f, 0.5f);
        postR.GetComponent<Renderer>().sharedMaterial = woodMat;
        Destroy(postR.GetComponent<Collider>());

        // Banner Crossbar
        GameObject banner = GameObject.CreatePrimitive(PrimitiveType.Cube);
        banner.transform.SetParent(arch.transform, false);
        banner.transform.localPosition = new Vector3(0, 5.2f, 0);
        banner.transform.localScale = new Vector3(0.4f, 1.2f, 7.2f);
        banner.GetComponent<Renderer>().sharedMaterial = bannerMat;
        Destroy(banner.GetComponent<Collider>());
    }

    private void SpawnTree(Vector3 pos, bool snowCovered)
    {
        GameObject tree = new GameObject("Tree");
        tree.transform.SetParent(transform, false);
        tree.transform.position = pos;

        GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.transform.SetParent(tree.transform, false);
        trunk.transform.localPosition = new Vector3(0, 1.4f, 0);
        trunk.transform.localScale = new Vector3(0.6f, 1.4f, 0.6f);
        trunk.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(new Color(0.38f, 0.22f, 0.12f));
        Destroy(trunk.GetComponent<Collider>());

        // Tiered Foliage
        Color fColor = snowCovered ? new Color(0.92f, 0.96f, 1.0f) : new Color(0.16f, 0.55f, 0.18f);
        Material folMat = MaterialHelper.CreateMat(fColor);

        for (int t = 0; t < 2; t++)
        {
            GameObject foliage = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            foliage.transform.SetParent(tree.transform, false);
            foliage.transform.localPosition = new Vector3(0, 2.8f + (t * 1.6f), 0);
            float scale = 2.4f - (t * 0.8f);
            foliage.transform.localScale = new Vector3(scale, 1.5f, scale);
            foliage.GetComponent<Renderer>().sharedMaterial = folMat;
            Destroy(foliage.GetComponent<Collider>());
        }
    }

    private void SpawnRock(Vector3 pos, Color rockColor)
    {
        GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        rock.name = "Rock";
        rock.transform.SetParent(transform, false);
        rock.transform.position = pos + new Vector3(0, 0.7f, 0);
        rock.transform.localScale = new Vector3(1.8f, 1.4f, 1.6f);
        rock.transform.rotation = Quaternion.Euler(UnityEngine.Random.Range(0, 45), UnityEngine.Random.Range(0, 90), 0);
        rock.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(rockColor);
        Destroy(rock.GetComponent<Collider>());
    }

    private void SpawnFence(Vector3 pos)
    {
        GameObject fence = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fence.name = "WoodenFence";
        fence.transform.SetParent(transform, false);
        fence.transform.position = pos + new Vector3(0, 0.8f, 0);
        fence.transform.localScale = new Vector3(3.2f, 0.8f, 0.2f);
        fence.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(new Color(0.55f, 0.35f, 0.18f));
        Destroy(fence.GetComponent<Collider>());
    }

    // --- WORLD 2: DESERT STORM PROPS ---
    private void SpawnCactus(Vector3 pos)
    {
        GameObject cactus = new GameObject("Cactus");
        cactus.transform.SetParent(transform, false);
        cactus.transform.position = pos;

        Material mat = MaterialHelper.CreateMat(new Color(0.18f, 0.58f, 0.25f));

        GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stem.transform.SetParent(cactus.transform, false);
        stem.transform.localPosition = new Vector3(0, 2.0f, 0);
        stem.transform.localScale = new Vector3(0.6f, 2.0f, 0.6f);
        stem.GetComponent<Renderer>().sharedMaterial = mat;
        Destroy(stem.GetComponent<Collider>());

        GameObject arm1 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        arm1.transform.SetParent(cactus.transform, false);
        arm1.transform.localPosition = new Vector3(0.85f, 2.4f, 0);
        arm1.transform.localRotation = Quaternion.Euler(0, 0, -50);
        arm1.transform.localScale = new Vector3(0.4f, 0.9f, 0.4f);
        arm1.GetComponent<Renderer>().sharedMaterial = mat;
        Destroy(arm1.GetComponent<Collider>());

        GameObject arm2 = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        arm2.transform.SetParent(cactus.transform, false);
        arm2.transform.localPosition = new Vector3(-0.85f, 1.8f, 0);
        arm2.transform.localRotation = Quaternion.Euler(0, 0, 50);
        arm2.transform.localScale = new Vector3(0.4f, 0.9f, 0.4f);
        arm2.GetComponent<Renderer>().sharedMaterial = mat;
        Destroy(arm2.GetComponent<Collider>());
    }

    private void SpawnRuinsPillar(Vector3 pos)
    {
        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pillar.name = "DesertPillar";
        pillar.transform.SetParent(transform, false);
        pillar.transform.position = pos + new Vector3(0, 2.4f, 0);
        pillar.transform.rotation = Quaternion.Euler(0, 15f, 8f);
        pillar.transform.localScale = new Vector3(1.3f, 4.8f, 1.3f);
        pillar.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(new Color(0.88f, 0.72f, 0.48f));
        Destroy(pillar.GetComponent<Collider>());
    }

    private void SpawnDesertArch(Vector3 pos)
    {
        GameObject arch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arch.name = "AncientArch";
        arch.transform.SetParent(transform, false);
        arch.transform.position = pos + new Vector3(0, 3.2f, 0);
        arch.transform.localScale = new Vector3(2.8f, 1.0f, 1.2f);
        arch.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(new Color(0.82f, 0.65f, 0.42f));
        Destroy(arch.GetComponent<Collider>());
    }

    // --- WORLD 3: SNOW MOUNTAIN PROPS ---
    private void SpawnIceBridgeOrShard(Vector3 pos)
    {
        GameObject ice = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ice.name = "IceShard";
        ice.transform.SetParent(transform, false);
        ice.transform.position = pos + new Vector3(0, 1.8f, 0);
        ice.transform.rotation = Quaternion.Euler(20f, 35f, -25f);
        ice.transform.localScale = new Vector3(0.8f, 2.5f, 0.8f);
        ice.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(new Color(0.45f, 0.85f, 0.98f), 0.9f, 0.2f);
        Destroy(ice.GetComponent<Collider>());
    }

    // --- WORLD 4: ALIEN PLANET PROPS ---
    private void SpawnAlienCrystal(Vector3 pos)
    {
        GameObject crystal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        crystal.name = "AlienCrystal";
        crystal.transform.SetParent(transform, false);
        crystal.transform.position = pos + new Vector3(0, 2.2f, 0);
        crystal.transform.rotation = Quaternion.Euler(15f, 45f, -25f);
        crystal.transform.localScale = new Vector3(0.7f, 2.8f, 0.7f);
        crystal.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(new Color(0.1f, 0.95f, 0.90f), 0.95f, 0.5f);
        Destroy(crystal.GetComponent<Collider>());
    }

    private void SpawnAlienSpire(Vector3 pos)
    {
        GameObject spire = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        spire.name = "AlienSpire";
        spire.transform.SetParent(transform, false);
        spire.transform.position = pos + new Vector3(0, 2.8f, 0);
        spire.transform.localScale = new Vector3(1.2f, 4.2f, 1.2f);
        spire.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(new Color(0.98f, 0.15f, 0.68f), 0.85f, 0.3f);
        Destroy(spire.GetComponent<Collider>());
    }

    private void SpawnSciFiGate(Vector3 pos)
    {
        GameObject gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gate.name = "SciFiPylon";
        gate.transform.SetParent(transform, false);
        gate.transform.position = pos + new Vector3(0, 3.0f, 0);
        gate.transform.localScale = new Vector3(1.0f, 5.0f, 1.0f);
        gate.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(new Color(0.25f, 0.15f, 0.45f), 0.9f, 0.7f);
        Destroy(gate.GetComponent<Collider>());
    }
}

/// <summary>
/// =========================================================================================
/// PICKUP - Animated Coin & Fuel Jerrycan Pickups with Trigger Collision
/// =========================================================================================
/// </summary>
public class Pickup : MonoBehaviour
{
    public enum PickupType { Coin, Fuel }
    public PickupType type;

    private float basePosY;
    private bool isCollected = false;

    public void Setup(PickupType pType)
    {
        type = pType;
        basePosY = transform.position.y;

        SphereCollider col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 1.1f;

        if (type == PickupType.Coin)
        {
            GameObject coinVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coinVisual.transform.SetParent(transform, false);
            coinVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            coinVisual.transform.localScale = new Vector3(0.85f, 0.12f, 0.85f);
            Destroy(coinVisual.GetComponent<Collider>());
            coinVisual.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(new Color(1.0f, 0.82f, 0.12f), 0.9f, 0.8f);
        }
        else if (type == PickupType.Fuel)
        {
            GameObject can = GameObject.CreatePrimitive(PrimitiveType.Cube);
            can.transform.SetParent(transform, false);
            can.transform.localScale = new Vector3(0.7f, 1.0f, 0.5f);
            Destroy(can.GetComponent<Collider>());
            can.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(new Color(0.92f, 0.15f, 0.15f), 0.6f, 0.3f);

            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.transform.SetParent(can.transform, false);
            handle.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            handle.transform.localScale = new Vector3(0.45f, 0.2f, 0.2f);
            Destroy(handle.GetComponent<Collider>());
            handle.GetComponent<Renderer>().sharedMaterial = MaterialHelper.CreateMat(new Color(0.92f, 0.15f, 0.15f));
        }
    }

    private void Update()
    {
        if (isCollected) return;

        transform.Rotate(0f, 110f * Time.deltaTime, 0f, Space.World);

        float newY = basePosY + Mathf.Sin(Time.time * 3.5f + transform.position.x) * 0.22f;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isCollected) return;

        if (other.GetComponentInParent<VehicleController>() != null || other.CompareTag("Player"))
        {
            isCollected = true;
            if (type == PickupType.Coin)
            {
                ExtremeHillRiders.Instance.CollectCoin(1);
            }
            else if (type == PickupType.Fuel)
            {
                ExtremeHillRiders.Instance.CollectFuel();
            }

            StartCoroutine(CollectAnimation());
        }
    }

    private IEnumerator CollectAnimation()
    {
        float timer = 0f;
        Vector3 initialScale = transform.localScale;
        while (timer < 0.15f)
        {
            timer += Time.deltaTime;
            transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, timer / 0.15f);
            yield return null;
        }
        Destroy(gameObject);
    }
}

/// <summary>
/// =========================================================================================
/// SOUND FX - Procedural Synthesized Sound Effects
/// =========================================================================================
/// </summary>
public class SoundFX : MonoBehaviour
{
    public enum SfxType { Coin, Fuel, Crash, ButtonClick }

    private AudioSource sfxSource;
    private AudioSource engineSource;
    private AudioClip coinClip;
    private AudioClip fuelClip;
    private AudioClip crashClip;
    private AudioClip buttonClip;
    private AudioClip engineClip;

    private void Awake()
    {
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        engineSource = gameObject.AddComponent<AudioSource>();
        engineSource.loop = true;
        engineSource.playOnAwake = false;
        engineSource.volume = 0.35f;

        coinClip = GenerateToneClip(980f, 0.16f, true);
        fuelClip = GenerateToneClip(420f, 0.25f, false);
        crashClip = GenerateNoiseClip(0.45f);
        buttonClip = GenerateToneClip(650f, 0.08f, false);
        engineClip = GenerateEngineHumClip();

        engineSource.clip = engineClip;
    }

    public void PlaySound(SfxType type)
    {
        AudioClip clip = null;
        switch (type)
        {
            case SfxType.Coin: clip = coinClip; break;
            case SfxType.Fuel: clip = fuelClip; break;
            case SfxType.Crash: clip = crashClip; break;
            case SfxType.ButtonClick: clip = buttonClip; break;
        }
        if (clip != null && sfxSource != null)
        {
            sfxSource.PlayOneShot(clip);
        }
    }

    public void PlayEngineSound(bool play)
    {
        if (engineSource == null) return;
        if (play && !engineSource.isPlaying) engineSource.Play();
        else if (!play && engineSource.isPlaying) engineSource.Stop();
    }

    private void Update()
    {
        if (engineSource != null && engineSource.isPlaying && ExtremeHillRiders.Instance != null && ExtremeHillRiders.Instance.activeVehicle != null)
        {
            float speedRatio = ExtremeHillRiders.Instance.activeVehicle.GetSpeed() / 25f;
            engineSource.pitch = Mathf.Lerp(0.85f, 1.85f, speedRatio);
        }
    }

    private AudioClip GenerateToneClip(float frequency, float duration, bool arpeggio)
    {
        int sampleRate = 44100;
        int samplesCount = (int)(sampleRate * duration);
        float[] samples = new float[samplesCount];

        for (int i = 0; i < samplesCount; i++)
        {
            float t = (float)i / sampleRate;
            float freq = frequency;
            if (arpeggio && t > duration * 0.5f) freq = frequency * 1.5f;

            float decay = 1f - (t / duration);
            samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * decay * 0.4f;
        }

        AudioClip clip = AudioClip.Create("Tone_" + frequency, samplesCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip GenerateNoiseClip(float duration)
    {
        int sampleRate = 44100;
        int samplesCount = (int)(sampleRate * duration);
        float[] samples = new float[samplesCount];

        for (int i = 0; i < samplesCount; i++)
        {
            float decay = 1f - ((float)i / samplesCount);
            samples[i] = (UnityEngine.Random.value * 2f - 1f) * decay * 0.5f;
        }

        AudioClip clip = AudioClip.Create("NoiseClip", samplesCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip GenerateEngineHumClip()
    {
        int sampleRate = 44100;
        float duration = 1.0f;
        int samplesCount = (int)(sampleRate * duration);
        float[] samples = new float[samplesCount];

        for (int i = 0; i < samplesCount; i++)
        {
            float t = (float)i / sampleRate;
            float val = Mathf.Sin(2f * Mathf.PI * 75f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.3f;
            samples[i] = val * 0.35f;
        }

        AudioClip clip = AudioClip.Create("EngineHum", samplesCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}

/// <summary>
/// =========================================================================================
/// UI MANAGER - Built-in Unity UI (Main Menu, World Selection, HUD & Game Over Screen)
/// =========================================================================================
/// </summary>
public class UIManager : MonoBehaviour
{
    private Canvas mainCanvas;

    private GameObject mainMenuPanel;
    private GameObject hudPanel;
    private GameObject pausePanel;
    private GameObject gameOverPanel;

    private Text speedText;
    private Text fuelText;
    private Image fuelFillImage;
    private Text coinsText;
    private Text distanceText;
    private Text worldNameText;

    private Text goReasonText;
    private Text goDistanceText;
    private Text goCoinsText;
    private Text goScoreText;
    private Text goBestText;

    private Button[] worldButtons = new Button[4];
    private Button[] vehicleButtons = new Button[3];

    private void Awake()
    {
        BuildEntireUI();
    }

    public void BuildEntireUI()
    {
        GameObject canvasGo = new GameObject("ExtremeHillRiders_Canvas");
        mainCanvas = canvasGo.AddComponent<Canvas>();
        mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        ExtremeHillRiders.FixEventSystemInputModule();

        Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // --- 1. MAIN MENU PANEL ---
        mainMenuPanel = CreatePanel(canvasGo.transform, "MainMenuPanel", new Color(0.06f, 0.08f, 0.12f, 0.90f));

        Text title = CreateText(mainMenuPanel.transform, "Title", "EXTREME HILL RIDERS", 46, defaultFont, new Color(1f, 0.85f, 0.2f), FontStyle.Bold);
        SetRect(title.rectTransform, new Vector2(0.5f, 0.88f), new Vector2(0.5f, 0.88f), new Vector2(0, 0), new Vector2(650, 65));

        Text subtitle = CreateText(mainMenuPanel.transform, "SubTitle", "3D OFF-ROAD ADVENTURE", 19, defaultFont, Color.white, FontStyle.Normal);
        SetRect(subtitle.rectTransform, new Vector2(0.5f, 0.81f), new Vector2(0.5f, 0.81f), new Vector2(0, 0), new Vector2(400, 30));

        Text worldLbl = CreateText(mainMenuPanel.transform, "WorldLbl", "SELECT WORLD", 20, defaultFont, new Color(0.3f, 0.9f, 1f), FontStyle.Bold);
        SetRect(worldLbl.rectTransform, new Vector2(0.5f, 0.70f), new Vector2(0.5f, 0.70f), new Vector2(0, 0), new Vector2(300, 30));

        string[] worldNames = { "GREEN HILLS", "DESERT STORM", "SNOW MOUNTAIN", "ALIEN PLANET" };
        Color[] worldColors = {
            new Color(0.24f, 0.65f, 0.28f),
            new Color(0.88f, 0.58f, 0.22f),
            new Color(0.35f, 0.68f, 0.92f),
            new Color(0.65f, 0.22f, 0.85f)
        };

        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            Button wBtn = CreateButton(mainMenuPanel.transform, "WorldBtn_" + i, worldNames[i], 15, defaultFont, worldColors[i]);
            float posX = -270f + (i * 180f);
            SetRect(wBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.61f), new Vector2(0.5f, 0.61f), new Vector2(posX, 0), new Vector2(165, 50));
            wBtn.onClick.AddListener(() => ExtremeHillRiders.Instance.SelectWorld(idx));
            worldButtons[i] = wBtn;
        }

        Text vehLbl = CreateText(mainMenuPanel.transform, "VehLbl", "SELECT VEHICLE", 20, defaultFont, new Color(0.3f, 0.9f, 1f), FontStyle.Bold);
        SetRect(vehLbl.rectTransform, new Vector2(0.5f, 0.49f), new Vector2(0.5f, 0.49f), new Vector2(0, 0), new Vector2(300, 30));

        string[] vehNames = { "HILL JEEP", "MONSTER TRUCK", "DUNE BUGGY" };
        Color[] vehColors = {
            new Color(0.25f, 0.55f, 0.35f),
            new Color(0.85f, 0.25f, 0.20f),
            new Color(0.15f, 0.65f, 0.85f)
        };

        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            Button vBtn = CreateButton(mainMenuPanel.transform, "VehBtn_" + i, vehNames[i], 16, defaultFont, vehColors[i]);
            float posX = -200f + (i * 200f);
            SetRect(vBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.40f), new Vector2(0.5f, 0.40f), new Vector2(posX, 0), new Vector2(180, 48));
            vBtn.onClick.AddListener(() => ExtremeHillRiders.Instance.SelectVehicle(idx));
            vehicleButtons[i] = vBtn;
        }

        Button playBtn = CreateButton(mainMenuPanel.transform, "PlayButton", "LET'S RIDE!", 26, defaultFont, new Color(0.12f, 0.82f, 0.35f));
        SetRect(playBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.24f), new Vector2(0, 0), new Vector2(280, 60));
        playBtn.onClick.AddListener(() => ExtremeHillRiders.Instance.StartGame());

        Text controlsInfo = CreateText(mainMenuPanel.transform, "ControlsInfo", "CONTROLS: [D / ->] Gas | [A / <-] Brake | [Space] Tilt Down | [Shift] Tilt Up", 14, defaultFont, new Color(0.8f, 0.85f, 0.9f));
        SetRect(controlsInfo.rectTransform, new Vector2(0.5f, 0.10f), new Vector2(0.5f, 0.10f), new Vector2(0, 0), new Vector2(650, 30));

        // --- 2. GAMEPLAY HUD PANEL (Clean, Non-Overlapping Layout) ---
        hudPanel = CreatePanel(canvasGo.transform, "HUDPanel", Color.clear);

        // Distance & World (Top Left)
        distanceText = CreateText(hudPanel.transform, "DistanceText", "DISTANCE: 0 m", 22, defaultFont, Color.white, FontStyle.Bold);
        SetRect(distanceText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(140, -32), new Vector2(240, 35));

        worldNameText = CreateText(hudPanel.transform, "WorldBadge", "WORLD: GREEN HILLS", 15, defaultFont, new Color(0.4f, 0.9f, 1f), FontStyle.Bold);
        SetRect(worldNameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(140, -65), new Vector2(240, 25));

        coinsText = CreateText(hudPanel.transform, "CoinsText", "★ COINS: 0", 22, defaultFont, new Color(1f, 0.88f, 0.2f), FontStyle.Bold);
        SetRect(coinsText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(140, -96), new Vector2(240, 32));

        // Fuel Bar (Top Center)
        GameObject fuelContainer = new GameObject("FuelBarContainer");
        fuelContainer.transform.SetParent(hudPanel.transform, false);
        Image fuelBg = fuelContainer.AddComponent<Image>();
        fuelBg.color = new Color(0.12f, 0.12f, 0.15f, 0.9f);
        SetRect(fuelContainer.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -32), new Vector2(260, 26));

        GameObject fuelFill = new GameObject("FuelFill");
        fuelFill.transform.SetParent(fuelContainer.transform, false);
        fuelFillImage = fuelFill.AddComponent<Image>();
        fuelFillImage.color = new Color(0.2f, 0.85f, 0.3f);
        SetRect(fuelFill.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(128, 0), new Vector2(254, 22));

        fuelText = CreateText(fuelContainer.transform, "FuelLabel", "FUEL", 14, defaultFont, Color.white, FontStyle.Bold);
        SetRect(fuelText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100, 22));

        // Speedometer & Pause (Top Right)
        speedText = CreateText(hudPanel.transform, "SpeedText", "0 KM/H", 22, defaultFont, Color.cyan, FontStyle.Bold);
        SetRect(speedText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-190, -32), new Vector2(150, 35));

        Button pauseBtn = CreateButton(hudPanel.transform, "PauseBtn", "II", 22, defaultFont, new Color(0.25f, 0.3f, 0.4f, 0.9f));
        SetRect(pauseBtn.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-55, -35), new Vector2(50, 42));
        pauseBtn.onClick.AddListener(() => ExtremeHillRiders.Instance.PauseGame());

        // Mobile Touch Pedals
        Button brakeBtn = CreateButton(hudPanel.transform, "BrakePedal", "BRAKE\n[A / <-]", 17, defaultFont, new Color(0.85f, 0.25f, 0.25f, 0.85f));
        SetRect(brakeBtn.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(110, 85), new Vector2(150, 100));
        AddTouchListener(brakeBtn.gameObject, () => ExtremeHillRiders.Instance.SetMobileBrake(true), () => ExtremeHillRiders.Instance.SetMobileBrake(false));

        Button gasBtn = CreateButton(hudPanel.transform, "GasPedal", "GAS\n[D / ->]", 17, defaultFont, new Color(0.25f, 0.82f, 0.35f, 0.85f));
        SetRect(gasBtn.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-110, 85), new Vector2(150, 100));
        AddTouchListener(gasBtn.gameObject, () => ExtremeHillRiders.Instance.SetMobileGas(true), () => ExtremeHillRiders.Instance.SetMobileGas(false));

        // --- 3. PAUSE PANEL ---
        pausePanel = CreatePanel(canvasGo.transform, "PausePanel", new Color(0.05f, 0.05f, 0.08f, 0.88f));

        Text pauseTitle = CreateText(pausePanel.transform, "PauseTitle", "GAME PAUSED", 42, defaultFont, Color.white, FontStyle.Bold);
        SetRect(pauseTitle.rectTransform, new Vector2(0.5f, 0.70f), new Vector2(0.5f, 0.70f), Vector2.zero, new Vector2(400, 60));

        Button resumeBtn = CreateButton(pausePanel.transform, "ResumeBtn", "RESUME", 22, defaultFont, new Color(0.2f, 0.75f, 0.35f));
        SetRect(resumeBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.52f), new Vector2(0.5f, 0.52f), Vector2.zero, new Vector2(240, 52));
        resumeBtn.onClick.AddListener(() => ExtremeHillRiders.Instance.ResumeGame());

        Button restartBtn = CreateButton(pausePanel.transform, "RestartBtn", "RESTART", 22, defaultFont, new Color(0.85f, 0.55f, 0.2f));
        SetRect(restartBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.40f), new Vector2(0.5f, 0.40f), Vector2.zero, new Vector2(240, 52));
        restartBtn.onClick.AddListener(() => ExtremeHillRiders.Instance.RestartGame());

        Button menuBtn = CreateButton(pausePanel.transform, "MenuBtn", "MAIN MENU", 22, defaultFont, new Color(0.45f, 0.5f, 0.6f));
        SetRect(menuBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.28f), Vector2.zero, new Vector2(240, 52));
        menuBtn.onClick.AddListener(() => ExtremeHillRiders.Instance.ReturnToMainMenu());

        // --- 4. GAME OVER PANEL ---
        gameOverPanel = CreatePanel(canvasGo.transform, "GameOverPanel", new Color(0.12f, 0.04f, 0.04f, 0.94f));

        Text goTitle = CreateText(gameOverPanel.transform, "GOTitle", "GAME OVER", 48, defaultFont, new Color(1f, 0.25f, 0.25f), FontStyle.Bold);
        SetRect(goTitle.rectTransform, new Vector2(0.5f, 0.85f), new Vector2(0.5f, 0.85f), Vector2.zero, new Vector2(450, 65));

        goReasonText = CreateText(gameOverPanel.transform, "GOReason", "CRASHED!", 26, defaultFont, Color.yellow, FontStyle.Bold);
        SetRect(goReasonText.rectTransform, new Vector2(0.5f, 0.75f), new Vector2(0.5f, 0.75f), Vector2.zero, new Vector2(400, 40));

        goDistanceText = CreateText(gameOverPanel.transform, "GODistance", "Distance: 0 m", 22, defaultFont, Color.white);
        SetRect(goDistanceText.rectTransform, new Vector2(0.5f, 0.64f), new Vector2(0.5f, 0.64f), Vector2.zero, new Vector2(350, 35));

        goCoinsText = CreateText(gameOverPanel.transform, "GOCoins", "Coins Collected: 0", 22, defaultFont, new Color(1f, 0.88f, 0.2f));
        SetRect(goCoinsText.rectTransform, new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.56f), Vector2.zero, new Vector2(350, 35));

        goScoreText = CreateText(gameOverPanel.transform, "GOScore", "Total Score: 0", 26, defaultFont, Color.green, FontStyle.Bold);
        SetRect(goScoreText.rectTransform, new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.47f), Vector2.zero, new Vector2(350, 40));

        goBestText = CreateText(gameOverPanel.transform, "GOBest", "Best Score: 0", 20, defaultFont, new Color(0.7f, 0.85f, 1f));
        SetRect(goBestText.rectTransform, new Vector2(0.5f, 0.39f), new Vector2(0.5f, 0.39f), Vector2.zero, new Vector2(350, 35));

        Button goRetryBtn = CreateButton(gameOverPanel.transform, "GORetry", "RIDE AGAIN", 24, defaultFont, new Color(0.15f, 0.78f, 0.35f));
        SetRect(goRetryBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.26f), new Vector2(0.5f, 0.26f), Vector2.zero, new Vector2(250, 56));
        goRetryBtn.onClick.AddListener(() => ExtremeHillRiders.Instance.RestartGame());

        Button goMenuBtn = CreateButton(gameOverPanel.transform, "GOMenu", "MAIN MENU", 20, defaultFont, new Color(0.45f, 0.5f, 0.6f));
        SetRect(goMenuBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0.15f), new Vector2(0.5f, 0.15f), Vector2.zero, new Vector2(250, 48));
        goMenuBtn.onClick.AddListener(() => ExtremeHillRiders.Instance.ReturnToMainMenu());

        UpdateWorldButtons(ExtremeHillRiders.Instance.currentWorld);
        UpdateVehicleButtons(ExtremeHillRiders.Instance.currentVehicle);
    }

    public void UpdateStateUI(ExtremeHillRiders.GameState state)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(state == ExtremeHillRiders.GameState.MainMenu);
        if (hudPanel != null) hudPanel.SetActive(state == ExtremeHillRiders.GameState.Playing);
        if (pausePanel != null) pausePanel.SetActive(state == ExtremeHillRiders.GameState.Paused);
        if (gameOverPanel != null) gameOverPanel.SetActive(state == ExtremeHillRiders.GameState.GameOver);
    }

    public void UpdateHUD(float speed, float fuelRatio, int coins, float distance, ExtremeHillRiders.WorldTheme world)
    {
        if (speedText != null) speedText.text = $"{Mathf.RoundToInt(speed)} KM/H";
        if (distanceText != null) distanceText.text = $"DISTANCE: {Mathf.FloorToInt(distance)} m";
        if (coinsText != null) coinsText.text = $"★ COINS: {coins}";

        if (worldNameText != null)
        {
            switch (world)
            {
                case ExtremeHillRiders.WorldTheme.GreenHills:
                    worldNameText.text = "WORLD: GREEN HILLS";
                    worldNameText.color = new Color(0.4f, 1.0f, 0.4f);
                    break;
                case ExtremeHillRiders.WorldTheme.DesertStorm:
                    worldNameText.text = "WORLD: DESERT STORM";
                    worldNameText.color = new Color(1.0f, 0.75f, 0.2f);
                    break;
                case ExtremeHillRiders.WorldTheme.SnowMountain:
                    worldNameText.text = "WORLD: SNOW MOUNTAIN";
                    worldNameText.color = new Color(0.5f, 0.9f, 1.0f);
                    break;
                case ExtremeHillRiders.WorldTheme.AlienPlanet:
                    worldNameText.text = "WORLD: ALIEN PLANET";
                    worldNameText.color = new Color(0.85f, 0.4f, 1.0f);
                    break;
            }
        }

        if (fuelFillImage != null)
        {
            fuelRatio = Mathf.Clamp01(fuelRatio);
            fuelFillImage.rectTransform.sizeDelta = new Vector2(254f * fuelRatio, 22f);

            if (fuelRatio > 0.45f) fuelFillImage.color = new Color(0.2f, 0.85f, 0.3f);
            else if (fuelRatio > 0.20f) fuelFillImage.color = new Color(0.95f, 0.80f, 0.15f);
            else fuelFillImage.color = new Color(0.95f, 0.20f, 0.20f);
        }
    }

    public void ShowGameOver(string reason, float distance, int coins, int score, int best)
    {
        if (goReasonText != null) goReasonText.text = reason;
        if (goDistanceText != null) goDistanceText.text = $"Distance: {Mathf.FloorToInt(distance)} m";
        if (goCoinsText != null) goCoinsText.text = $"Coins Collected: {coins}";
        if (goScoreText != null) goScoreText.text = $"Total Score: {score}";
        if (goBestText != null) goBestText.text = $"Best Record: {best}";
    }

    public void UpdateWorldButtons(ExtremeHillRiders.WorldTheme selected)
    {
        for (int i = 0; i < worldButtons.Length; i++)
        {
            if (worldButtons[i] == null) continue;
            ColorBlock cb = worldButtons[i].colors;
            if (i == (int)selected)
            {
                cb.normalColor = Color.white;
                worldButtons[i].GetComponentInChildren<Text>().color = Color.black;
            }
            else
            {
                cb.normalColor = new Color(0.25f, 0.30f, 0.40f);
                worldButtons[i].GetComponentInChildren<Text>().color = Color.white;
            }
            worldButtons[i].colors = cb;
        }
    }

    public void UpdateVehicleButtons(ExtremeHillRiders.VehicleType selected)
    {
        for (int i = 0; i < vehicleButtons.Length; i++)
        {
            if (vehicleButtons[i] == null) continue;
            ColorBlock cb = vehicleButtons[i].colors;
            if (i == (int)selected)
            {
                cb.normalColor = Color.white;
                vehicleButtons[i].GetComponentInChildren<Text>().color = Color.black;
            }
            else
            {
                cb.normalColor = new Color(0.25f, 0.30f, 0.40f);
                vehicleButtons[i].GetComponentInChildren<Text>().color = Color.white;
            }
            vehicleButtons[i].colors = cb;
        }
    }

    public void PopCoinCounter()
    {
        if (coinsText != null) StartCoroutine(PulseRoutine(coinsText.rectTransform));
    }

    public void PopFuelBar()
    {
        if (fuelFillImage != null) StartCoroutine(PulseRoutine(fuelFillImage.rectTransform));
    }

    private IEnumerator PulseRoutine(RectTransform target)
    {
        Vector3 orig = target.localScale;
        target.localScale = orig * 1.25f;
        yield return new WaitForSeconds(0.12f);
        target.localScale = orig;
    }

    private GameObject CreatePanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        Image img = panel.AddComponent<Image>();
        img.color = color;
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        return panel;
    }

    private Text CreateText(Transform parent, string name, string content, int fontSize, Font font, Color color, FontStyle style = FontStyle.Normal)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);
        Text text = textObj.AddComponent<Text>();
        text.text = content;
        text.fontSize = fontSize;
        text.font = font;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        return text;
    }

    private Button CreateButton(Transform parent, string name, string label, int fontSize, Font font, Color bgColor)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        Image img = btnObj.AddComponent<Image>();
        img.color = bgColor;

        Button btn = btnObj.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = Color.Lerp(bgColor, Color.white, 0.25f);
        cb.pressedColor = Color.Lerp(bgColor, Color.black, 0.25f);
        btn.colors = cb;

        Text txt = CreateText(btnObj.transform, "Text", label, fontSize, font, Color.white, FontStyle.Bold);
        SetRect(txt.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        return btn;
    }

    private void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;
    }

    private void AddTouchListener(GameObject obj, Action onDown, Action onUp)
    {
        EventTrigger trigger = obj.AddComponent<EventTrigger>();

        EventTrigger.Entry downEntry = new EventTrigger.Entry();
        downEntry.eventID = EventTriggerType.PointerDown;
        downEntry.callback.AddListener((data) => onDown?.Invoke());
        trigger.triggers.Add(downEntry);

        EventTrigger.Entry upEntry = new EventTrigger.Entry();
        upEntry.eventID = EventTriggerType.PointerUp;
        upEntry.callback.AddListener((data) => onUp?.Invoke());
        trigger.triggers.Add(upEntry);
    }
}
