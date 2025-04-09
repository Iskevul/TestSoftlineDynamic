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
