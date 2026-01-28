using UnityEngine;

/// <summary>
/// Ajoute ce script à n'importe quel objet pour le transformer en source de lumière.
/// Crée automatiquement une Light et un matériau émissif.
/// </summary>
[ExecuteInEditMode]
public class GlowingLight : MonoBehaviour
{
    [Header("Light Settings")]
    [Tooltip("Couleur de la lumière")]
    public Color lightColor = new Color(1f, 0.9f, 0.7f, 1f);

    [Tooltip("Intensité de la lumière")]
    [Range(0f, 10f)]
    public float intensity = 2f;

    [Tooltip("Portée de la lumière")]
    [Range(0.1f, 50f)]
    public float range = 10f;

    [Tooltip("Type de lumière")]
    public LightType lightType = LightType.Point;

    [Header("Glow Settings")]
    [Tooltip("Intensité de l'émission du matériau")]
    [Range(0f, 10f)]
    public float emissionIntensity = 2f;

    [Tooltip("Garder l'apparence originale du matériau (texture, couleur)")]
    public bool preserveOriginalAppearance = true;

    [Tooltip("Activer les ombres")]
    public bool castShadows = true;

    [Header("Advanced")]
    [Tooltip("Adapter automatiquement la lumière à la taille de l'objet")]
    public bool autoFitToMesh = true;

    [Tooltip("Mode de distribution des lumières")]
    public LightDistribution lightDistribution = LightDistribution.Single;

    [Tooltip("Nombre de lumières le long de l'objet (pour Strip/Bar)")]
    [Range(2, 100)]
    public int lightCount = 5;

    [Tooltip("Axe principal pour Strip mode")]
    public Axis stripAxis = Axis.X;

    [Tooltip("Décalage de la lumière par rapport au centre")]
    public Vector3 lightOffset = Vector3.zero;

    public enum LightDistribution
    {
        Single,     // Une seule lumière au centre
        Strip,      // Lumières en ligne (barre LED)
        Box,        // Lumières sur les 6 faces
        Auto        // Détecte automatiquement la forme
    }

    public enum Axis { X, Y, Z }

    // Components
    private Light _light;
    private Light[] _additionalLights;
    private Renderer _renderer;
    private Material _emissiveMaterial;
    private Material _originalMaterial;
    private bool _materialCreated = false;
    private Bounds _meshBounds;
    private Vector3 _lastScale;
    private Transform _lightsHolder;

    void OnEnable()
    {
        _lastScale = transform.lossyScale;
        SetupLight();
        SetupEmissiveMaterial();
        UpdateLight();
    }

    void Update()
    {
        // Check if scale changed - reposition lights dynamically
        if (transform.lossyScale != _lastScale)
        {
            _lastScale = transform.lossyScale;
            RepositionLights();
        }
    }

    void RepositionLights()
    {
        // Recalculate mesh bounds with new scale
        CalculateMeshBounds();

        LightDistribution mode = lightDistribution;
        if (mode == LightDistribution.Auto)
        {
            mode = DetectBestDistribution();
        }

        if (mode == LightDistribution.Single)
        {
            // Reposition single light
            var mainLight = transform.Find("_MainLight");
            if (mainLight != null)
            {
                mainLight.localPosition = _meshBounds.center + lightOffset;
            }
        }
        else if (_additionalLights != null && _additionalLights.Length > 0)
        {
            // Update holder scale
            var lightsHolder = transform.Find("_GlowLights");
            if (lightsHolder != null)
            {
                Vector3 parentScale = transform.localScale;
                lightsHolder.localScale = new Vector3(
                    1f / parentScale.x,
                    1f / parentScale.y,
                    1f / parentScale.z
                );
            }

            // Reposition distributed lights
            Vector3[] positions = GetLightPositions(mode);
            Vector3 parentScale2 = transform.localScale;

            for (int i = 0; i < _additionalLights.Length && i < positions.Length; i++)
            {
                if (_additionalLights[i] != null)
                {
                    Vector3 scaledPos = Vector3.Scale(positions[i], parentScale2);
                    _additionalLights[i].transform.localPosition = scaledPos;
                }
            }
        }

        // Update light properties (range based on new size)
        UpdateLight();
    }

