using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ClapYourHands
{
    /// <summary>
    /// 击掌互动的工作器，由原版 <c>Pawn_InteractionsTracker</c> 按
    /// <see cref="RandomSelectionWeight"/> 加权抽取。
    /// </summary>
    public class InteractionWorker_Clap : InteractionWorker
    {
        /// <summary>触发权重。本方法会被高频调用，需保持廉价、无副作用。</summary>
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (initiator.Inhumanized())
                return 0f;

            // 没有可用的手就击不了掌（双方都至少需要一只）
            if (!ClapUtility.TryGetHand(initiator, out _) || !ClapUtility.TryGetHand(recipient, out _))
                return 0f;

            // 敌对 / 仇视关系不互动（敌对另由原版 pawn.HostileTo 排除）
            if (ClapUtility.IsHostileRelation(initiator, recipient))
                return 0f;

            if (!ClapUtility.CanClapNow(initiator, recipient))
                return 0f;

            // 好感度不影响抽取权重，只影响结果概率
            return ClapDebug.MoreClapHands
                ? ClapUtility.BaseSelectionWeight * ClapDebug.WeightMultiplier
                : ClapUtility.BaseSelectionWeight;
        }

        /// <summary>互动实际发生时的结算入口。</summary>
        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;

            if (initiator is null || recipient is null || initiator.Dead || recipient.Dead)
            {
                Log.Error($"{ClapYourHandsMod.LogPrefix}击掌结算收到无效的小人，已跳过。");
                return;
            }

            var outcome = ClapUtility.RollOutcome(initiator, recipient);
            ClapUtility.ApplyOutcome(initiator, recipient, outcome);

            if (outcome == ClapOutcome.Perfect)
            {
                ClapUtility.ApplyPerfectReward(initiator);
                ClapUtility.ApplyPerfectReward(recipient);
            }

            // 双方各自进入 24 小时冷却
            ClapUtility.ApplyCooldown(initiator);
            ClapUtility.ApplyCooldown(recipient);
        }
    }
}
