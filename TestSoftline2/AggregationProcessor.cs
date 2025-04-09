using System.Globalization;
using System.Text.Json;

public class AggregationProcessor
{
    private readonly List<IAggregator> _aggregators = new();

    // Чтение конфигурации из файла
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
            if (parts.Length != 5) continue;

            samples.Add(new CounterSample
            {
                dt = DateTime.ParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                @object = parts[1].Trim(),
                counter = parts[2].Trim(),
                instance = parts[3].Trim(),
                v = double.Parse(parts[4], CultureInfo.InvariantCulture)
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
                yield return sample;
        }
    }
}