    void OnDisable()
    {
        // Restore original material
        if (_renderer != null && _originalMaterial != null)
        {
            _renderer.sharedMaterial = _originalMaterial;
        }

        // Cleanup created material
        if (_materialCreated && _emissiveMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_emissiveMaterial);
            else
                DestroyImmediate(_emissiveMaterial);
        }

        // Cleanup additional lights
        CleanupAdditionalLights();
    }

    void CleanupAdditionalLights()
    {
        // Cleanup additional lights
        var lightsHolder = transform.Find("_GlowLights");
        if (lightsHolder != null)
        {
            if (Application.isPlaying)
                Destroy(lightsHolder.gameObject);
            else
                DestroyImmediate(lightsHolder.gameObject);
        }
        _additionalLights = null;

        // Cleanup main light child
        var mainLight = transform.Find("_MainLight");
        if (mainLight != null)
        {
            if (Application.isPlaying)
                Destroy(mainLight.gameObject);
            else
                DestroyImmediate(mainLight.gameObject);
        }
        _light = null;
    }

    private int _lastLightCount;
    private LightDistribution _lastDistribution;

    void OnValidate()
    {
        // Check if we need to recreate lights (count or mode changed)
        bool needsRecreate = (_lastLightCount != lightCount) || (_lastDistribution != lightDistribution);
        _lastLightCount = lightCount;
        _lastDistribution = lightDistribution;

#if UNITY_EDITOR
        if (needsRecreate && (_additionalLights != null || _light != null))
        {
            // Schedule recreation for next frame (can't destroy in OnValidate)
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                CleanupAdditionalLights();
                SetupLight();
                SetupEmissiveMaterial();
                UpdateLight();
            };
        }
        else
