using UnityEngine;

/// <summary>
/// Partial ViewManager — Vue locale et overlay hex sur globe.
/// ShowLocalView, EnterLocalFromSelection, PlaceHexGridOnGlobe, CloseLocalOverlay.
/// </summary>
public partial class ViewManager
{
    /// <summary>
    /// Ouvre la Vue Locale sur la région à la lat/lon donnée.
    /// Appelé depuis un clic en sous-vue Flat ou Plan Tangent.
    /// </summary>
    public void ShowLocalView(float latitude, float longitude)
    {
        if (_activePlanet == null) return;

        cameraController?.SetOrbitKeyboardPanEnabled(false);

        _previousStateBeforeLocal   = _state;
        _previousSubViewBeforeLocal = _planetSubView;

        MapRegion region = BuildRegion(
            Mathf.Clamp01(latitude),
            Mathf.Clamp01(longitude),
            _activeProjectionOverride);

        SetActiveRoot(hexGridRoot);
        hexGrid.LoadRegion(region);
        ApplyLocalRuntimeContext(region);

        Bounds gridBounds = hexGrid.GetWorldBounds();
        float fittedZoom  = Mathf.Max(gridBounds.size.x, gridBounds.size.z) * 1.35f;
        float appliedZoom = Mathf.Max(localStartZoom, fittedZoom);

        cameraController.SetMode(CameraController.CameraMode.OrthoTopDown, localMinZoom, localMaxZoom);
        cameraController.FocusOn(gridBounds.center, appliedZoom);

        _state = ViewState.Local;
        OnViewChanged?.Invoke(_state);
        Debug.Log($"[ViewManager] → Vue Locale | lat={latitude:F3} lon={longitude:F3} | {_activePlanet.bodyName}");
        RequestAuthoritativeRegionSync(latitude, longitude);
    }

    /// <summary>
    /// Ouvre la Vue Locale pour la dernière tuile sélectionnée en vue Globe.
    /// Appelé par le bouton "Voir en local" du HUD.
    /// Mode overlay : le globe reste visible, la grille hex se superpose à la face cliquée.
    /// </summary>
    public void EnterLocalFromSelection()
    {
        if (!_hasGlobeSelection || _activePlanet == null || planetSphere == null) return;
        if (_state != ViewState.Planet) return;

        _previousSubViewBeforeLocal = _planetSubView;

        // Activer hexGridRoot EN PARALLÈLE de planetRoot (pas SetActiveRoot qui cache le globe)
        if (hexGridRoot != null)
        {
            hexGridRoot.SetActive(true);
            hexGrid?.LoadRegion(BuildRegion(_selectedGlobeLat, _selectedGlobeLon, _activeProjectionOverride));
            PlaceHexGridOnGlobe();
        }

        // Orbiter vers la face
        cameraController?.OrbitToFace(
            planetSphere.LastClickedFaceCentroid.normalized,
            localOverlayOrbitDistance,
            localOverlayOrbitDuration);

        Debug.Log($"[ViewManager] Vue locale overlay → face {planetSphere.LastClickedFaceId}");
    }

    /// <summary>
    /// Place et dimensionne hexGridRoot exactement sur la face GP cliquée.
    /// Appelé depuis EnterLocalFromSelection après activation de hexGridRoot.
    /// </summary>
    private void PlaceHexGridOnGlobe()
    {
        if (hexGridRoot == null || planetSphere == null) return;

        Vector3 dir = planetSphere.LastClickedFaceCentroid.normalized;

        // ── Scale : rayon englobant réel de la grille flat-top en espace local XZ ──
        // L'étendue Z (R * verticalSpacing + innerRadius) est plus grande que X
        // et représente le vrai rayon du cercle englobant la grille.
        float faceRadius = planetSphere.GetFaceRadius(planetSphere.LastClickedFaceId);
        int   gridRadius = hexGrid != null ? hexGrid.Radius : 5;
        float xBound     = gridRadius * HexMetrics.horizontalSpacing + HexMetrics.outerRadius;
        float zBound     = gridRadius * HexMetrics.verticalSpacing   + HexMetrics.innerRadius;
        float hexBoundRadius = Mathf.Max(xBound, zBound);
        float scale = faceRadius / hexBoundRadius;
        hexGridRoot.transform.localScale = Vector3.one * scale;

        // ── Orientation : local Y = outward, local Z = nord, local X = est ──
        // LookRotation(forward=northTangent, up=dir) → Z→nord, Y→outward, X→est.
        // La caméra (LookAt sphere center) a aussi northTangent comme viewport-up → alignement garanti.
        Vector3 worldRef     = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
        Vector3 northTangent = Vector3.ProjectOnPlane(worldRef, dir).normalized;
        hexGridRoot.transform.rotation = Quaternion.LookRotation(northTangent, dir);

        // ── Position : collé à la surface (offset minimal anti z-fighting) ──
        hexGridRoot.transform.position = dir * (GoldbergSphereGenerator.VisualRadius + localOverlaySurfaceOffset);

        // ── Coordonnées sphériques par cellule (nécessaire pour le serveur dédié) ──
        if (hexGrid != null)
        {
            HexCell[] cells = hexGrid.GetCells();
            if (cells != null)
            {
                foreach (HexCell cell in cells)
                {
                    Vector3 worldPos  = hexGridRoot.transform.TransformPoint(cell.center);
                    Vector3 sphereDir = worldPos.normalized;
                    float latDeg = Mathf.Asin(Mathf.Clamp(sphereDir.y, -1f, 1f)) * Mathf.Rad2Deg;
                    float lonDeg = Mathf.Atan2(sphereDir.z, sphereDir.x) * Mathf.Rad2Deg;
                    cell.latOnSphere = (latDeg + 90f)  / 180f;
                    cell.lonOnSphere = (lonDeg + 180f) / 360f;
                }
            }
        }

        // ── Masquer la face GP remplacée visuellement ──
        planetSphere.HideFaceOnSphere(planetSphere.LastClickedFaceId);

        Debug.Log($"[ViewManager] PlaceHexGridOnGlobe | faceR={faceRadius:F3} | hexBound={hexBoundRadius:F1} | scale={scale:F5}");
    }

    /// <summary>
    /// Ferme la vue locale (overlay globe ou vue locale standard) et retourne au Globe.
    /// Appelé par le bouton "Fermer" du HUD.
    /// </summary>
    public void CloseLocalOverlay()
    {
        // Restaure la face GP masquée si nécessaire
        if (planetSphere != null && planetSphere.LastClickedFaceId >= 0)
            planetSphere.RestoreFaceOnSphere(planetSphere.LastClickedFaceId);

        // Cas overlay globe : hexGridRoot visible par-dessus planetRoot (state = Planet)
        if (_state == ViewState.Planet && hexGridRoot != null && hexGridRoot.activeSelf)
        {
            hexGridRoot.SetActive(false);
            Debug.Log("[ViewManager] Vue locale overlay fermée → retour Globe");
            return;
        }

        // Cas standard : on était en ViewState.Local
        if (_state == ViewState.Local && _activePlanet != null)
        {
            ShowProjectedPlanet(_activePlanet, _activeProjectionOverride, _activeProjectionWaterLevel);
            if (_previousSubViewBeforeLocal == PlanetSubView.Flat)
            {
                _planetSubView = PlanetSubView.Flat;
                ApplyPlanetSubView();
            }
            Debug.Log("[ViewManager] Vue locale fermée → retour Planet");
        }
    }
}
