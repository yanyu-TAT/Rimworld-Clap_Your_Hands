using RimWorld;
using Verse;

namespace ClapYourHands
{
    /// <summary>Def 引用缓存。字段名必须与 Defs 中的 defName 一致。</summary>
    [DefOf]
    public static class ClapDefOf
    {
        public static InteractionDef Clap;
        public static ThoughtDef Clap_Bad;
        public static ThoughtDef Clap_Normal;
        public static ThoughtDef Clap_Good;
        public static ThoughtDef Clap_Perfect;
        public static HediffDef Clap_Buff;
        public static HediffDef Clap_Cooldown;

        static ClapDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ClapDefOf));
        }
    }
}
