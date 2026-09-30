namespace SuperCapMES.WinForms.Models;

/// <summary>
/// 现场异常操作履历（只增不改，用于审计）：
/// 登记 / 确认 / 闭环 每次动作追加一条，闭环时 Remark 简述根本措施与横展结论。
/// </summary>
public class AbnormalHistory
{
    /// <summary>全局自增序号</summary>
    public int Seq { get; set; }

    /// <summary>异常编号</summary>
    public string AbNo { get; set; } = "";

    /// <summary>动作类型：登记 / 确认 / 闭环</summary>
    public string Action { get; set; } = AbnormalAction.Register;

    /// <summary>操作人</summary>
    public string Operator { get; set; } = "";

    /// <summary>操作时间</summary>
    public DateTime Time { get; set; }

    /// <summary>备注：登记摘要 / 确认摘要 / 根因措施与横展结论</summary>
    public string Remark { get; set; } = "";
}
