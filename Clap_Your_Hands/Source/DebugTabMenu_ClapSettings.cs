using System.Reflection;
using LudeonTK;
using RimWorld;
using Verse;

namespace ClapYourHands
{
    /// <summary>
    /// 原版 settings 分页的扩展版：执行原版逻辑后追加本模组的 <c>Clap</c> 分类开关。
    /// <para>由 <c>Patches/Patch_DebugTabMenu.xml</c> 经 XPath 换掉原版 Settings Def 的 menuClass 接入。
    /// 节点设置 <c>settingsField</c> 后会被原版渲染为复选框。</para>
    /// </summary>
    public class DebugTabMenu_ClapSettings : DebugTabMenu_Settings
    {
        private const string CategoryLabel = "Clap";

        public DebugTabMenu_ClapSettings(DebugTabMenuDef def, Dialog_Debug dialog, DebugActionNode root)
            : base(def, dialog, root) { }

        public override DebugActionNode InitActions(DebugActionNode absRoot)
        {
            myRoot = base.InitActions(absRoot);

            AddSetting(nameof(ClapDebug.MoreClapHands), "More Clap Hands (weight x100)");
            AddSetting(nameof(ClapDebug.AlwaysPerfect), "Always Perfect clap");

            return myRoot;
        }

        private void AddSetting(string fieldName, string label)
        {
            FieldInfo field = typeof(ClapDebug).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            if (field is null || field.FieldType != typeof(bool))
            {
                Log.Error($"{ClapYourHandsMod.LogPrefix}调试开关字段无效：{nameof(ClapDebug)}.{fieldName}");
                return;
            }

            DebugActionNode node = new(label)
            {
                settingsField = field,
                category = CategoryLabel
            };
            myRoot.AddChild(node);
        }
    }
}
