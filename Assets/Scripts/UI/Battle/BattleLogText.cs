using System.Collections.Generic;
using System.Text;

/// <summary>
/// 战斗日志文案格式化（不引用 Luban 生成的类型，避免 cfg.cfg.* 命名空间问题）：
///   battlelog.logcontext 里的 %s 按 logcontext_type 顺序替换
///   1 = 发动者名字，2 = 受到效果者名字，3 = 技能效果
///   skill.skilllog 自身的 %s 用实数值填充
/// </summary>
public static class BattleLogText
{
    /// <summary> 技能效果文本：skilllog 的 %s 换成数值；没配则回退 "{value}点伤害" </summary>
    public static string BuildEffect(string skillLog, int value)
    {
        if (!string.IsNullOrEmpty(skillLog))
            return ReplacePlaceholders(skillLog, new List<string> { value.ToString() });
        return value > 0 ? $"{value}点伤害" : "";
    }

    /// <summary> 按 logcontext_type 组装一条池文案；返回 null 表示模板为空 </summary>
    public static string Format(string logcontext, List<int> types,
                               string actorName, string targetName, string effectText)
    {
        if (string.IsNullOrEmpty(logcontext)) return null;

        var args = new List<string>();
        if (types != null)
        {
            foreach (int t in types)
            {
                switch (t)
                {
                    case 1: args.Add(actorName ?? "?"); break;
                    case 2: args.Add(targetName ?? "?"); break;
                    case 3: args.Add(effectText ?? ""); break;
                    default: args.Add(""); break;
                }
            }
        }
        return ReplacePlaceholders(logcontext, args);
    }

    /// <summary> 按顺序把第 n 个 %s 替换成第 n 个参数（battlelog 表用 %s 作占位符） </summary>
    public static string ReplacePlaceholders(string template, List<string> args)
    {
        if (string.IsNullOrEmpty(template)) return template;
        var sb = new StringBuilder(template.Length + 32);
        int argIndex = 0;
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] == '%' && i + 1 < template.Length && template[i + 1] == 's')
            {
                sb.Append(argIndex < args.Count ? args[argIndex] : "%s");
                argIndex++;
                i++; // 跳过 's'
            }
            else
            {
                sb.Append(template[i]);
            }
        }
        return sb.ToString();
    }
}
