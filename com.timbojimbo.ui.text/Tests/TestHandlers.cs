using System;
using TimboJimbo.UI.Text.Markup;

namespace TimboJimbo.UI.Text.Tests
{
    /// <summary>A :name: handler with a chosen placement, padding and acceptance rule.</summary>
    internal sealed class DelimitedHandler : InlineHandler
    {
        private float _paddingEm;
        public Func<string, bool> Accept;

        public override float PaddingEm => _paddingEm;
        public override bool Accepts(string payload) => Accept == null || Accept(payload);

        public static DelimitedHandler Create(InlinePlacement placement, float paddingEm = 0f)
        {
            var handler = CreateInstance<DelimitedHandler>();
            handler._paddingEm = paddingEm;
            handler.Configure("emote", new InlineTrigger { Kind = InlineTriggerKind.Delimited, Open = ":", Close = ":" }, placement);
            return handler;
        }
    }

    /// <summary>An @name handler, atomic.</summary>
    internal sealed class PrefixHandler : InlineHandler
    {
        public static PrefixHandler Create()
        {
            var handler = CreateInstance<PrefixHandler>();
            handler.Configure("mention", new InlineTrigger { Kind = InlineTriggerKind.Prefix, Open = "@" }, InlinePlacement.Atomic);
            return handler;
        }
    }

    /// <summary>A handler for a built-in kind ("code", "codeblock"): no trigger of its own, only placement and padding.</summary>
    internal sealed class KindHandler : InlineHandler
    {
        private float _paddingEm;

        public override float PaddingEm => _paddingEm;

        public static KindHandler Create(string kind, InlinePlacement placement, float paddingEm)
        {
            var handler = CreateInstance<KindHandler>();
            handler._paddingEm = paddingEm;
            handler.Configure(kind, default, placement);
            return handler;
        }
    }
}
