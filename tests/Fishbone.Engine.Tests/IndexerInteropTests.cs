namespace Fishbone.Engine.Tests;

public class IndexerInteropTests
{
    [Fact]
    public void Run_CustomIndexer_ReadsWritesAndConvertsArguments()
    {
        var sample = new CustomIndexer();
        var configuration = new FishboneConfiguration().RegisterBuiltIn("sample", sample);

        var env = FishboneEngine.Run("""
sample["2"] = "7";
let value = sample[2];
""", configuration);

        Assert.Equal(7, sample[2]);
        Assert.Equal(7, env.GetValue("value"));
    }

    [Fact]
    public void Run_ArrayIndexer_ReadsWritesAndConvertsValues()
    {
        int[] values = [1, 2, 3];
        var configuration = new FishboneConfiguration().RegisterBuiltIn("values", values);

        var env = FishboneEngine.Run("""
values[1] = "9";
let result = values[1];
""", configuration);

        Assert.Equal([1, 9, 3], values);
        Assert.Equal(9, env.GetValue("result"));
    }

    [Fact]
    public void Run_TypedDictionaryIndexer_AddsAndConvertsEntries()
    {
        var values = new Dictionary<string, int>();
        var configuration = new FishboneConfiguration().RegisterBuiltIn("values", values);

        FishboneEngine.Run("values[123] = \"8\";", configuration);

        Assert.Equal(8, values["123"]);
    }

    [Fact]
    public void Run_ExplicitCollectionInterfaces_UseListAndDictionaryFallbacks()
    {
        var list = new ExplicitList();
        var dictionary = new ExplicitDictionary();
        var configuration = new FishboneConfiguration()
            .RegisterBuiltIn("list", list)
            .RegisterBuiltIn("dictionary", dictionary);

        var env = FishboneEngine.Run("""
list[1] = 9;
dictionary["new"] = 3;
let listValue = list[1];
let dictionaryValue = dictionary["new"];
""", configuration);

        Assert.Equal(9, env.GetValue("listValue"));
        Assert.Equal(3, env.GetValue("dictionaryValue"));
    }

    [Fact]
    public void Run_ReadOnlyIndexer_RejectsAssignment()
    {
        var configuration = new FishboneConfiguration()
            .RegisterBuiltIn("sample", new ReadOnlyIndexer());

        Exception exception = Assert.ThrowsAny<Exception>(() =>
            FishboneEngine.Run("sample[0] = 10;", configuration));

        Assert.Contains("read-only", exception.Message);
    }

    [Fact]
    public void Run_IncompatibleIndexerValue_ThrowsClearError()
    {
        var configuration = new FishboneConfiguration()
            .RegisterBuiltIn("sample", new CustomIndexer());

        Exception exception = Assert.ThrowsAny<Exception>(() =>
            FishboneEngine.Run("sample[0] = [1];", configuration));

        Assert.Contains("not compatible", exception.Message);
    }

    [Fact]
    public void Run_IndexedAssignment_EvaluatesTargetIndexAndValueOnce()
    {
        var values = new List<object> { 0 };
        int targetCalls = 0;
        int indexCalls = 0;
        int valueCalls = 0;
        var configuration = new FishboneConfiguration()
            .RegisterFunction("getTarget", new Func<List<object>>(() =>
            {
                targetCalls++;
                return values;
            }))
            .RegisterFunction("getIndex", new Func<int>(() =>
            {
                indexCalls++;
                return 0;
            }))
            .RegisterFunction("getValue", new Func<int>(() =>
            {
                valueCalls++;
                return 42;
            }));

        FishboneEngine.Run("getTarget()[getIndex()] = getValue();", configuration);

        Assert.Equal(42, values[0]);
        Assert.Equal(1, targetCalls);
        Assert.Equal(1, indexCalls);
        Assert.Equal(1, valueCalls);
    }

    private sealed class CustomIndexer
    {
        private readonly Dictionary<int, int> _values = [];

        public int this[int index]
        {
            get => _values.TryGetValue(index, out int value) ? value : 0;
            set => _values[index] = value;
        }
    }

    private sealed class ReadOnlyIndexer
    {
        public int this[int index] => index;
    }

    private sealed class ExplicitList : System.Collections.CollectionBase
    {
        public ExplicitList()
        {
            List.Add(1);
            List.Add(2);
        }
    }

    private sealed class ExplicitDictionary : System.Collections.DictionaryBase
    {
        public ExplicitDictionary()
        {
            Dictionary["existing"] = 1;
        }
    }
}