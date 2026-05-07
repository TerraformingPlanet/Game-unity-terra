using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Partial SolarSystemView — Chargement dynamique depuis le serveur dédié.
/// DTOs, helpers statiques d'inférence de profil, LoadFromServer.
/// </summary>
public partial class SolarSystemView
{
    // =========================================================
    // DTOs (désérialisés depuis le JSON serveur)
    // =========================================================

    [Serializable] private class OrbitalParamsDto  { public float semiMajorAxisAU; public float eccentricity; public float initialPhaseDeg; public int periodTicks; }
    [Serializable] private class BodyDto           { public string bodyId; public string name; public int bodyType; public string parentId; public string spectralType; public float radiusKm; public float waterLevel; public OrbitalParamsDto orbitalParams; }
    [Serializable] private class SystemDto         { public string systemId; public string name; public string rootBodyId; public string[] bodyIds; }
    [Serializable] private class BodyListWrapper   { public BodyDto[] items; }
    [Serializable] private class SystemListWrapper { public SystemDto[] items; }

    // Transfert de résultats entre sous-coroutines
    private SystemDto _pendingSystem;
    private BodyDto[] _pendingBodies;

    // =========================================================
    // Helpers statiques — conversion DTO → OrbitalBody
    // =========================================================

    private static StarType SpectralTypeFromServer(string spectralType)
    {
        if (string.IsNullOrWhiteSpace(spectralType)) return StarType.G;
        switch (char.ToUpperInvariant(spectralType[0]))
        {
            case 'M': return StarType.M;
            case 'K': return StarType.K;
            case 'G': return StarType.G;
            case 'F': return StarType.F;
            case 'A': return StarType.A;
            default: return StarType.G;
        }
    }

    private static float StableOrbitalPhase01(string bodyId, float currentPhase)
    {
        if (currentPhase > 0.0001f) return Mathf.Repeat(currentPhase, 1f);
        if (string.IsNullOrEmpty(bodyId)) return 0f;

        unchecked
        {
            int hash = 17;
            for (int i = 0; i < bodyId.Length; i++)
                hash = hash * 31 + bodyId[i];
            int positiveHash = hash & 0x7fffffff;
            return (positiveHash % 360) / 360f;
        }
    }

    private static PlanetaryPhysics InferPhysics(BodyDto dto)
    {
        float waterLevel = Mathf.Clamp01(dto.waterLevel);
        float baseTemperature = Mathf.Lerp(-55f, 18f, waterLevel);

        return new PlanetaryPhysics
        {
            baseEquatorTemperature = baseTemperature,
            rotationSpeed = Mathf.Lerp(0.35f, 0.78f, 1f - Mathf.Abs(waterLevel - 0.5f)),
            axialTilt = Mathf.Lerp(6f, 26f, Mathf.Clamp01(dto.radiusKm / 7000f)),
        };
    }

    private static GeologicalProfile InferGeology(BodyDto dto, ServerBodyType bodyType)
    {
        float clampedWater = Mathf.Clamp01(dto.waterLevel);
        float geologicalActivity = bodyType == ServerBodyType.Moon
            ? Mathf.Lerp(0.15f, 0.55f, clampedWater)
            : Mathf.Lerp(0.2f, 0.65f, 1f - clampedWater * 0.4f);

        if (bodyType == ServerBodyType.GasGiant)
            geologicalActivity = 0f;

        return new GeologicalProfile
        {
            waterAbundance = clampedWater,
            geologicalActivity = geologicalActivity,
            mineralRichness = Mathf.Lerp(0.35f, 0.75f, Mathf.Clamp01(dto.radiusKm / 9000f)),
            magneticField = dto.radiusKm >= 3000f,
        };
    }

