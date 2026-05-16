using System.Collections.Generic;
using UnityEngine;

namespace DroneVR.Simulation
{
    public class EnvironmentMaterialWeatherAdapter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform environmentRoot;

        [Header("Conversion")]
        [SerializeField] private bool convertOnStart = true;
        [SerializeField] private bool restoreOriginalsOnDisable = true;
        [SerializeField] private bool castShadows = true;
        [SerializeField] private bool receiveShadows = true;

        [Header("Day Material")]
        [SerializeField] private bool leaveDayMaterialsUnchanged = true;

        [Header("Night Material")]
        [SerializeField] private Color nightTint = new Color(0.82f, 0.88f, 1f);
        [SerializeField, Range(0f, 2f)] private float nightBrightness = 0.55f;
        [SerializeField, Range(0f, 1f)] private float nightSmoothness = 0.22f;

        [Header("Rain Material")]
        [SerializeField] private Color rainTint = new Color(0.86f, 0.92f, 1f);
        [SerializeField, Range(0f, 2f)] private float rainBrightness = 0.78f;
        [SerializeField, Range(0f, 1f)] private float rainSmoothness = 0.45f;
        [SerializeField, Range(0f, 1f)] private float rainMetallic = 0f;

        private readonly List<RendererState> rendererStates = new List<RendererState>();
        private readonly Dictionary<Material, RuntimeMaterialSet> convertedMaterials = new Dictionary<Material, RuntimeMaterialSet>();
        private bool hasConverted;
        private WeatherMode activeWeather = WeatherMode.Day;

        private sealed class RendererState
        {
            public Renderer renderer;
            public Material[] originalSharedMaterials;
            public RuntimeMaterialSet[] runtimeMaterialSets;
        }

        private sealed class RuntimeMaterialSet
        {
            public Material day;
            public Material night;
            public Material rain;
        }

        private void Start()
        {
            if (convertOnStart)
            {
                ConvertEnvironmentMaterials();
                ApplyWeather(activeWeather);
            }
        }

        private void OnDisable()
        {
            if (restoreOriginalsOnDisable)
            {
                RestoreOriginalMaterials();
            }
        }

        private void OnDestroy()
        {
            DestroyRuntimeMaterials();
        }

        public void ApplyWeather(WeatherMode weather)
        {
            activeWeather = weather;

            if (!hasConverted)
            {
                ConvertEnvironmentMaterials();
            }

            foreach (RendererState state in rendererStates)
            {
                if (state.renderer == null || state.runtimeMaterialSets == null)
                {
                    continue;
                }

                if (weather == WeatherMode.Day && leaveDayMaterialsUnchanged)
                {
                    state.renderer.sharedMaterials = state.originalSharedMaterials;
                    continue;
                }

                Material[] materials = new Material[state.runtimeMaterialSets.Length];
                for (int i = 0; i < state.runtimeMaterialSets.Length; i++)
                {
                    RuntimeMaterialSet set = state.runtimeMaterialSets[i];
                    materials[i] = GetMaterialForWeather(set, weather);
                }

                state.renderer.sharedMaterials = materials;
                state.renderer.shadowCastingMode = castShadows
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
                state.renderer.receiveShadows = receiveShadows;
            }
        }

        [ContextMenu("Convert Environment Materials")]
        public void ConvertEnvironmentMaterials()
        {
            if (hasConverted)
            {
                return;
            }

            ResolveEnvironmentRoot();
            if (environmentRoot == null)
            {
                Debug.LogWarning("EnvironmentMaterialWeatherAdapter could not find an environment root.");
                return;
            }

            Renderer[] renderers = environmentRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer rendererComponent in renderers)
            {
                if (rendererComponent == null)
                {
                    continue;
                }

                Material[] originalMaterials = rendererComponent.sharedMaterials;
                RuntimeMaterialSet[] sets = new RuntimeMaterialSet[originalMaterials.Length];
                for (int i = 0; i < originalMaterials.Length; i++)
                {
                    sets[i] = GetOrCreateRuntimeSet(originalMaterials[i]);
                }

                rendererStates.Add(new RendererState
                {
                    renderer = rendererComponent,
                    originalSharedMaterials = originalMaterials,
                    runtimeMaterialSets = sets
                });
            }

