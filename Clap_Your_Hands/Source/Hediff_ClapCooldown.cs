using Verse;

namespace ClapYourHands
{
    /// <summary>
    /// 击掌冷却载体（24 小时），不出现在健康列表。
    /// <para>时长由原版 <see cref="HediffComp_Disappears"/> 承载，随存档序列化。</para>
    /// </summary>
    public class Hediff_ClapCooldown : HediffWithComps
    {
        /// <summary>不在健康列表中显示。</summary>
        public override bool Visible => false;

        /// <summary>重置冷却计时（重复击掌会顺延）。</summary>
        public void ResetTimer(int durationTicks)
        {
            GetComp<HediffComp_Disappears>()?.SetDuration(durationTicks);
        }
    }
}
