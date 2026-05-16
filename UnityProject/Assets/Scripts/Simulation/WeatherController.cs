using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;

namespace DroneVR.Simulation
{
    public enum WeatherMode
    {
        Day,
        Night,
        Rain
    }

    public class WeatherController : MonoBehaviour
    {
        [Header("Mode")]
        [SerializeField] private WeatherMode startingWeather = WeatherMode.Day;
        [SerializeField] private bool applyOnStart = true;

        [Header("Input")]
        [SerializeField] private KeyCode cycleWeatherKey = KeyCode.F;
        [SerializeField] private bool enableVrButtonCycling = true;

        [Header("References")]
        [SerializeField] private Light sunOrMoonLight;
        [SerializeField] private Transform followTarget;
        [SerializeField] private Camera tintCamera;
        [SerializeField] private EnvironmentMaterialWeatherAdapter materialAdapter;
        [SerializeField] private bool useMaterialAdapter;

        [Header("Night")]
        [SerializeField] private Color moonColor = new Color(0.72f, 0.8f, 1f);
        [SerializeField] private float moonIntensity = 0.65f;
        [SerializeField] private Color nightAmbientColor = new Color(0.18f, 0.2f, 0.24f);
        [SerializeField] private Color nightFogColor = new Color(0.05f, 0.065f, 0.1f);
        [SerializeField] private float nightFogDensity = 0.0008f;

        [Header("Rain")]
        [SerializeField] private bool createRainIfMissing = true;
        [SerializeField] private ParticleSystem rainParticles;
        [SerializeField] private Vector3 rainFollowOffset = new Vector3(0f, 18f, 0f);
        [SerializeField] private float rainAreaSize = 55f;
        [SerializeField] private float rainRate = 1200f;
        [SerializeField] private float rainFallSpeed = 20f;
        [SerializeField] private Color rainColor = new Color(0.72f, 0.82f, 0.92f, 0.45f);

        [Header("Camera Tint")]
        [SerializeField] private bool useCameraTint = true;
        [SerializeField, Range(0f, 1f)] private float nightDarkness = 0.75f;
        [SerializeField, Range(0f, 1f)] private float rainDarkness = 0.45f;
        [SerializeField] private Color nightCameraTint = new Color(0.005f, 0.018f, 0.06f, 1f);
        [SerializeField] private Color rainCameraTint = new Color(0.025f, 0.03f, 0.035f, 1f);
        [SerializeField] private float tintPlaneDistance = 0.25f;

        [Header("Overlay")]
        [SerializeField] private bool showWeatherOverlay = true;

        private WeatherMode currentWeather;
        private XRInputDevice rightController;
        private bool vrButtonWasPressed;
        private Material generatedRainMaterial;
        private Material generatedRainSheetMaterial;
        private Material generatedRainMistMaterial;
        private Transform generatedRainRig;
        private ParticleSystem[] rainSystems;
        private MeshRenderer tintRenderer;
        private Material tintMaterial;

        private bool originalFogEnabled;
        private FogMode originalFogMode;
        private Color originalFogColor;
        private float originalFogDensity;
        private float originalFogStartDistance;
        private float originalFogEndDistance;
        private UnityEngine.Rendering.AmbientMode originalAmbientMode;
        private Color originalAmbientLight;
        private float originalReflectionIntensity;
        private Material originalSkybox;
        private LightShadows originalLightShadows;
        private float originalLightIntensity;
        private Color originalLightColor;

        public WeatherMode CurrentWeather => currentWeather;

        private void OnValidate()
        {
            rainAreaSize = Mathf.Max(1f, rainAreaSize);
            rainRate = Mathf.Max(0f, rainRate);
            rainFallSpeed = Mathf.Max(0.1f, rainFallSpeed);
            nightFogDensity = Mathf.Max(0f, nightFogDensity);
            nightDarkness = Mathf.Clamp01(nightDarkness);
            rainDarkness = Mathf.Clamp01(rainDarkness);
            tintPlaneDistance = Mathf.Max(0.02f, tintPlaneDistance);
        }

        private void Awake()
        {
            ResolveReferences();
            CacheOriginalLighting();

            if (createRainIfMissing && rainParticles == null)
            {
                rainSystems = CreateRainRig();
                rainParticles = rainSystems != null && rainSystems.Length > 0 ? rainSystems[0] : null;
            }
            else if (rainParticles != null)
            {
                rainSystems = new[] { rainParticles };
            }

            ConfigureRainRenderer();
            CreateTintOverlayIfNeeded();
            StopRain();
        }

