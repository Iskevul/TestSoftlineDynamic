using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.CodeDom.Compiler;
using Microsoft.CSharp;
using System.Runtime.InteropServices;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Globalization;

public struct CounterSample
{
    public DateTime dt { get; set; }
    public string @object { get; set; }
    public string counter { get; set; }
    public string instance { get; set; }
    public double v { get; set; }
}

public interface IAggregator
{
    IEnumerable<CounterSample> Aggregate(IEnumerable<CounterSample> samples);
}

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

public class AggregationRule
{
    public string AggregationExpression { get; set; } = default!;
    public string InstanceSuffix { get; set; } = default!;
    public List<string> Counters { get; set; } = new();
}

public static class CodeGenerator
{
    public static IAggregator GenerateAggregator(
        string objectName,
        List<string> counters,
        string aggregationExpression,
        string instanceSuffix
    )
    {
        var code = new StringBuilder();
        string className = SanitizeClassName($"{objectName}_{instanceSuffix}_Aggregator");

        code.AppendLine("using System;");
        code.AppendLine("using System.Collections.Generic;");
        code.AppendLine("using System.Linq;");
        code.AppendLine();
        code.AppendLine($"public class {className} : IAggregator");
        code.AppendLine("{");
        code.AppendLine("    public IEnumerable<CounterSample> Aggregate(IEnumerable<CounterSample> samples)");
        code.AppendLine("    {");
        code.AppendLine($"        var targetCounters = new HashSet<string>(new[] {{ {string.Join(", ", counters.Select(c => $"\"{EscapeString(c)}\""))} }});");
        code.AppendLine($@"
        var filtered = samples.Where(s => 
            s.@object == ""{EscapeString(objectName)}"" && 
            targetCounters.Contains(s.counter)
        );

        return filtered
            .GroupBy(s => new {{ s.@object, s.counter }})
            .Select(g => new CounterSample
            {{
                dt = g.First().dt,
                @object = g.Key.@object,
                counter = g.Key.counter,
                instance = (g.First().instance ?? """") + ""{EscapeString(instanceSuffix)}"",
                v = {PrepareExpression(aggregationExpression)}
            }});");
        code.AppendLine("    }");
        code.AppendLine("}");

        return CompileCode(code.ToString(), className);
    }

    private static string EscapeString(string input) => input.Replace("\"", "\"\"");

    private static string PrepareExpression(string expr) =>
        expr.Replace("values", "g.Select(s => s.v)");

    private static string SanitizeClassName(string input) =>
        new string(input.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray())
            .TrimStart('_');

    private static IAggregator CompileCode(string code, string className)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);

        var references = new[]
        {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location), // System.Linq.dll
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        MetadataReference.CreateFromFile(typeof(CounterSample).Assembly.Location)
    };

        var compilation = CSharpCompilation.Create("DynamicAssembly")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(references)
            .AddSyntaxTrees(syntaxTree);

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics);
            throw new InvalidOperationException($"Ошибки компиляции:\n{errors}");
        }

        var assembly = Assembly.Load(ms.ToArray());
        return (IAggregator)Activator.CreateInstance(assembly.GetType(className));
    }
}

public class Program
{
    public static void Main()
    {
        var processor = new AggregationProcessor("config.json");
        var inputSamples = AggregationProcessor.ReadInputSamples("input.txt");

        foreach (var sample in processor.Process(inputSamples))
        {
            Console.WriteLine($"{sample.dt:yyyy-MM-dd HH:mm:ss};{sample.@object};{sample.counter};{sample.instance};{sample.v}");
        }
    }
}