using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor utility — configure les RectTransforms et câble les SerializeFields de LoginPanel.
/// Menu : Terraformation > Wire LoginPanel
/// </summary>
public static class WireLoginPanel
{
    [MenuItem("Terraformation/Wire LoginPanel")]
    static void Wire()
    {
        var panelGO = GameObject.Find("LoginPanel");
        if (panelGO == null) { Debug.LogError("[WireLoginPanel] LoginPanel not found in scene."); return; }

        // ── 1. RectTransform panneau : plein écran ──────────────────────────────
        var panelRT = panelGO.GetComponent<RectTransform>();
        if (panelRT == null) panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.anchorMin        = Vector2.zero;
        panelRT.anchorMax        = Vector2.one;
        panelRT.offsetMin        = Vector2.zero;
        panelRT.offsetMax        = Vector2.zero;

        // ── 2. Fond semi-transparent ────────────────────────────────────────────
        var bg = panelGO.transform.Find("Background");
        if (bg != null)
        {
            var bgRT = EnsureRT(bg.gameObject);
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            var img = bg.GetComponent<Image>();
            if (img == null) img = bg.gameObject.AddComponent<Image>();
            img.color = new Color(0.047f, 0.047f, 0.071f, 0.93f);  // #0c0c12 à 93 %
        }

        // ── helper layout centré ────────────────────────────────────────────────
        Vector2 mid = new Vector2(0.5f, 0.5f);
        void Place(string name, Vector2 pos, Vector2 size)
        {
            var t = panelGO.transform.Find(name);
            if (t == null) return;
            var rt = EnsureRT(t.gameObject);
            rt.anchorMin = rt.anchorMax = rt.pivot = mid;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        // ── 3. Titre ────────────────────────────────────────────────────────────
        Place("TitleText", new Vector2(0, 160), new Vector2(420, 50));
        var titleT = panelGO.transform.Find("TitleText");
        if (titleT != null)
        {
            var tmp = titleT.GetComponent<TextMeshProUGUI>();
            if (tmp == null) tmp = titleT.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text      = "TERRAFORMATION";
            tmp.fontSize  = 28;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = new Color(0.392f, 0.706f, 1f, 1f);  // #64b4ff
        }

        // ── 4. Champs de saisie ─────────────────────────────────────────────────
        Place("UsernameInput", new Vector2(0,  90), new Vector2(320, 40));
        Place("PasswordInput", new Vector2(0,  30), new Vector2(320, 40));
        Place("CorpNameInput", new Vector2(0, -30), new Vector2(320, 40));

        // Configurer le placeholder texte et content type mot de passe
        SetupInputField("UsernameInput", "Nom d'utilisateur", TMP_InputField.ContentType.Standard);
        SetupInputField("PasswordInput", "Mot de passe",       TMP_InputField.ContentType.Password);
        SetupInputField("CorpNameInput", "Nom de corporation (inscription)", TMP_InputField.ContentType.Standard);

        // ── 5. Status ────────────────────────────────────────────────────────────
        Place("StatusText", new Vector2(0, -85), new Vector2(380, 30));
        var statusT = panelGO.transform.Find("StatusText");
        if (statusT != null)
        {
            var tmp = statusT.GetComponent<TextMeshProUGUI>();
            if (tmp == null) tmp = statusT.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.fontSize  = 13;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = new Color(0.549f, 0.549f, 0.549f, 1f);  // #8c8c8c
            tmp.text      = "";
        }

        // ── 6. Boutons ───────────────────────────────────────────────────────────
        Place("LoginButton",    new Vector2(-85, -135), new Vector2(150, 40));
        Place("RegisterButton", new Vector2( 85, -135), new Vector2(150, 40));

        SetupButton("LoginButton",    "CONNEXION",  new Color(0.392f, 0.706f, 1f, 1f));
        SetupButton("RegisterButton", "S'INSCRIRE", new Color(0.22f, 0.22f, 0.28f, 1f));

        // ── 7. Câbler les SerializeFields via SerializedObject (accès privés) ──
        var lp = panelGO.GetComponent<LoginPanel>();
        if (lp == null) { Debug.LogError("[WireLoginPanel] LoginPanel component missing."); return; }

        var so = new SerializedObject(lp);

        SetRef(so, "usernameField",  FindChild<TMP_InputField> (panelGO, "UsernameInput"));
        SetRef(so, "passwordField",  FindChild<TMP_InputField> (panelGO, "PasswordInput"));
        SetRef(so, "corpNameField",  FindChild<TMP_InputField> (panelGO, "CorpNameInput"));
        SetRef(so, "statusText",     FindChild<TextMeshProUGUI>(panelGO, "StatusText"));
        SetRef(so, "loginButton",    FindChild<Button>         (panelGO, "LoginButton"));
        SetRef(so, "registerButton", FindChild<Button>         (panelGO, "RegisterButton"));

        // hideUntilLogin (inclut les GameObjects inactifs)
        var hide = new List<GameObject>();
        foreach (var n in new[] { "TestLaunchMenu", "HUDController", "GameHUD" })
        {
            var go = FindIncludingInactive(n);
            if (go != null) hide.Add(go);
            else Debug.LogWarning($"[WireLoginPanel] hideUntilLogin: '{n}' not found.");
        }
        var hideProp = so.FindProperty("hideUntilLogin");
        hideProp.arraySize = hide.Count;
        for (int i = 0; i < hide.Count; i++)
            hideProp.GetArrayElementAtIndex(i).objectReferenceValue = hide[i];

        so.ApplyModifiedProperties();

        // ── 8. Marquer la scène modifiée ─────────────────────────────────────
        EditorUtility.SetDirty(panelGO);
        EditorUtility.SetDirty(lp);
        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(panelGO.scene);

        Debug.Log($"[WireLoginPanel] Done — {hide.Count} hideUntilLogin objects assigned.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    static RectTransform EnsureRT(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        return rt != null ? rt : go.AddComponent<RectTransform>();
    }

    static T FindChild<T>(GameObject parent, string childName) where T : Component
    {
        var t = parent.transform.Find(childName);
        return t != null ? t.GetComponent<T>() : null;
    }

    static void SetRef<T>(SerializedObject so, string propName, T value) where T : UnityEngine.Object
    {
        var prop = so.FindProperty(propName);
        if (prop != null) prop.objectReferenceValue = value;
        else Debug.LogWarning($"[WireLoginPanel] SerializedProperty '{propName}' not found.");
    }

    /// <summary>Trouve un GameObject par nom, incluant les objets inactifs.</summary>
    static GameObject FindIncludingInactive(string name)
    {
        // Unity 2022+ : FindObjectsByType inclut les inactifs
        foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            if (go.name == name) return go;
        return null;
    }

    static void SetupInputField(string childName, string placeholderText, TMP_InputField.ContentType contentType)
    {
        // Trouver le GameObject du champ (enfant de LoginPanel ou dans toute la scène)
        var go = GameObject.Find(childName);
        if (go == null) { Debug.LogWarning($"[WireLoginPanel] InputField '{childName}' not found."); return; }

        // Fond visible
        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.12f, 0.16f, 1f);

        // TMP_InputField
        var field = go.GetComponent<TMP_InputField>();
        if (field == null) field = go.AddComponent<TMP_InputField>();
        field.contentType = contentType;

        // Nettoyer les anciens enfants Text Area s'il y en a déjà
        var existingArea = go.transform.Find("Text Area");
        if (existingArea != null) UnityEngine.Object.DestroyImmediate(existingArea.gameObject);

        // ── Text Area (RectMask2D) ──────────────────────────────────────────────
        var areaGO = new GameObject("Text Area");
        areaGO.transform.SetParent(go.transform, false);
        areaGO.AddComponent<RectMask2D>();
        var areaRT = areaGO.GetComponent<RectTransform>();
        areaRT.anchorMin = Vector2.zero;
        areaRT.anchorMax = Vector2.one;
        areaRT.offsetMin = new Vector2(8, 2);
        areaRT.offsetMax = new Vector2(-8, -2);

        // ── Placeholder ──────────────────────────────────────────────────────────
        var phGO = new GameObject("Placeholder");
        phGO.transform.SetParent(areaGO.transform, false);
        var phTMP = phGO.AddComponent<TextMeshProUGUI>();
        phTMP.text      = placeholderText;
        phTMP.fontSize  = 14;
        phTMP.fontStyle = FontStyles.Italic;
        phTMP.color     = new Color(0.5f, 0.5f, 0.5f, 0.75f);
        phTMP.alignment = TextAlignmentOptions.MidlineLeft;
        phTMP.textWrappingMode = TextWrappingModes.NoWrap;
        var phRT = phGO.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = Vector2.zero;
        phRT.offsetMax = Vector2.zero;

        // ── Text (saisie) ────────────────────────────────────────────────────────
        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(areaGO.transform, false);
        var txtTMP = txtGO.AddComponent<TextMeshProUGUI>();
        txtTMP.text      = "";
        txtTMP.fontSize  = 14;
        txtTMP.color     = Color.white;
        txtTMP.alignment = TextAlignmentOptions.MidlineLeft;
        txtTMP.textWrappingMode = TextWrappingModes.NoWrap;
        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero;
        txtRT.offsetMax = Vector2.zero;

        // ── Câbler les références du TMP_InputField ──────────────────────────────
        var soField = new SerializedObject(field);
        soField.FindProperty("m_TextComponent").objectReferenceValue = txtTMP;
        soField.FindProperty("m_Placeholder").objectReferenceValue   = phTMP;
        soField.ApplyModifiedProperties();

        EditorUtility.SetDirty(go);
    }

    static void SetupButton(string childName, string label, Color bgColor)
    {
        // Chercher dans toute la scène (parent non connu au moment du call)
        var go = GameObject.Find(childName);
        if (go == null) return;

        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.color = bgColor;

        var btn = go.GetComponent<Button>();
        if (btn == null) btn = go.AddComponent<Button>();

        // Label texte enfant
        var labelT = go.transform.Find("Text");
        TextMeshProUGUI tmp;
        if (labelT == null)
        {
            var child = new GameObject("Text");
            child.transform.SetParent(go.transform, false);
            tmp = child.AddComponent<TextMeshProUGUI>();
            var rt = child.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        else
        {
            tmp = labelT.GetComponent<TextMeshProUGUI>();
            if (tmp == null) tmp = labelT.gameObject.AddComponent<TextMeshProUGUI>();
        }
        tmp.text      = label;
        tmp.fontSize  = 14;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
    }
}