        private void Start()
        {
            if (applyOnStart)
            {
                SetWeather(startingWeather);
            }
        }

        private void OnDisable()
        {
            ApplyDay();
        }

        private void OnDestroy()
        {
            if (generatedRainMaterial != null)
            {
                Destroy(generatedRainMaterial);
            }

            if (generatedRainSheetMaterial != null)
            {
                Destroy(generatedRainSheetMaterial);
            }

            if (generatedRainMistMaterial != null)
            {
                Destroy(generatedRainMistMaterial);
            }

            if (tintMaterial != null)
            {
                Destroy(tintMaterial);
            }
        }

        private void Update()
        {
            if (WasKeyPressed(cycleWeatherKey) || WasVrCyclePressed())
            {
                CycleWeather();
            }
        }

        private void LateUpdate()
        {
            if (generatedRainRig != null && followTarget != null)
            {
                generatedRainRig.position = followTarget.position + rainFollowOffset;
                generatedRainRig.rotation = Quaternion.identity;
            }
            else if (rainParticles != null && followTarget != null)
            {
                rainParticles.transform.position = followTarget.position + rainFollowOffset;
                rainParticles.transform.rotation = Quaternion.identity;
            }

            UpdateTintOverlayScale();
        }

        public void CycleWeather()
        {
            WeatherMode nextWeather = (WeatherMode)(((int)currentWeather + 1) % System.Enum.GetValues(typeof(WeatherMode)).Length);
            SetWeather(nextWeather);
        }

        public void SetWeather(WeatherMode weather)
        {
            if (!System.Enum.IsDefined(typeof(WeatherMode), weather))
            {
                weather = WeatherMode.Day;
            }

            currentWeather = weather;

            switch (weather)
            {
                case WeatherMode.Day:
                    ApplyDay();
                    break;
                case WeatherMode.Night:
                    ApplyNight();
                    break;
                case WeatherMode.Rain:
                    ApplyRain();
                    break;
            }
        }

        private void ApplyDay()
        {
            if (useMaterialAdapter && materialAdapter != null)
            {
                materialAdapter.ApplyWeather(WeatherMode.Day);
            }

            RenderSettings.fog = originalFogEnabled;
            RenderSettings.fogMode = originalFogMode;
            RenderSettings.fogColor = originalFogColor;
            RenderSettings.fogDensity = originalFogDensity;
            RenderSettings.fogStartDistance = originalFogStartDistance;
            RenderSettings.fogEndDistance = originalFogEndDistance;
            RenderSettings.ambientMode = originalAmbientMode;
            RenderSettings.ambientLight = originalAmbientLight;
            RenderSettings.reflectionIntensity = originalReflectionIntensity;
            RenderSettings.skybox = originalSkybox;

            if (sunOrMoonLight != null)
            {
                sunOrMoonLight.color = originalLightColor;
                sunOrMoonLight.intensity = originalLightIntensity;
                sunOrMoonLight.shadows = originalLightShadows;
            }

            StopRain();
            SetCameraTint(false, Color.clear);
        }

        private void ApplyNight()
        {
            if (useMaterialAdapter && materialAdapter != null)
            {
                materialAdapter.ApplyWeather(WeatherMode.Night);
            }

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = nightAmbientColor;
            RenderSettings.reflectionIntensity = 0.2f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = nightFogColor;
            RenderSettings.fogDensity = nightFogDensity;

            if (sunOrMoonLight != null)
            {
                sunOrMoonLight.color = moonColor;
                sunOrMoonLight.intensity = moonIntensity;
                sunOrMoonLight.shadows = LightShadows.Soft;
            }

            StopRain();
            SetCameraTint(true, WithAlpha(nightCameraTint, nightDarkness));
        }

        private void ApplyRain()
        {
            if (useMaterialAdapter && materialAdapter != null)
            {
                materialAdapter.ApplyWeather(WeatherMode.Rain);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.46f, 0.48f, 0.5f);
            RenderSettings.reflectionIntensity = 0.35f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.5f, 0.52f, 0.54f);
            RenderSettings.fogDensity = 0.0012f;

            if (sunOrMoonLight != null)
            {
                sunOrMoonLight.color = new Color(0.78f, 0.84f, 0.9f);
                sunOrMoonLight.intensity = 0.75f;
                sunOrMoonLight.shadows = LightShadows.Soft;
            }

            StartRain();
            SetCameraTint(true, WithAlpha(rainCameraTint, rainDarkness));
        }

