namespace ClapYourHands
{
    /// <summary>
    /// 开发期调试开关，不参与存档（游戏退出即重置）。
    /// 在开发者窗口 settings 分页的「Clap」分类下切换。
    /// </summary>
    public static class ClapDebug
    {
        /// <summary>More Clap Hands 开启时，击掌抽取权重的放大倍数。</summary>
        public const float WeightMultiplier = 100f;

        /// <summary>开启后击掌的抽取权重 ×<see cref="WeightMultiplier"/>。</summary>
        public static bool MoreClapHands;

        /// <summary>开启后跳过权重抽取，总是达成完美击掌。</summary>
        public static bool AlwaysPerfect;
    }
}
