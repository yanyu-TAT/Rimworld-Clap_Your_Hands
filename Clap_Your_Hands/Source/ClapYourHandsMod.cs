using Verse;

namespace ClapYourHands
{
    /// <summary>
    /// 模组主类。本模组零第三方依赖，主类基类使用原版 <see cref="Mod"/>。
    /// </summary>
    public class ClapYourHandsMod : Mod
    {
        /// <summary>所有日志输出统一前缀，便于在 Player.log 中检索本模组。</summary>
        public const string LogPrefix = "[ClapYourHands] ";

        public ClapYourHandsMod(ModContentPack content) : base(content)
        {
            Log.Message(LogPrefix + "loaded.");
        }
    }
}
/* Todo List:
 * - [ ] 添加互动效果、情绪及对应的增益效果
 * - [ ] 添加完成完美击掌时的特效和音效
 */

/* Develop Log:
 * 09/12 22:26 初始化了项目框架并进行了初次提交
 */