#endif
        if (_light != null)
        {
            UpdateLight();
        }
    }

    void SetupLight()
    {
        // Get mesh bounds
        CalculateMeshBounds();

        // Determine actual distribution mode
        LightDistribution mode = lightDistribution;
        if (mode == LightDistribution.Auto)
        {
            mode = DetectBestDistribution();
        }

        // Setup lights based on distribution mode
        if (mode == LightDistribution.Single)
        {
            SetupSingleLight();
        }
        else
        {
            SetupDistributedLights(mode);
        }
    }

    LightDistribution DetectBestDistribution()
    {
        Vector3 size = _meshBounds.size;
        float maxDim = Mathf.Max(size.x, size.y, size.z);
        float minDim = Mathf.Min(size.x, size.y, size.z);

        // If one dimension is much larger than others, it's a strip/bar
        if (maxDim > minDim * 3f)
        {
            // Auto-detect strip axis
            if (size.x >= size.y && size.x >= size.z) stripAxis = Axis.X;
            else if (size.y >= size.x && size.y >= size.z) stripAxis = Axis.Y;
            else stripAxis = Axis.Z;

            return LightDistribution.Strip;
        }

        // If object is large, use box distribution
        if (_meshBounds.size.magnitude > 2f)
        {
            return LightDistribution.Box;
        }

        return LightDistribution.Single;
    }

    void SetupSingleLight()
    {
        // Create single light at mesh center
        var lightHolder = transform.Find("_MainLight");
        if (lightHolder == null)
        {
            var go = new GameObject("_MainLight");
            go.transform.SetParent(transform);
            go.transform.localPosition = _meshBounds.center + lightOffset;
            go.transform.localRotation = Quaternion.identity;
            lightHolder = go.transform;
            _light = go.AddComponent<Light>();
        }
        else
        {
            _light = lightHolder.GetComponent<Light>();
            lightHolder.localPosition = _meshBounds.center + lightOffset;
        }
    }

    void SetupDistributedLights(LightDistribution mode)
    {
        // Create holder for lights (with inverse scale to cancel parent scale)
        var lightsHolder = transform.Find("_GlowLights");
        if (lightsHolder == null)
        {
            var go = new GameObject("_GlowLights");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            lightsHolder = go.transform;
        }
        else
        {
            // Clear existing lights
            foreach (Transform child in lightsHolder)
            {
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        // Apply inverse scale so lights aren't distorted
        Vector3 parentScale = transform.localScale;
        lightsHolder.localScale = new Vector3(
            1f / parentScale.x,
            1f / parentScale.y,
            1f / parentScale.z
        );

        // Calculate positions (in scaled space, then unscale for child positioning)
        Vector3[] localPositions = GetLightPositions(mode);
        _additionalLights = new Light[localPositions.Length];

        for (int i = 0; i < localPositions.Length; i++)
        {
            var lightGO = new GameObject($"Light_{i}");
            lightGO.transform.SetParent(lightsHolder);

            // Scale the position to account for the inverse scale of holder
            Vector3 scaledPos = Vector3.Scale(localPositions[i], parentScale);
            lightGO.transform.localPosition = scaledPos;

            _additionalLights[i] = lightGO.AddComponent<Light>();
        }

        // Use first light as main reference
        if (_additionalLights.Length > 0)
        {
            _light = _additionalLights[0];
        }
    }

    void CalculateMeshBounds()
    {
        Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);

        // Try to get bounds from MeshFilter
        var meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            localBounds = meshFilter.sharedMesh.bounds;
        }
        else
        {
            // Try to get bounds from SkinnedMeshRenderer
            var skinnedMesh = GetComponent<SkinnedMeshRenderer>();
            if (skinnedMesh != null)
            {
                localBounds = skinnedMesh.localBounds;
            }
        }

        // Use unscaled bounds - child transforms inherit parent scale automatically
        _meshBounds = localBounds;
    }

    Vector3[] GetLightPositions(LightDistribution mode)
    {
        Vector3 center = _meshBounds.center;
        Vector3 extents = _meshBounds.extents;

        if (mode == LightDistribution.Strip)
        {
            // Distribute lights along the strip axis (LED bar)
            Vector3[] positions = new Vector3[lightCount];
            Vector3 axisDir = Vector3.zero;
            float length = 0f;

            switch (stripAxis)
            {
                case Axis.X:
                    axisDir = Vector3.right;
                    length = extents.x * 2f;
                    break;
                case Axis.Y:
                    axisDir = Vector3.up;
                    length = extents.y * 2f;
                    break;
                case Axis.Z:
                    axisDir = Vector3.forward;
                    length = extents.z * 2f;
                    break;
            }

            // Distribute lights along the axis
            Vector3 startPos = center - axisDir * (length * 0.45f);
            float step = (length * 0.9f) / Mathf.Max(1, lightCount - 1);

            for (int i = 0; i < lightCount; i++)
            {
                positions[i] = startPos + axisDir * (step * i) + lightOffset;
            }

            return positions;
        }
        else // Box mode
        {
            // 6 lights on each face of the bounding box
            return new Vector3[]
            {
                center + Vector3.up * extents.y * 0.8f + lightOffset,
                center + Vector3.down * extents.y * 0.8f + lightOffset,
                center + Vector3.left * extents.x * 0.8f + lightOffset,
                center + Vector3.right * extents.x * 0.8f + lightOffset,
                center + Vector3.forward * extents.z * 0.8f + lightOffset,
                center + Vector3.back * extents.z * 0.8f + lightOffset
            };
        }
    }

    void SetupEmissiveMaterial()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer == null) return;

        // Save original material
        if (_originalMaterial == null)
        {
            _originalMaterial = _renderer.sharedMaterial;
        }

        // Clone original material to preserve appearance (textures, colors, etc.)
        if (_originalMaterial != null)
        {
            _emissiveMaterial = new Material(_originalMaterial);
        }
        else
        {
            // Fallback: create new URP Lit material
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            _emissiveMaterial = new Material(shader);
        }

        _materialCreated = true;
        _renderer.material = _emissiveMaterial;

        // Enable emission keyword
        _emissiveMaterial.EnableKeyword("_EMISSION");
        _emissiveMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
    }

    void UpdateLight()
    {
        // Calculate auto-fit values
        float autoRange = range;
        float autoIntensity = intensity;

        if (autoFitToMesh)
        {
            // Range based on object size
            float meshSize = _meshBounds.size.magnitude;
            autoRange = Mathf.Max(range, meshSize * 2f);

            // Intensity scales slightly with size
            autoIntensity = intensity * Mathf.Max(1f, meshSize * 0.5f);
        }

        // Update main light
        if (_light != null)
        {
            _light.type = lightType;
            _light.color = lightColor;
            _light.intensity = autoIntensity;
            _light.range = autoRange;
            _light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;

            // Position light at mesh center + offset
            // Light component is on same GO, so we offset it via renderMode for Point lights
            // For proper offset, we'd need a child object - using lightOffset
        }

        // Update additional lights
        if (_additionalLights != null)
        {
            float splitIntensity = autoIntensity / (_additionalLights.Length + 1);
            float splitRange = autoRange * 0.6f;

            foreach (var light in _additionalLights)
            {
                if (light == null) continue;
                light.type = LightType.Point;
                light.color = lightColor;
                light.intensity = splitIntensity;
                light.range = splitRange;
                light.shadows = LightShadows.None; // Performance: no shadows on additional lights
            }

            // Reduce main light intensity when using multiple lights
            if (_light != null)
            {
                _light.intensity = splitIntensity;
            }
        }

        UpdateEmission();
    }

    void UpdateEmission()
    {
        if (_emissiveMaterial == null) return;

        // Determine emission color
        Color emissionColor;

        if (preserveOriginalAppearance && _originalMaterial != null)
        {
            // Use original material's base color for emission
            Color originalColor = Color.white;

            if (_originalMaterial.HasProperty("_BaseColor"))
                originalColor = _originalMaterial.GetColor("_BaseColor");
            else if (_originalMaterial.HasProperty("_Color"))
                originalColor = _originalMaterial.GetColor("_Color");

            emissionColor = originalColor * emissionIntensity;
        }
        else
        {
            // Use light color for emission
            emissionColor = lightColor * emissionIntensity;

            // Also update base color
            if (_emissiveMaterial.HasProperty("_BaseColor"))
                _emissiveMaterial.SetColor("_BaseColor", lightColor);
            if (_emissiveMaterial.HasProperty("_Color"))
                _emissiveMaterial.SetColor("_Color", lightColor);
        }

        // Apply emission
        _emissiveMaterial.SetColor("_EmissionColor", emissionColor);
    }

    /// <summary>
    /// Change la couleur de la lumière en runtime
    /// </summary>
    public void SetColor(Color color)
    {
        lightColor = color;
        UpdateLight();
    }

    /// <summary>
    /// Change l'intensité en runtime
    /// </summary>
    public void SetIntensity(float newIntensity)
    {
        intensity = newIntensity;
        UpdateLight();
    }

    /// <summary>
    /// Active/désactive la lumière
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        if (_light != null)
        {
            _light.enabled = enabled;
        }

        if (_emissiveMaterial != null)
        {
            Color emissionColor = enabled ? lightColor * emissionIntensity : Color.black;
            _emissiveMaterial.SetColor("_EmissionColor", emissionColor);
        }
    }

    /// <summary>
    /// Fait clignoter la lumière
    /// </summary>
    public void Flicker(float duration = 0.1f)
    {
        StartCoroutine(FlickerCoroutine(duration));
    }

    private System.Collections.IEnumerator FlickerCoroutine(float duration)
    {
        SetEnabled(false);
        yield return new WaitForSeconds(duration);
        SetEnabled(true);
    }

    void OnDrawGizmosSelected()
    {
        // Calculate bounds if not done
        if (_meshBounds.size == Vector3.zero)
        {
            CalculateMeshBounds();
        }

        // Draw mesh bounds
        Gizmos.color = new Color(lightColor.r, lightColor.g, lightColor.b, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(_meshBounds.center, _meshBounds.size);

        // Determine mode
        LightDistribution mode = lightDistribution;
        if (mode == LightDistribution.Auto)
        {
            mode = DetectBestDistribution();
        }

        // Draw light positions
        Gizmos.color = lightColor;
        if (mode == LightDistribution.Single)
        {
            Gizmos.DrawSphere(_meshBounds.center + lightOffset, 0.1f);
        }
        else
        {
            Vector3[] positions = GetLightPositions(mode);
            foreach (var pos in positions)
            {
                Gizmos.DrawSphere(pos, 0.05f);
            }

            // Draw line connecting lights for Strip mode
            if (mode == LightDistribution.Strip && positions.Length > 1)
            {
                Gizmos.color = new Color(lightColor.r, lightColor.g, lightColor.b, 0.5f);
                for (int i = 0; i < positions.Length - 1; i++)
                {
                    Gizmos.DrawLine(positions[i], positions[i + 1]);
                }
            }
        }

        // Draw light range
        Gizmos.color = new Color(lightColor.r, lightColor.g, lightColor.b, 0.1f);
        float displayRange = autoFitToMesh ? Mathf.Max(range, _meshBounds.size.magnitude * 2f) : range;
        Gizmos.DrawWireSphere(_meshBounds.center + lightOffset, displayRange * 0.5f);
    }
}
