namespace SuperCapMES.WinForms.Models;

/// <summary>电性能测试数据（全检全量入库）</summary>
public class TestRecord
{
    public string CellSn { get; set; } = "";
    public double CapacityF { get; set; }      // 容量 F
    public double EsrMohm { get; set; }        // ESR mΩ
    public double LeakageUa { get; set; }      // 漏电流 μA
    public double SelfDischarge { get; set; }  // 自放电率 %
    public string Tester { get; set; } = "";   // 测试仪编号
    public DateTime TestTime { get; set; }
    public bool Passed { get; set; } = true;
}
