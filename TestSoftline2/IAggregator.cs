public interface IAggregator
{
    IEnumerable<CounterSample> Aggregate(IEnumerable<CounterSample> samples);
}
