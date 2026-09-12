using Verse;

namespace ClapYourHands
{
    /// <summary>
    /// 完美击掌的 12 小时增益载体。等级用 <c>Severity</c>（1 / 2 / 3），时长由原版
    /// <see cref="HediffComp_Disappears"/> 承载。
    /// <para>合并规则：新等级更高 → 升级并重置时长；同级 → 仅重置时长；更低 → 忽略。</para>
    /// </summary>
    public class Hediff_ClapBuff : HediffWithComps
    {
        public int Level => (int)Severity;

        /// <summary>按合并规则应用一次增益。</summary>
        public void ApplyLevel(int level, int durationTicks)
        {
            if (level < Level)
            {
                // 低级不覆盖高级：不动等级，也不动时长
                return;
            }

            if (level > Level)
            {
                Severity = level;
            }

            GetComp<HediffComp_Disappears>()?.SetDuration(durationTicks);
        }
    }
}
