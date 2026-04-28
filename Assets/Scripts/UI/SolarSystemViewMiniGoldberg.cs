using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Partial SolarSystemView — Colorisation biome des mini-planètes Goldberg via le serveur.
/// StopMiniPlanetColorization, RestartMiniPlanetColorization, FetchAndColorizeMiniPlanets.
/// </summary>
public partial class SolarSystemView
{
    [Serializable] private struct MiniBodyEntry { public string bodyId; public string name; }
    [Serializable] private struct MiniBodyList  { public MiniBodyEntry[] items; }
    [Serializable] private struct MiniTileList  { public GoldbergTileState[] items; }

    private void StopMiniPlanetColorization()
    {
        if (_miniPlanetColorizeCoroutine == null)
            return;

        StopCoroutine(_miniPlanetColorizeCoroutine);
        _miniPlanetColorizeCoroutine = null;
    }

    private void RestartMiniPlanetColorization()
    {
        StopMiniPlanetColorization();

        if (goldbergMaterial == null || _miniMeshes.Count == 0 || !isActiveAndEnabled)
            return;

        _miniPlanetColorizeCoroutine = StartCoroutine(FetchAndColorizeMiniPlanets());
    }

    private IEnumerator FetchAndColorizeMiniPlanets()
    {
        int revision = _miniMeshRevision;
        string baseUrl = SimUrl.TrimEnd('/');
        var meshEntries = new List<KeyValuePair<string, GoldbergSphereGenerator.GoldbergMeshData>>(_miniMeshes);

        // 1) Résoudre name → bodyId
        Dictionary<string, string> nameToId = new Dictionary<string, string>();
        using (UnityWebRequest req = UnityWebRequest.Get(baseUrl + "/bodies"))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (revision != _miniMeshRevision || !isActiveAndEnabled)
                yield break;

            if (req.result == UnityWebRequest.Result.Success)
            {
                MiniBodyList list = JsonUtility.FromJson<MiniBodyList>("{\"items\":" + req.downloadHandler.text + "}");
                if (list.items != null)
                    foreach (MiniBodyEntry e in list.items)
                        if (!string.IsNullOrEmpty(e.name) && !string.IsNullOrEmpty(e.bodyId))
                            nameToId[e.name] = e.bodyId;
            }
        }

        if (nameToId.Count == 0)
        {
            Debug.LogWarning("[SolarSystemView] Aucun bodyId résolu depuis le serveur.");
            yield break;
        }

        Dictionary<TerrainType, Color> colorByType = terrainPalette != null
            ? terrainPalette.ToDictionary()
            : TerrainColorPalette.DefaultDictionary();

        // 2) Fetcher les tuiles de chaque planète et coloriser son mini-mesh
        foreach (KeyValuePair<string, GoldbergSphereGenerator.GoldbergMeshData> kv in meshEntries)
        {
            if (revision != _miniMeshRevision || !isActiveAndEnabled)
                yield break;

            string bodyName = kv.Key;
            GoldbergSphereGenerator.GoldbergMeshData md = kv.Value;

            if (!nameToId.TryGetValue(bodyName, out string bodyId)) continue;

            var allTiles = new List<GoldbergTileState>();
            int page = 0;
            const int pageSize = 200;

            while (true)
            {
                string url = $"{baseUrl}/bodies/{bodyId}/tiles?page={page}&size={pageSize}";
                using UnityWebRequest req = UnityWebRequest.Get(url);
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (revision != _miniMeshRevision || !isActiveAndEnabled)
                    yield break;

                if (req.result != UnityWebRequest.Result.Success) break;

                MiniTileList batch;
                try   { batch = JsonUtility.FromJson<MiniTileList>("{\"items\":" + req.downloadHandler.text + "}"); }
                catch { break; }
                if (batch.items == null || batch.items.Length == 0) break;
                allTiles.AddRange(batch.items);
                if (batch.items.Length < pageSize) break;
                page++;
            }

            if (allTiles.Count == 0) continue;

            if (!_miniMeshes.TryGetValue(bodyName, out GoldbergSphereGenerator.GoldbergMeshData currentMesh) || !ReferenceEquals(currentMesh.mesh, md.mesh))
                continue;

            GoldbergFaceColorizer.ColorizeFromServerTiles(currentMesh.faces, allTiles.ToArray(), colorByType);
            GoldbergSphereGenerator.ApplyFaceColors(currentMesh.mesh, currentMesh.faces, currentMesh.vertexFaceId);

            Debug.Log($"[SolarSystemView] {bodyName} : {allTiles.Count} tuiles → {currentMesh.faces.Length} faces colorisées.");

            // Étale légèrement le travail sur plusieurs frames pour éviter les pics au lancement.
            yield return null;
        }

        if (revision == _miniMeshRevision)
            _miniPlanetColorizeCoroutine = null;
    }
}
