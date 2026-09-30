namespace SuperCapMES.WinForms.Models;

/// <summary>SPC 样本来源常量</summary>
public static class SpcSampleSource
{
    /// <summary>实测（来自种子/现场采集）</summary>
    public const string Measured = "实测";

    /// <summary>模拟（由演示按钮「生成模拟样本」追加）</summary>
    public const string Simulated = "模拟";
}

/// <summary>SPC 单个样本点（一条质量特性的一次观测）</summary>
public class SpcSample
{
    /// <summary>所属监控项编号</summary>
    public string ItemId { get; set; } = "";

    /// <summary>序号（同一监控项内自增，从 1 开始）</summary>
    public int Seq { get; set; }

    /// <summary>观测值</summary>
    public double Value { get; set; }

    /// <summary>采样时间</summary>
    public DateTime SampleTime { get; set; }

    /// <summary>采样设备编码</summary>
    public string Equipment { get; set; } = "";

    /// <summary>关联电芯 SN（可空/可空串）</summary>
    public string CellSn { get; set; } = "";

    /// <summary>子组号（可空；本演示按单值-样本口径，未分组）</summary>
    public int? Subgroup { get; set; }

    /// <summary>来源：实测 / 模拟</summary>
    public string Source { get; set; } = SpcSampleSource.Measured;
}
