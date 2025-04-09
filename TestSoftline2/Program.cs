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

        Console.WriteLine("\n=== Dictionary Contents ===");
        PrintDictionary("Objects", processor.GetObjectDictionary());
        PrintDictionary("Counters", processor.GetCounterDictionary());
        PrintDictionary("Instances", processor.GetInstanceDictionary());
    }

    static void PrintDictionary(string name, Dictionary<string, int> dict)
    {
        Console.WriteLine($"\n{name} Dictionary:");
        foreach (var item in dict)
        {
            Console.WriteLine($"  {item.Key} = {item.Value}");
        }
    }
}
