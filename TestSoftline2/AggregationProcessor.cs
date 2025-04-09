using System.Globalization;
using System.Security.AccessControl;
using System.Text.Json;

public class AggregationProcessor
{
    private readonly List<IAggregator> _aggregators = new();

    private static readonly Dictionary<string, int> _objectNameToId = new();
    private static readonly Dictionary<string, int> _counterNameToId = new();
    private static readonly Dictionary<string, int> _instanceNameToId = new();

    public Dictionary<string, int> GetObjectDictionary() => _objectNameToId;
    public Dictionary<string, int> GetCounterDictionary() => _counterNameToId;
    public Dictionary<string, int> GetInstanceDictionary() => _instanceNameToId;

    public AggregationProcessor(string configPath)
    {
        var configJson = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(configJson);
        var root = doc.RootElement;
        var rulesSection = root.GetProperty("perf.aggregation.rules");

        foreach (var obj in rulesSection.EnumerateObject())
        {
            var rules = JsonSerializer.Deserialize<List<AggregationRule>>(
                obj.Value.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            foreach (var rule in rules)
            {
                var aggregator = CodeGenerator.GenerateAggregator(
                    obj.Name,
                    rule.Counters,
                    rule.AggregationExpression,
                    rule.InstanceSuffix
                );
                _aggregators.Add(aggregator);
            }
        }
    }

    public static List<CounterSample> ReadInputSamples(string inputPath)
    {
        var samples = new List<CounterSample>();

        foreach (var line in File.ReadAllLines(inputPath))
        {
            var parts = line.Split(';');
            if (parts.Length != 5)
                throw new FormatException($"Invalid line format: {line}");

            var dt = DateTime.Parse(parts[0].Trim());
            var objectName = parts[1].Trim();
            var counterName = parts[2].Trim();
            var instanceName = parts[3].Trim();

            if (!double.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"Invalid value format: {parts[4]}");
            }

            samples.Add(new CounterSample
            {
                dt = dt,
                @object = objectName,
                counter = counterName,
                instance = instanceName,
                v = value
            });
        }

        return samples;
    }

    public IEnumerable<CounterSample> Process(IEnumerable<CounterSample> input)
    {
        var groupedByTime = input.GroupBy(s => s.dt);
        foreach (var timeGroup in groupedByTime)
        {
            var output = new List<CounterSample>(timeGroup);
            foreach (var aggregator in _aggregators)
                output.AddRange(aggregator.Aggregate(timeGroup));

            foreach (var sample in output.OrderBy(s => s.dt))
            {
                if (!_objectNameToId.TryGetValue(sample.@object, out var objectId))
                {
                    objectId = _objectNameToId.Count + 1;
                    _objectNameToId[sample.@object] = objectId;
                }

                if (!_counterNameToId.TryGetValue(sample.counter, out var counterId))
                {
                    counterId = _counterNameToId.Count + 1;
                    _counterNameToId[sample.counter] = counterId;
                }

                if (!_instanceNameToId.TryGetValue(sample.instance, out var instanceId))
                {
                    instanceId = _instanceNameToId.Count + 1;
                    _instanceNameToId[sample.instance] = instanceId;
                }

                yield return new CounterSample
                {
                    dt = sample.dt,
                    @object = objectId.ToString(),
                    counter = counterId.ToString(),
                    instance = instanceId.ToString(),
                    v = sample.v,
                };
            }
                
        }
    }
}