        private void ResolveReferences()
        {
            if (sunOrMoonLight == null)
            {
                foreach (Light lightComponent in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (lightComponent != null && lightComponent.type == LightType.Directional)
                    {
                        sunOrMoonLight = lightComponent;
                        break;
                    }
                }
            }

            if (followTarget == null && Camera.main != null)
            {
                followTarget = Camera.main.transform;
            }

            if (tintCamera == null)
            {
                tintCamera = Camera.main;
            }

            if (useMaterialAdapter && materialAdapter == null)
            {
                materialAdapter = GetComponent<EnvironmentMaterialWeatherAdapter>();
                if (materialAdapter == null)
                {
                    materialAdapter = FindFirstObjectByType<EnvironmentMaterialWeatherAdapter>();
                }
            }
        }

        private void CacheOriginalLighting()
        {
            originalFogEnabled = RenderSettings.fog;
            originalFogMode = RenderSettings.fogMode;
            originalFogColor = RenderSettings.fogColor;
            originalFogDensity = RenderSettings.fogDensity;
            originalFogStartDistance = RenderSettings.fogStartDistance;
            originalFogEndDistance = RenderSettings.fogEndDistance;
            originalAmbientMode = RenderSettings.ambientMode;
            originalAmbientLight = RenderSettings.ambientLight;
            originalReflectionIntensity = RenderSettings.reflectionIntensity;
            originalSkybox = RenderSettings.skybox;

            if (sunOrMoonLight != null)
            {
                originalLightColor = sunOrMoonLight.color;
                originalLightIntensity = sunOrMoonLight.intensity;
                originalLightShadows = sunOrMoonLight.shadows;
            }
        }

        private ParticleSystem[] CreateRainRig()
        {
            GameObject rigObject = new GameObject("Generated Rain Rig");
            rigObject.transform.SetParent(transform, false);
            generatedRainRig = rigObject.transform;

            if (followTarget != null)
            {
                generatedRainRig.position = followTarget.position + rainFollowOffset;
            }

            ParticleSystem drops = CreateRainDrops(generatedRainRig);
            ParticleSystem sheetsNear = CreateRainSheets(generatedRainRig, "Generated Rain Sheets Near", 0.03f, 2.5f, 6.5f, 0.72f);
            ParticleSystem sheetsFar = CreateRainSheets(generatedRainRig, "Generated Rain Sheets Far", 0.018f, 5.5f, 12f, 0.48f);
            ParticleSystem mist = CreateRainMist(generatedRainRig);

            return new[] { drops, sheetsNear, sheetsFar, mist };
        }

