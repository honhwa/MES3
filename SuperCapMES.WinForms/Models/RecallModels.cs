namespace SuperCapMES.WinForms.Models;

/// <summary>
/// 召回反查方式常量：电芯SN / 材料批次 / 出货单号 / 客户；All 用于 UI 下拉（额外值仅作聚合展示，分派仍以具体模式为准）。
/// </summary>
public static class RecallMode
{
    public const string CellSn = "电芯SN";
    public const string MaterialLot = "材料批次";
    public const string ShipmentOrder = "出货单号";
    public const string Customer = "客户";

    /// <summary>UI 下拉展示顺序</summary>
    public static readonly string[] All =
    {
        CellSn,
        MaterialLot,
        ShipmentOrder,
        Customer
    };
}

/// <summary>
/// 召回反查汇总（命中影响范围的关键计数）。
/// 口径说明：
/// ・ShipmentCount：受影响电芯涉及的「去重」有效出货单数；
/// ・CustomerCount：上述出货单对应的去重客户数；
/// ・CellCount：受影响电芯总数（含已在库与已出货）；
/// ・ShippedCellCount：已出货电芯数（出现在某有效出货绑定中）；
/// ・InStockCellCount：在库电芯数（未出货，口径=未出货电芯总数）；
/// ・HeldCellCount：已在保留中的电芯数（任一有效保留单）；
/// ・LotCount：受影响材料批次数（极片批 ∪ 电解液批，去重）；
/// ・FreezableCellCount：可冻结电芯数（未出货 且 未在保留中）。
/// </summary>
public class RecallSummary
{
    public int ShipmentCount { get; set; }
    public int CustomerCount { get; set; }
    public int CellCount { get; set; }
    public int ShippedCellCount { get; set; }
    public int InStockCellCount { get; set; }
    public int HeldCellCount { get; set; }
    public int LotCount { get; set; }
    public int FreezableCellCount { get; set; }
}

/// <summary>召回反查——出货记录视图行（一只已出货电芯一行）</summary>
public class RecallShipmentRow
{
    public string OrderNo { get; set; } = "";
    public string Customer { get; set; } = "";
    public DateTime? ShipTime { get; set; }
    public string RefNo { get; set; } = "";
    public string CellSn { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public string ElectrodeLot { get; set; } = "";
    public string ElectrolyteLot { get; set; } = "";
    public string WarehouseLot { get; set; } = "";
}

/// <summary>召回反查——受影响电芯视图行（一只电芯一行）</summary>
public class RecallCellRow
{
    public string CellSn { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Shipped { get; set; }
    public string OrderNo { get; set; } = "";
    public string Customer { get; set; } = "";
    public string StockLocation { get; set; } = "";
    public double? StockQty { get; set; }
    public bool Held { get; set; }
    public string HolderNo { get; set; } = "";
    public string ElectrodeLot { get; set; } = "";
    public string ElectrolyteLot { get; set; } = "";
}

/// <summary>召回反查——受影响材料批次视图行</summary>
public class RecallLotRow
{
    public string LotNo { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public int CellCount { get; set; }
    public int ShipmentCount { get; set; }
}

/// <summary>
/// 召回反查结果（纯查询视图模型，不持久化）。
/// 任何模式下空/找不到时：Message 说明原因，各列表为空，不抛异常。
/// </summary>
public class RecallQueryResult
{
    public string Mode { get; set; } = "";
    public string Keyword { get; set; } = "";
    public RecallSummary Summary { get; set; } = new();
    public List<RecallShipmentRow> Shipments { get; set; } = new();
    public List<RecallCellRow> Cells { get; set; } = new();
    public List<RecallLotRow> Lots { get; set; } = new();
    public string Message { get; set; } = "";
}
