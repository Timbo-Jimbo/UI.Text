using System.Collections.Generic;
using TimboJimbo.UI.Text.Markup;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI.Text
{
    /// <summary>Places the prefabs a <see cref="TextLayout"/> asks for, from a pool, once UGUI's rebuild loop has finished.</summary>
    public sealed partial class TextBlock
    {
        private struct InlineInstance
        {
            public GameObject Prefab;
            public GameObject Instance;
            public IInlineContent Content;
        }

        private struct PendingPlacement
        {
            public GameObject Prefab;
            public bool BelowGlyphs;
            public InlineToken Token;
            public InlineContext Context;
            public InlineFragment Fragment;
        }

        private readonly List<InlineInstance> _inlineInstances = new();
        private readonly List<PendingPlacement> _pending = new();
        private bool _pendingDirty;

        private static readonly List<InlinePlacementRequest> s_Placements = new();
        private static readonly List<TextBlock> s_PendingBlocks = new();
        private static readonly List<TextBlock> s_Applying = new();
        private static readonly Dictionary<GameObject, Stack<GameObject>> s_Pool = new();
        private static Transform s_PoolRoot;
        private static bool s_Hooked;
        private static bool s_InApply;

        // UGUI forbids enabling or disabling a Graphic while it rebuilds graphics, and our rebuild runs inside
        // that loop. Placements are therefore computed during the rebuild and applied from a hook that runs
        // once the loop has finished; a forced canvas update then lets the new objects draw in the same frame.

        /// <summary>Converts the layout's placement requests to canvas units and queues them for after the rebuild.</summary>
        private void QueueInlineContent(float scale)
        {
            _pending.Clear();
            if (_richText)
            {
                var context = new InlineContext(this, _fontSize, color);
                _layout.CollectPlacements(in context, s_Placements);
                float inverseScale = 1f / scale;
                for (int i = 0; i < s_Placements.Count; i++)
                {
                    var p = s_Placements[i];
                    var fragment = new InlineFragment(Scale(p.Px, inverseScale), p.Index, p.Count, p.BaselinePx * inverseScale, p.AscentPx * inverseScale, p.DescentPx * inverseScale);
                    _pending.Add(new PendingPlacement { Prefab = p.Prefab, BelowGlyphs = p.BelowGlyphs, Token = Document.Tokens[p.TokenIndex], Context = context, Fragment = fragment });
                }
                s_Placements.Clear();
            }

            _pendingDirty = true;
            if (!s_PendingBlocks.Contains(this))
                s_PendingBlocks.Add(this);
            EnsureHook();
        }

        private static void EnsureHook()
        {
            if (s_Hooked)
                return;
            // Touch the registry first so its own handler is subscribed before ours and runs first.
            _ = CanvasUpdateRegistry.instance;
            Canvas.willRenderCanvases += ApplyPendingInline;
            s_Hooked = true;
        }

        private static void ApplyPendingInline()
        {
            if (s_InApply || s_PendingBlocks.Count == 0)
                return;
            s_InApply = true;
            bool changed = false;
            try
            {
                s_Applying.Clear();
                s_Applying.AddRange(s_PendingBlocks);
                s_PendingBlocks.Clear();
                for (int i = 0; i < s_Applying.Count; i++)
                {
                    var block = s_Applying[i];
                    if (block != null)
                        changed |= block.ApplyInline();
                }
                s_Applying.Clear();
            }
            finally
            {
                s_InApply = false;
            }
            // The objects were enabled after the canvases were built; a second pass draws them this frame.
            if (changed)
                Canvas.ForceUpdateCanvases();
        }

        private bool ApplyInline()
        {
            if (!_pendingDirty)
                return false;
            _pendingDirty = false;

            bool changed = _inlineInstances.Count > 0;
            ReleaseInlineContent();
            if (!IsActive())
            {
                _pending.Clear();
                return changed;
            }

            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                Place(p.Prefab, p.BelowGlyphs ? DecorationsContainer : ObjectsContainer, in p.Token, in p.Context, in p.Fragment);
                changed = true;
            }
            _pending.Clear();
            return changed;
        }

        private static Rect Scale(Rect px, float inverseScale) => new(px.x * inverseScale, px.y * inverseScale, px.width * inverseScale, px.height * inverseScale);

        private void Place(GameObject prefab, RectTransform container, in InlineToken token, in InlineContext context, in InlineFragment fragment)
        {
            var instance = Acquire(prefab, container);
            var rt = instance.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(fragment.Rect.x, -fragment.Rect.y);
                rt.sizeDelta = fragment.Rect.size;
                rt.localScale = Vector3.one;
            }
            var content = instance.GetComponent<IInlineContent>();
            content?.Bind(in token, in context, in fragment);
            _inlineInstances.Add(new InlineInstance { Prefab = prefab, Instance = instance, Content = content });
        }

        private void ReleaseInlineContent()
        {
            for (int i = 0; i < _inlineInstances.Count; i++)
            {
                var entry = _inlineInstances[i];
                if (entry.Instance == null)
                    continue;
                entry.Content?.Unbind();
                Release(entry.Prefab, entry.Instance);
            }
            _inlineInstances.Clear();
        }

        private static GameObject Acquire(GameObject prefab, RectTransform container)
        {
            GameObject instance = null;
            if (s_Pool.TryGetValue(prefab, out var stack))
                while (stack.Count > 0 && instance == null)
                    instance = stack.Pop();

            if (instance == null)
            {
                instance = Instantiate(prefab);
                instance.name = prefab.name;
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    t.gameObject.hideFlags = HideFlags.DontSave;
            }
            instance.transform.SetParent(container, false);
            instance.SetActive(true);
            return instance;
        }

        private static void Release(GameObject prefab, GameObject instance)
        {
            if (s_PoolRoot == null)
            {
                var root = new GameObject("TextBlock inline pool") { hideFlags = HideFlags.HideAndDontSave };
                root.SetActive(false);
                s_PoolRoot = root.transform;
            }
            instance.SetActive(false);
            instance.transform.SetParent(s_PoolRoot, false);
            if (!s_Pool.TryGetValue(prefab, out var stack))
                s_Pool[prefab] = stack = new Stack<GameObject>();
            stack.Push(instance);
        }
    }
}