    private static AtmosphericComposition InferAtmosphere(BodyDto dto, ServerBodyType bodyType)
    {
        float clampedWater = Mathf.Clamp01(dto.waterLevel);

        if (bodyType == ServerBodyType.GasGiant)
            return InferAtmosphereGasGiant();

        if (bodyType == ServerBodyType.Moon)
            return InferAtmosphereMoon(clampedWater);

        if (clampedWater >= 0.45f)
        {
            AtmosphericComposition atmosphere = AtmosphericComposition.EarthLike;
            atmosphere.density = Mathf.Lerp(0.55f, 0.92f, clampedWater);
            return atmosphere;
        }

        if (clampedWater <= 0.08f)
            return AtmosphericComposition.Mars;

        return new AtmosphericComposition
        {
            density = Mathf.Lerp(0.15f, 0.55f, clampedWater),
            n2Ratio = 0.62f,
            o2Ratio = Mathf.Lerp(0.01f, 0.12f, clampedWater),
            co2Ratio = Mathf.Lerp(0.28f, 0.08f, clampedWater),
            ch4Ratio = 0.01f,
            toxinRatio = Mathf.Lerp(0.08f, 0.01f, clampedWater),
        };
    }

    private static AtmosphericComposition InferAtmosphereGasGiant()
    {
        return new AtmosphericComposition
        {
            density = 1f,
            n2Ratio = 0.05f,
            o2Ratio = 0f,
            co2Ratio = 0.05f,
            ch4Ratio = 0.12f,
            toxinRatio = 0.08f,
        };
    }

    private static AtmosphericComposition InferAtmosphereMoon(float clampedWater)
    {
        return clampedWater >= 0.35f
            ? AtmosphericComposition.IcyMoon
            : new AtmosphericComposition
            {
                density = 0.03f,
                n2Ratio = 0.82f,
                o2Ratio = 0f,
                co2Ratio = 0.08f,
                ch4Ratio = 0.02f,
                toxinRatio = 0f,
            };
    }

    private static void PopulateOrbitalBodyProfile(OrbitalBody body, BodyDto dto, ServerBodyType bodyType)
    {
        if (body == null || dto == null)
            return;

        body.physics = InferPhysics(dto);
        body.geology = InferGeology(dto, bodyType);
        body.atmosphere = InferAtmosphere(dto, bodyType);
    }

    private static OrbitalBody BuildOrbitalBodyFromDto(BodyDto dto)
    {
        ServerBodyType bodyType = (ServerBodyType)dto.bodyType;

        if (bodyType == ServerBodyType.GasGiant)
        {
            GasGiant gasGiant = ScriptableObject.CreateInstance<GasGiant>();
            gasGiant.bodyName = dto.name;
            gasGiant.radius = dto.radiusKm;
            gasGiant.displayColor = new Color(0.8f, 0.6f, 0.3f);
            PopulateOrbitalBodyProfile(gasGiant, dto, bodyType);
            return gasGiant;
        }

        if (bodyType == ServerBodyType.Moon)
        {
            Moon moon = ScriptableObject.CreateInstance<Moon>();
            moon.bodyName = dto.name;
            moon.radius = dto.radiusKm;
            moon.displayColor = Color.gray;
            moon.moonType = dto.waterLevel >= 0.35f ? MoonType.Oceanic : MoonType.Rocky;
            PopulateOrbitalBodyProfile(moon, dto, bodyType);
            return moon;
        }

        Planet planet = ScriptableObject.CreateInstance<Planet>();
        planet.bodyName = dto.name;
        planet.radius = dto.radiusKm;
        planet.displayColor = WaterLevelToColor(dto.waterLevel);
        planet.planetType = dto.waterLevel >= 0.75f
            ? PlanetType.OceanWorld
            : dto.waterLevel <= 0.08f
                ? PlanetType.Desert
                : PlanetType.Rocky;
        PopulateOrbitalBodyProfile(planet, dto, bodyType);
        return planet;
    }

    private static Color WaterLevelToColor(float w)
    {
        if (w > 0.6f)  return new Color(0.2f, 0.4f, 0.9f);   // océan
        if (w > 0.3f)  return new Color(0.3f, 0.7f, 0.4f);   // côtier
        if (w > 0.05f) return new Color(0.7f, 0.6f, 0.3f);   // aride
        return new Color(0.6f, 0.5f, 0.4f);                   // rocheux/désert
    }

    // =========================================================
    // Helpers instance
    // =========================================================

