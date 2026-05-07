using UnityEngine;

/// <summary>
/// Partial ViewManager — sous-vue planétaire (Globe Goldberg 3D ↔ Flat Mercator).
/// Méthodes : TogglePlanetView, ApplyPlanetSubView, ResetPlanetVisuals.
/// </summary>
public partial class ViewManager
{
    /// <summary>
    /// Bascule entre sous-vue Globe (Goldberg 3D) et Flat (Mercator) dans la Vue Planétaire.
    /// </summary>
    public void TogglePlanetView()
    {
        if (_state != ViewState.Planet) return;
        _planetSubView = _planetSubView == PlanetSubView.Globe ? PlanetSubView.Flat : PlanetSubView.Globe;
        ApplyPlanetSubView();
        Debug.Log($"[ViewManager] Toggle vue planète → {_planetSubView}");
    }

    private void ApplyPlanetSubView()
    {
        bool isGlobe = _planetSubView == PlanetSubView.Globe;

        if (isGlobe)
        {
            if (planetSphere      != null) planetSphere.gameObject.SetActive(true);
            if (planetTangentView != null) planetTangentView.gameObject.SetActive(false);
            if (planetFlatView    != null) planetFlatView.gameObject.SetActive(false);
            if (minimapController != null) minimapController.gameObject.SetActive(false);

            Vector3 pivot = planetSphere != null ? planetSphere.transform.position : Vector3.zero;
            cameraController.SetMode(CameraController.CameraMode.OrbitPerspective,
                                     planetOrbitMinDistance, planetOrbitMaxDistance, pivot);
            cameraController.SetOrbitPivot(pivot, planetOrbitStartDistance);
        }
        else
        {
            if (planetTangentView != null) planetTangentView.gameObject.SetActive(true);
            if (planetFlatView    != null) planetFlatView.gameObject.SetActive(true);  // minimap
            if (minimapController != null) minimapController.gameObject.SetActive(true);
            // La sphère reste active pendant la transition, sera désactivée dans le callback

            Vector3 focus = planetSphere != null
                ? planetSphere.LastClickedFaceCentroid
                : Vector3.up * GoldbergSphereGenerator.VisualRadius;

            planetTangentView?.SetFocusAndEnter(focus, onComplete: () =>
            {
                if (planetSphere != null) planetSphere.gameObject.SetActive(false);
            });

            cameraController.SetMode(CameraController.CameraMode.OrthoTopDown, planetH3MinZoom, planetH3MaxZoom);
            cameraController.FocusOn(Vector3.zero, planetH3StartZoom);
        }
    }

    private void ResetPlanetVisuals()
    {
        if (planetSphere      != null) planetSphere.gameObject.SetActive(true);
        if (planetFlatView    != null) planetFlatView.gameObject.SetActive(false);
        if (planetTangentView != null) planetTangentView.gameObject.SetActive(false);
        if (minimapController != null) minimapController.gameObject.SetActive(false);

        if (hexGridRoot != null && _state != ViewState.Local)
            hexGridRoot.SetActive(false);

        if (planetSphere != null && planetSphere.LastClickedFaceId >= 0)
            planetSphere.RestoreFaceOnSphere(planetSphere.LastClickedFaceId);
    }
}
