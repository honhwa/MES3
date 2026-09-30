namespace SuperCapMES.WinForms.Models;

/// <summary>ERP BOM 物料类型常量（用于标注 BOM 行性质，可选）</summary>
public static class ErpMaterialTypes
{
    /// <summary>主材</summary>
    public const string Main = "主材";

    /// <summary>辅材</summary>
    public const string Aux = "辅材";

    /// <summary>包材</summary>
    public const string Pack = "包材";

    /// <summary>全部类型（顺序即展示顺序）</summary>
    public static readonly string[] All = { Main, Aux, Pack };
}

/// <summary>
/// ERP BOM 明细行：某产品型号生产 1 只成品所需的某物料及其单位用量 / 损耗率。
/// 需求量计算统一由仓储 <c>MesDatabase.GetBomRequirement</c> 提供，避免模型层藏业务逻辑。
/// </summary>
public class ErpBomItem
{
    /// <summary>产品型号</summary>
    public string ProductModel { get; set; } = "";

    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = "";

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = "";

    /// <summary>规格</summary>
    public string Spec { get; set; } = "";

    /// <summary>单位</summary>
    public string Unit { get; set; } = "";

    /// <summary>单位用量（生产 1 只成品所需物料量，以「只」为基准）</summary>
    public double QtyPerUnit { get; set; }

    /// <summary>损耗率（%，如 1.5 表示 1.5%）</summary>
    public double LossRate { get; set; }

    /// <summary>物料类型：主材 / 辅材 / 包材</summary>
    public string MaterialType { get; set; } = "";
}

/// <summary>
/// BOM 展示行：BOM 明细 + 按计划数量计算出的需求量，供网格直接绑定。
/// 由 <c>MesDatabase.BuildBomRequirementRows</c> 生成，供工单页明细区与 BOM 窗体复用。
/// </summary>
public class ErpBomRequirementRow
{
    /// <summary>物料编码</summary>
    public string MaterialCode { get; set; } = "";

    /// <summary>物料名称</summary>
    public string MaterialName { get; set; } = "";

    /// <summary>规格</summary>
    public string Spec { get; set; } = "";

    /// <summary>单位</summary>
    public string Unit { get; set; } = "";

    /// <summary>单位用量</summary>
    public double QtyPerUnit { get; set; }

    /// <summary>损耗率（%）</summary>
    public double LossRate { get; set; }

    /// <summary>需求量 = 单位用量 × (1 + 损耗率/100) × 计划数量</summary>
    public double Requirement { get; set; }

    /// <summary>物料类型：主材 / 辅材 / 包材</summary>
    public string MaterialType { get; set; } = "";
}