        private ParticleSystem CreateRainDrops(Transform parent)
        {
            GameObject rainObject = new GameObject("Generated Rain Drops");
            rainObject.transform.SetParent(parent, false);

            ParticleSystem particles = rainObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.startLifetime = 1.15f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
            main.startColor = rainColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 9000;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = rainRate;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(rainAreaSize, 1f, rainAreaSize);

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.65f, 0.65f);
            velocity.y = ConstantRange(-Mathf.Abs(rainFallSpeed));
            velocity.z = ConstantRange(-2.5f);

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f);

            ParticleSystemRenderer rendererModule = particles.GetComponent<ParticleSystemRenderer>();
            rendererModule.renderMode = ParticleSystemRenderMode.Stretch;
            rendererModule.lengthScale = 2.4f;
            rendererModule.velocityScale = 0.12f;
            rendererModule.cameraVelocityScale = 0f;
            rendererModule.material = GetRainMaterial();

            return particles;
        }

        private ParticleSystem CreateRainSheets(Transform parent, string objectName, float alpha, float minSize, float maxSize, float speedScale)
        {
            GameObject rainObject = new GameObject(objectName);
            rainObject.transform.SetParent(parent, false);

            ParticleSystem particles = rainObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, 1.15f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = WithAlpha(rainColor, alpha);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 450;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = Mathf.Max(4f, rainRate * 0.028f);

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(rainAreaSize, 1f, rainAreaSize);

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            velocity.y = ConstantRange(-Mathf.Abs(rainFallSpeed) * speedScale);
            velocity.z = ConstantRange(-1.25f);

            ParticleSystemRenderer rendererModule = particles.GetComponent<ParticleSystemRenderer>();
            rendererModule.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            rendererModule.material = GetRainSheetMaterial();
            rendererModule.trailMaterial = null;

            return particles;
        }

        private ParticleSystem CreateRainMist(Transform parent)
        {
            GameObject mistObject = new GameObject("Generated Rain Mist");
            mistObject.transform.SetParent(parent, false);
            mistObject.transform.localPosition = new Vector3(0f, -rainFollowOffset.y * 0.55f, 0f);

            ParticleSystem particles = mistObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 4.8f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(8f, 18f);
            main.startColor = new Color(0.55f, 0.6f, 0.65f, 0.035f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 180;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = Mathf.Max(6f, rainRate * 0.008f);

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(rainAreaSize * 0.9f, 4f, rainAreaSize * 0.9f);

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.2f, 0.15f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.45f, 0.15f);

            ParticleSystemRenderer rendererModule = particles.GetComponent<ParticleSystemRenderer>();
            rendererModule.renderMode = ParticleSystemRenderMode.Billboard;
            rendererModule.material = GetRainMistMaterial();
            rendererModule.trailMaterial = null;

            return particles;
        }

        private void ConfigureRainRenderer()
        {
            EnsureRainSystems();
            if (rainSystems == null)
            {
                return;
            }

            foreach (ParticleSystem system in rainSystems)
            {
                if (system == null)
                {
                    continue;
                }

                ParticleSystemRenderer rendererModule = system.GetComponent<ParticleSystemRenderer>();
                if (rendererModule == null)
                {
                    continue;
                }

                rendererModule.trailMaterial = null;
            }

            if (rainParticles != null)
            {
                ParticleSystem.MainModule main = rainParticles.main;
                main.startColor = rainColor;

                ParticleSystemRenderer rendererModule = rainParticles.GetComponent<ParticleSystemRenderer>();
                if (rendererModule != null)
                {
                    rendererModule.material = GetRainMaterial();
                    rendererModule.renderMode = ParticleSystemRenderMode.Stretch;
                    rendererModule.lengthScale = 2.4f;
                    rendererModule.velocityScale = 0.12f;
                    rendererModule.cameraVelocityScale = 0f;
                }
            }
        }

        private Material GetRainMaterial()
        {
            if (generatedRainMaterial != null)
            {
                return generatedRainMaterial;
            }

            Shader shader =
                Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                Shader.Find("Particles/Standard Unlit") ??
                Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");

            generatedRainMaterial = shader != null
                ? new Material(shader)
                : new Material(Shader.Find("Sprites/Default"));
            generatedRainMaterial.name = "Generated Rain Material";

            if (generatedRainMaterial.HasProperty("_BaseColor"))
            {
                generatedRainMaterial.SetColor("_BaseColor", rainColor);
            }

            if (generatedRainMaterial.HasProperty("_Color"))
            {
                generatedRainMaterial.SetColor("_Color", rainColor);
            }

            return generatedRainMaterial;
        }

        private Material GetRainSheetMaterial()
        {
            if (generatedRainSheetMaterial != null)
            {
                return generatedRainSheetMaterial;
            }

            generatedRainSheetMaterial = CreateParticleMaterial(new Color(0.68f, 0.78f, 0.88f, 0.08f), "Generated Rain Sheet Material");
            return generatedRainSheetMaterial;
        }

        private Material GetRainMistMaterial()
        {
            if (generatedRainMistMaterial != null)
            {
                return generatedRainMistMaterial;
            }

            generatedRainMistMaterial = CreateParticleMaterial(new Color(0.52f, 0.58f, 0.64f, 0.04f), "Generated Rain Mist Material");
            return generatedRainMistMaterial;
        }

        private static Material CreateParticleMaterial(Color color, string materialName)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                Shader.Find("Particles/Standard Unlit") ??
                Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");

            Material material = shader != null
                ? new Material(shader)
                : new Material(Shader.Find("Sprites/Default"));
            material.name = materialName;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }

        private void StartRain()
        {
            EnsureRainSystems();
            if (rainSystems == null)
            {
                return;
            }

            ConfigureRainRenderer();

            foreach (ParticleSystem system in rainSystems)
            {
                if (system == null)
                {
                    continue;
                }

                ParticleSystem.ShapeModule shape = system.shape;
                if (shape.enabled && shape.shapeType == ParticleSystemShapeType.Box)
                {
                    float scaleMultiplier = system == rainParticles ? 1f : 0.9f;
                    shape.scale = new Vector3(rainAreaSize * scaleMultiplier, shape.scale.y, rainAreaSize * scaleMultiplier);
                }

                ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                if (velocity.enabled)
                {
                    velocity.space = ParticleSystemSimulationSpace.World;
                    if (system == rainParticles)
                    {
                        velocity.y = ConstantRange(-Mathf.Abs(rainFallSpeed));
                        velocity.z = ConstantRange(-2.5f);
                    }
                }

                if (!system.isPlaying)
                {
                    system.Play();
                }
            }
        }

        private void StopRain()
        {
            EnsureRainSystems();
            if (rainSystems == null)
            {
                return;
            }

            foreach (ParticleSystem system in rainSystems)
            {
                if (system != null && system.isPlaying)
                {
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private void EnsureRainSystems()
        {
            if (rainSystems != null && rainSystems.Length > 0)
            {
                return;
            }

            if (rainParticles != null)
            {
                rainSystems = new[] { rainParticles };
            }
        }

        private void CreateTintOverlayIfNeeded()
        {
            if (!useCameraTint || tintRenderer != null)
            {
                return;
            }

            if (tintCamera == null)
            {
                tintCamera = Camera.main;
            }

            if (tintCamera == null)
            {
                return;
            }

            Shader shader =
                Shader.Find("Sprites/Default") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color");
            tintMaterial = new Material(shader);
            tintMaterial.name = "Runtime Weather Camera Tint";
            tintMaterial.renderQueue = 5000;

            GameObject tintObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tintObject.name = "Weather Camera Tint";
            tintObject.transform.SetParent(tintCamera.transform, false);
            tintObject.transform.localRotation = Quaternion.identity;
            tintObject.transform.localPosition = new Vector3(0f, 0f, Mathf.Max(tintCamera.nearClipPlane + 0.02f, tintPlaneDistance));

            Collider tintCollider = tintObject.GetComponent<Collider>();
            if (tintCollider != null)
            {
                Destroy(tintCollider);
            }

            tintRenderer = tintObject.GetComponent<MeshRenderer>();
            tintRenderer.sharedMaterial = tintMaterial;
            tintRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tintRenderer.receiveShadows = false;
            tintObject.SetActive(false);
            UpdateTintOverlayScale();
        }

        private void SetCameraTint(bool enabled, Color color)
        {
            if (!useCameraTint)
            {
                return;
            }

            CreateTintOverlayIfNeeded();
            if (tintRenderer == null || tintMaterial == null)
            {
                return;
            }

            tintRenderer.gameObject.SetActive(enabled && color.a > 0.001f);
            if (tintMaterial.HasProperty("_Color"))
            {
                tintMaterial.SetColor("_Color", color);
            }

            if (tintMaterial.HasProperty("_BaseColor"))
            {
                tintMaterial.SetColor("_BaseColor", color);
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static ParticleSystem.MinMaxCurve ConstantRange(float value)
        {
            return new ParticleSystem.MinMaxCurve(value, value);
        }

        private void UpdateTintOverlayScale()
        {
            if (!useCameraTint || tintRenderer == null || tintCamera == null)
            {
                return;
            }

            Transform tintTransform = tintRenderer.transform;
            float distance = Mathf.Max(tintCamera.nearClipPlane + 0.02f, tintPlaneDistance);
            tintTransform.localPosition = new Vector3(0f, 0f, distance);

            float height = 2f * Mathf.Tan(tintCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
            float width = height * tintCamera.aspect;
            tintTransform.localScale = new Vector3(width * 1.15f, height * 1.15f, 1f);
        }

        private static bool WasKeyPressed(KeyCode keyCode)
        {
            if (Keyboard.current == null)
            {
                return false;
            }

            Key key = ConvertKeyCode(keyCode);
            return key != Key.None && Keyboard.current[key].wasPressedThisFrame;
        }

        private bool WasVrCyclePressed()
        {
            if (!enableVrButtonCycling)
            {
                return false;
            }

            if (!rightController.isValid)
            {
                rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            }

            bool pressed = rightController.isValid &&
                           rightController.TryGetFeatureValue(XRCommonUsages.primaryButton, out bool value) &&
                           value;
            bool wasPressedThisFrame = pressed && !vrButtonWasPressed;
            vrButtonWasPressed = pressed;
            return wasPressedThisFrame;
        }

        private static Key ConvertKeyCode(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.F: return Key.F;
                case KeyCode.C: return Key.C;
                case KeyCode.N: return Key.N;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Space: return Key.Space;
                default: return Key.None;
            }
        }

        private void OnGUI()
        {
            if (!showWeatherOverlay)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(Screen.width - 240f, 20f, 220f, 64f), GUI.skin.box);
            GUILayout.Label($"Weather: {currentWeather}");
            GUILayout.Label($"{cycleWeatherKey} / Right A: cycle");
            GUILayout.EndArea();
        }
    }
}