            hasConverted = rendererStates.Count > 0;
        }

        [ContextMenu("Restore Original Materials")]
        public void RestoreOriginalMaterials()
        {
            foreach (RendererState state in rendererStates)
            {
                if (state.renderer != null && state.originalSharedMaterials != null)
                {
                    state.renderer.sharedMaterials = state.originalSharedMaterials;
                }
            }
        }

        private void ResolveEnvironmentRoot()
        {
            if (environmentRoot != null)
            {
                return;
            }

            PathGenerator pathGenerator = FindFirstObjectByType<PathGenerator>();
            if (pathGenerator != null && pathGenerator.EnvironmentRoot != null)
            {
                environmentRoot = pathGenerator.EnvironmentRoot;
                return;
            }

            Transform largestRoot = null;
            float largestVolume = 0f;
            foreach (GameObject rootObject in gameObject.scene.GetRootGameObjects())
            {
                if (rootObject == null || rootObject == gameObject)
                {
                    continue;
                }

                if (rootObject.GetComponentInChildren<Camera>() != null ||
                    rootObject.GetComponentInChildren<Light>() != null)
                {
                    continue;
                }

                if (!TryGetRendererBounds(rootObject.transform, out Bounds bounds))
                {
                    continue;
                }

                float volume = bounds.size.x * bounds.size.y * bounds.size.z;
                if (largestRoot == null || volume > largestVolume)
                {
                    largestRoot = rootObject.transform;
                    largestVolume = volume;
                }
            }

            environmentRoot = largestRoot;
        }

        private RuntimeMaterialSet GetOrCreateRuntimeSet(Material source)
        {
            if (source != null && convertedMaterials.TryGetValue(source, out RuntimeMaterialSet existingSet))
            {
                return existingSet;
            }

            RuntimeMaterialSet set = new RuntimeMaterialSet
            {
                day = CreateLitMaterial(source, Color.white, 1f, 0.18f, 0f, "Day"),
                night = CreateLitMaterial(source, nightTint, nightBrightness, nightSmoothness, 0f, "Night"),
                rain = CreateLitMaterial(source, rainTint, rainBrightness, rainSmoothness, rainMetallic, "Rain")
            };

            if (source != null)
            {
                convertedMaterials[source] = set;
            }

            return set;
        }

        private Material CreateLitMaterial(Material source, Color tint, float brightness, float smoothness, float metallic, string suffix)
        {
            Shader litShader =
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard");

            Material material = litShader != null
                ? new Material(litShader)
                : new Material(source);

            material.name = source != null ? $"{source.name}_{suffix}_RuntimeLit" : $"RuntimeLit_{suffix}";

            Texture baseTexture = GetTexture(source, "_BaseMap", "_MainTex", "_BaseColorMap");
            Color baseColor = GetColor(source, "_BaseColor", "_Color");
            Color finalColor = MultiplyRgb(baseColor, tint, brightness);
            finalColor.a = baseColor.a;

            SetTexture(material, baseTexture, "_BaseMap", "_MainTex");
            SetColor(material, finalColor, "_BaseColor", "_Color");
            SetFloat(material, Mathf.Clamp01(smoothness), "_Smoothness", "_Glossiness");
            SetFloat(material, Mathf.Clamp01(metallic), "_Metallic");
            SetFloat(material, 0f, "_SpecularHighlights");

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 0f);
            }

            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0f);
            }

            material.renderQueue = -1;
            return material;
        }

        private static Material GetMaterialForWeather(RuntimeMaterialSet set, WeatherMode weather)
        {
            if (set == null)
            {
                return null;
            }

            switch (weather)
            {
                case WeatherMode.Night:
                    return set.night;
                case WeatherMode.Rain:
                    return set.rain;
                default:
                    return set.day;
            }
        }

        private static Texture GetTexture(Material material, params string[] propertyNames)
        {
            if (material == null)
            {
                return null;
            }

            foreach (string propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                {
                    Texture texture = material.GetTexture(propertyName);
                    if (texture != null)
                    {
                        return texture;
                    }
                }
            }

            return null;
        }

        private static Color GetColor(Material material, params string[] propertyNames)
        {
            if (material == null)
            {
                return Color.white;
            }

            foreach (string propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                {
                    return material.GetColor(propertyName);
                }
            }

            return Color.white;
        }

        private static void SetTexture(Material material, Texture texture, params string[] propertyNames)
        {
            if (material == null || texture == null)
            {
                return;
            }

            foreach (string propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                {
                    material.SetTexture(propertyName, texture);
                }
            }
        }

        private static void SetColor(Material material, Color color, params string[] propertyNames)
        {
            if (material == null)
            {
                return;
            }

            foreach (string propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                {
                    material.SetColor(propertyName, color);
                }
            }
        }

        private static void SetFloat(Material material, float value, params string[] propertyNames)
        {
            if (material == null)
            {
                return;
            }

            foreach (string propertyName in propertyNames)
            {
                if (material.HasProperty(propertyName))
                {
                    material.SetFloat(propertyName, value);
                }
            }
        }

        private static Color MultiplyRgb(Color baseColor, Color tint, float brightness)
        {
            return new Color(
                Mathf.Clamp01(baseColor.r * tint.r * brightness),
                Mathf.Clamp01(baseColor.g * tint.g * brightness),
                Mathf.Clamp01(baseColor.b * tint.b * brightness),
                baseColor.a);
        }

        private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bounds = default;
            bool hasBounds = false;

            foreach (Renderer rendererComponent in renderers)
            {
                if (rendererComponent == null || !rendererComponent.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = rendererComponent.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rendererComponent.bounds);
                }
            }

            return hasBounds;
        }

        private void DestroyRuntimeMaterials()
        {
            foreach (RuntimeMaterialSet set in convertedMaterials.Values)
            {
                DestroyMaterial(set.day);
                DestroyMaterial(set.night);
                DestroyMaterial(set.rain);
            }

            convertedMaterials.Clear();
            rendererStates.Clear();
            hasConverted = false;
        }

        private static void DestroyMaterial(Material material)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }
}
