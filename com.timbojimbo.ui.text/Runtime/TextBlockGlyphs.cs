using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI.Text
{
    /// <summary>
    /// The child renderer that draws a <see cref="TextBlock"/>'s glyphs: one sub-mesh per atlas texture with its
    /// own material. Sits between the decoration and inline-object containers so draw order is backgrounds,
    /// glyphs, objects. Created and fed by its owner; never add it by hand.
    /// </summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class TextBlockGlyphs : MaskableGraphic
    {
        private TextBlock _owner;
        private Mesh _mesh;
        private readonly List<Material> _materials = new();
        private Texture _mainTexture;

        private static readonly List<IMaterialModifier> s_Modifiers = new();

        internal void Bind(TextBlock owner)
        {
            _owner = owner;
            raycastTarget = false;
        }

        public override Texture mainTexture => _mainTexture != null ? _mainTexture : s_WhiteTexture;

        /// <summary>Uploads freshly built geometry. Called by the owner from inside its own rebuild.</summary>
        internal void Apply(List<Vector3> positions, List<Color32> colors, List<Vector2> uv0, List<Vector4> uv1, List<List<int>> groupTriangles, List<Material> materials)
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "TextBlock glyphs", hideFlags = HideFlags.HideAndDontSave };
                _mesh.MarkDynamic();
            }

            _mesh.Clear();
            _materials.Clear();
            _mainTexture = null;

            if (groupTriangles.Count > 0)
            {
                _mesh.SetVertices(positions);
                _mesh.SetColors(colors);
                _mesh.SetUVs(0, uv0);
                _mesh.SetUVs(1, uv1);
                _mesh.subMeshCount = groupTriangles.Count;
                for (int i = 0; i < groupTriangles.Count; i++)
                    _mesh.SetTriangles(groupTriangles[i], i, false);
                _mesh.RecalculateBounds();
                _materials.AddRange(materials);
                _mainTexture = materials[0].mainTexture;
            }

            // CanvasRenderer reads 16-bit indices only, which the default mesh index format provides.
            canvasRenderer.SetMesh(_mesh);
            ApplyMaterials();
        }

        protected override void UpdateGeometry()
        {
            // The canvas asked us to rebuild on our own (enable, canvas change): the owner regenerates and calls Apply.
            if (_owner != null && _owner.IsActive())
                _owner.RebuildGlyphs();
            else if (_mesh != null)
            {
                _mesh.Clear();
                canvasRenderer.SetMesh(_mesh);
            }
        }

        protected override void UpdateMaterial() => ApplyMaterials();

        private void ApplyMaterials()
        {
            if (!IsActive()) return;

            GetComponents(s_Modifiers);
            int count = _materials.Count;
            canvasRenderer.materialCount = Mathf.Max(1, count);
            if (count == 0)
            {
                canvasRenderer.SetMaterial(materialForRendering, 0);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                var material = _materials[i];
                for (int m = 0; m < s_Modifiers.Count; m++)
                    material = s_Modifiers[m].GetModifiedMaterial(material);
                canvasRenderer.SetMaterial(material, i);
            }
        }

        protected override void OnDestroy()
        {
            if (_mesh != null)
                DestroyImmediate(_mesh);
            _mesh = null;
            base.OnDestroy();
        }
    }
}
