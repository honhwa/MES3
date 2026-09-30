namespace SuperCapMES.WinForms.Models;

/// <summary>保留 / 解除保留操作履历（只增不改，用于审计）</summary>
public class HoldHistory
{
    /// <summary>全局自增序号</summary>
    public int Seq { get; set; }

    /// <summary>保留单号</summary>
    public string HoldNo { get; set; } = "";

    /// <summary>动作类型：保留 / 解除</summary>
    public string ActionType { get; set; } = HoldActionType.Hold;

    /// <summary>操作人</summary>
    public string Operator { get; set; } = "";

    /// <summary>操作时间</summary>
    public DateTime Time { get; set; }

    /// <summary>原因说明</summary>
    public string Reason { get; set; } = "";

    /// <summary>涉及电芯数量</summary>
    public int CellCount { get; set; }

    /// <summary>SN 摘要：单只显示 SN，多只显示"首个等 N 只"</summary>
    public string SnPreview { get; set; } = "";
}
