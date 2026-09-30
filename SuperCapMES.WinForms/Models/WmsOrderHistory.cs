namespace SuperCapMES.WinForms.Models;

/// <summary>
/// WMS 单据操作履历（只增不改，用于审计）：创建 / 入库检 / 过账 每次动作追加一条。
/// </summary>
public class WmsOrderHistory
{
    /// <summary>全局自增序号（同刻排序辅助）</summary>
    public int Seq { get; set; }

    /// <summary>单据号</summary>
    public string OrderNo { get; set; } = "";

    /// <summary>动作类型：创建 / 入库检 / 过账</summary>
    public string Action { get; set; } = WmsAction.Create;

    /// <summary>操作人</summary>
    public string Operator { get; set; } = "";

    /// <summary>操作时间</summary>
    public DateTime Time { get; set; }

    /// <summary>备注：动作摘要</summary>
    public string Remark { get; set; } = "";
}
