using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit VisualElement that renders a price-history sparkline
/// using painter2D (MeshGenerationContext). Drop into any container
/// and call SetData() with the ordered price samples.
/// </summary>
public class SparklineElement : VisualElement
{
    private float[] _values = System.Array.Empty<float>();

    public Color lineColor = new Color(0.31f, 0.86f, 0.55f); // default: bio green

    // ── UXML factory ────────────────────────────────────────────────────
    public new class UxmlFactory : UxmlFactory<SparklineElement, UxmlTraits> { }

    public new class UxmlTraits : VisualElement.UxmlTraits { }

    public SparklineElement()
    {
        generateVisualContent += OnGenerateVisualContent;
    }

    // ── API ─────────────────────────────────────────────────────────────
    public void SetData(float[] values)
    {
        _values = values ?? System.Array.Empty<float>();
        MarkDirtyRepaint();
    }

    public void SetData(float[] values, Color color)
    {
        lineColor = color;
        SetData(values);
    }

    // ── Rendering ───────────────────────────────────────────────────────
    private void OnGenerateVisualContent(MeshGenerationContext mgc)
    {
        if (_values.Length < 2) return;

        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in _values)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        float range = (max - min) < 0.001f ? 1f : (max - min);

        float w = contentRect.width;
        float h = contentRect.height;

        var p = mgc.painter2D;
        p.strokeColor = lineColor;
        p.lineWidth   = 1.5f;
        p.lineCap     = LineCap.Round;
        p.lineJoin    = LineJoin.Round;

        p.BeginPath();
        for (int i = 0; i < _values.Length; i++)
        {
            float x = (float)i / (_values.Length - 1) * w;
            float y = h * (1f - (_values[i] - min) / range);

            if (i == 0) p.MoveTo(new Vector2(x, y));
            else        p.LineTo(new Vector2(x, y));
        }
        p.Stroke();
    }
}
