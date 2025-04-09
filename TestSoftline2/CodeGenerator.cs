using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System.Reflection;
using System.Text;

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
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
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
