public class AggregationRule
{
    public string AggregationExpression { get; set; } = default!;
    public string InstanceSuffix { get; set; } = default!;
    public List<string> Counters { get; set; } = new();
}