    private void RegisterDebugInfo(OrbitalBody body, BodyDto dto, string parentName)
    {
        if (body == null || dto == null)
            return;

        _debugInfoByBody[body] = new BodyDebugInfo
        {
            typeName = ((ServerBodyType)dto.bodyType).ToString(),
            parentName = parentName,
            semiMajorAxisAu = dto.orbitalParams != null ? dto.orbitalParams.semiMajorAxisAU : 0f,
            orbitalPeriodDays = dto.orbitalParams != null ? Mathf.Max(1f, dto.orbitalParams.periodTicks) : 0f,
        };
    }

    private void LogSystemValidation(SolarSystemData data)
    {
        if (data == null || !logSystemValidationOnLoad)
            return;

        Debug.Log($"[SolarSystemView] Validation système: {data.BuildDebugSummary()}");
        SolarSystemData.ValidationIssue[] issues = data.ValidateHierarchy();
        foreach (SolarSystemData.ValidationIssue issue in issues)
        {
            if (string.Equals(issue.severity, "error", StringComparison.OrdinalIgnoreCase))
                Debug.LogError("[SolarSystemView] Validation système: " + issue.message);
            else
                Debug.LogWarning("[SolarSystemView] Validation système: " + issue.message);
        }
    }

    // =========================================================
    // LoadFromServer — coroutine principale de chargement serveur
    // =========================================================

