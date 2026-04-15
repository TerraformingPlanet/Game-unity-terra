using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Handles mouse input over the hex grid.
/// Fires a 3D Physics.Raycast against the HexMesh collider,
/// shows a tooltip on hover, and logs cell info on left-click.
/// </summary>
public class HexInput : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HexGrid hexGrid;
    [SerializeField] private GameObject tooltipPanel;
    [SerializeField] private Text tooltipText;

    private Camera _cam;

    private void Awake()
    {
        _cam = Camera.main;
        if (tooltipPanel != null) tooltipPanel.SetActive(false);
    }

    private void Update()
    {
        Ray ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            HexCell cell = hexGrid.GetCellAt(hit.point);
            if (cell != null)
            {
                ShowTooltip(cell);

                if (Mouse.current.leftButton.wasPressedThisFrame)
                    OnCellClick(cell);

                return;
            }
        }

        HideTooltip();
    }

    private void ShowTooltip(HexCell cell)
    {
        if (tooltipPanel == null) return;
        tooltipPanel.SetActive(true);
        string terrainName = cell.terrain != null ? cell.terrain.displayName : "Inconnu";
        tooltipText.text = $"{terrainName}\n({cell.Q}, {cell.R})";

        // Position tooltip near the mouse cursor
        tooltipPanel.transform.position = Mouse.current.position.ReadValue() + new Vector2(16f, -16f);
    }

    private void HideTooltip()
    {
        if (tooltipPanel != null) tooltipPanel.SetActive(false);
    }

    private void OnCellClick(HexCell cell)
    {
        string terrainName = cell.terrain != null ? cell.terrain.displayName : "Inconnu";
        Debug.Log($"[HexInput] Clicked: ({cell.Q}, {cell.R}) — {terrainName}");
    }
}
