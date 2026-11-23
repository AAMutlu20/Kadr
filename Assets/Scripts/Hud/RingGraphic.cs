using UnityEngine;
using UnityEngine.UI;

namespace Hud
{
    /// <summary>
    /// A thin circle drawn as a mesh, centred on its RectTransform. With a gap it becomes four arcs with openings at
    /// the top, bottom, left and right, like a lens focus mark.
    /// </summary>
    public class RingGraphic : MaskableGraphic
    {
        [SerializeField] private float radius = 20f;
        [SerializeField] private float thickness = 2f;
        [Tooltip("Width in degrees of the openings at 0, 90, 180 and 270 degrees. 0 = a closed ring.")]
        [SerializeField] private float gapDegrees;

        public void Configure(float newRadius, float newThickness, float newGapDegrees)
        {
            radius = newRadius;
            thickness = newThickness;
            gapDegrees = newGapDegrees;
            SetVerticesDirty();
        }

        public void SetRadius(float newRadius)
        {
            if (Mathf.Abs(newRadius - radius) < 0.05f) return;

            radius = newRadius;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var centre = GetPixelAdjustedRect().center;
            var segments = Mathf.Clamp(Mathf.RoundToInt(radius * 1.2f), 24, 96);
            var inner = Mathf.Max(0f, radius - thickness * 0.5f);
            var outer = radius + thickness * 0.5f;
            var quad = new UIVertex[4];

            for (var i = 0; i < segments; i++)
            {
                var a0 = i / (float)segments * Mathf.PI * 2f;
                var a1 = (i + 1) / (float)segments * Mathf.PI * 2f;

                if (gapDegrees > 0f)
                {
                    var middle = (a0 + a1) * 0.5f * Mathf.Rad2Deg;
                    var fromAxis = Mathf.Abs(Mathf.DeltaAngle(middle % 90f, 0f));
                    if (fromAxis < gapDegrees * 0.5f || fromAxis > 90f - gapDegrees * 0.5f) continue;
                }

                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                quad[0] = Vertex(centre + d0 * inner);
                quad[1] = Vertex(centre + d0 * outer);
                quad[2] = Vertex(centre + d1 * outer);
                quad[3] = Vertex(centre + d1 * inner);
                vh.AddUIVertexQuad(quad);
            }
        }

        private UIVertex Vertex(Vector2 position)
        {
            var v = UIVertex.simpleVert;
            v.position = position;
            v.color = color;
            return v;
        }
    }
}