    /// <summary>
    /// Fetch le serveur, reconstruit un SolarSystemData temporaire en mémoire
    /// et recharge la vue. Appeler depuis une coroutine (StartCoroutine).
    /// </summary>
    public IEnumerator LoadFromServer(string serverUrl, float timeoutSeconds = 2f)
    {
        int requestGeneration = ++_serverLoadGeneration;
        string baseUrl = serverUrl.TrimEnd('/');
        int timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds));

        yield return StartCoroutine(FetchSystemData(baseUrl, timeout, requestGeneration));

        BodyDto[] bodies = _pendingBodies;
        SystemDto activeSystem = _pendingSystem;
        _pendingSystem = null;
        _pendingBodies = null;

        if (bodies == null || bodies.Length == 0)
        {
            Debug.Log("[SolarSystemView] Chargement serveur ignoré: aucun corps reçu.");
            yield break;
        }

        bodies = FilterBodiesByActiveSystem(bodies, activeSystem);

        if (bodies.Length == 0)
        {
            Debug.LogWarning("[SolarSystemView] Aucun corps dans le système actif.");
            yield break;
        }

        BuildAndLoadSolarSystem(activeSystem, bodies);
    }

    private IEnumerator FetchSystemData(string baseUrl, int timeout, int requestGeneration)
    {
        const int maxAttempts = 3;
        const float retryDelaySeconds = 0.25f;
        _pendingSystem = null;
        _pendingBodies = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            yield return StartCoroutine(FetchActiveSystem(baseUrl, timeout));
            if (requestGeneration != _serverLoadGeneration || !isActiveAndEnabled || !gameObject.activeInHierarchy)
                yield break;

            yield return StartCoroutine(FetchBodiesList(baseUrl, timeout));
            if (requestGeneration != _serverLoadGeneration || !isActiveAndEnabled || !gameObject.activeInHierarchy)
                yield break;

            if (_pendingBodies != null && _pendingBodies.Length > 0)
                yield break;

            if (attempt < maxAttempts)
                yield return new WaitForSeconds(retryDelaySeconds);
        }
    }

    private IEnumerator FetchActiveSystem(string baseUrl, int timeout)
    {
        _pendingSystem = null;
        using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/galaxy/systems"))
        {
            req.timeout = timeout;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
                SystemListWrapper list = JsonUtility.FromJson<SystemListWrapper>(wrapped);
                if (list?.items != null && list.items.Length > 0)
                    _pendingSystem = list.items[0];
            }
        }
    }

    private IEnumerator FetchBodiesList(string baseUrl, int timeout)
    {
        _pendingBodies = null;
        using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/bodies"))
        {
            req.timeout = timeout;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                string wrapped = "{\"items\":" + req.downloadHandler.text + "}";
                BodyListWrapper list = JsonUtility.FromJson<BodyListWrapper>(wrapped);
                _pendingBodies = list?.items;
            }
        }
    }

    private static BodyDto[] FilterBodiesByActiveSystem(BodyDto[] bodies, SystemDto activeSystem)
    {
        if (activeSystem?.bodyIds == null || activeSystem.bodyIds.Length == 0)
            return bodies;
        var idSet = new HashSet<string>(activeSystem.bodyIds);
        var filtered = new List<BodyDto>();
        foreach (var b in bodies)
            if (idSet.Contains(b.bodyId)) filtered.Add(b);
        return filtered.ToArray();
    }

    private void BuildAndLoadSolarSystem(SystemDto activeSystem, BodyDto[] bodies)
    {
        SolarSystemData data = ScriptableObject.CreateInstance<SolarSystemData>();
        data.systemName = activeSystem?.name ?? "Système";

        BodyDto rootBody = activeSystem != null
            ? System.Array.Find(bodies, b => b.bodyId == activeSystem.rootBodyId)
            : System.Array.Find(bodies, b => b.bodyType == (int)ServerBodyType.Star);
        if (rootBody != null)
        {
            StarBody star = ScriptableObject.CreateInstance<StarBody>();
            star.spectralType = SpectralTypeFromServer(rootBody.spectralType);
            star.ApplySpectralDefaults();
            star.bodyName = rootBody.name;
            star.radius = rootBody.radiusKm;
            data.primaryStar = star;
        }

        var slotsByBodyId = new Dictionary<string, OrbitalSlot>();
        foreach (BodyDto b in bodies)
        {
            if (b.bodyType == (int)ServerBodyType.Star) continue;
            OrbitalBody body = BuildOrbitalBodyFromDto(b);
            OrbitalParameters orbit = new OrbitalParameters();
            if (b.orbitalParams != null)
            {
                orbit.semiMajorAxis = b.orbitalParams.semiMajorAxisAU;
                orbit.eccentricity  = b.orbitalParams.eccentricity;
                orbit.currentOrbitalPosition = StableOrbitalPhase01(b.bodyId, b.orbitalParams.initialPhaseDeg / 360f);
                orbit.orbitalPeriodDays = Mathf.Max(1f, b.orbitalParams.periodTicks);
            }
            else
            {
                orbit.currentOrbitalPosition = StableOrbitalPhase01(b.bodyId, 0f);
            }
            slotsByBodyId[b.bodyId] = new OrbitalSlot { body = body, orbit = orbit, moons = new OrbitalSlot[0] };
        }

        OrbitalSlot[] topSlots = PopulateBodyHierarchy(slotsByBodyId, bodies);
        data.orbitalSlots = topSlots;
        LoadSystem(data);
        LogSystemValidation(data);
        Debug.Log($"[SolarSystemView] Système chargé depuis serveur : {data.systemName} ({topSlots.Length} corps top-level)");
    }

    private OrbitalSlot[] PopulateBodyHierarchy(Dictionary<string, OrbitalSlot> slotsByBodyId, BodyDto[] bodies)
    {
        var topLevelSlots = new List<OrbitalSlot>();
        foreach (BodyDto b in bodies)
        {
            if (b.bodyType == (int)ServerBodyType.Star) continue;
            if (!slotsByBodyId.TryGetValue(b.bodyId, out OrbitalSlot slot)) continue;

            if (!string.IsNullOrEmpty(b.parentId) && slotsByBodyId.TryGetValue(b.parentId, out OrbitalSlot parentSlot))
            {
                var moonSlots = new List<OrbitalSlot>(parentSlot.moons ?? new OrbitalSlot[0]) { slot };
                moonSlots.Sort((left, right) => left.orbit.semiMajorAxis.CompareTo(right.orbit.semiMajorAxis));
                parentSlot.moons = moonSlots.ToArray();
                RegisterDebugInfo(slot.body, b, parentSlot.body != null ? parentSlot.body.bodyName : null);
            }
            else
            {
                topLevelSlots.Add(slot);
                RegisterDebugInfo(slot.body, b, null);
            }
        }
        topLevelSlots.Sort((left, right) => left.orbit.semiMajorAxis.CompareTo(right.orbit.semiMajorAxis));
        return topLevelSlots.ToArray();
    }
}
