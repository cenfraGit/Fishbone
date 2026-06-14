namespace Fishbone.Core.Tests;

public class FishboneEnvironmentTests
{
    [Fact]
    public void Declare_StoresValueInCurrentEnvironment()
    {
        var env = new FishboneEnvironment();

        env.Declare("answer", 42);

        Assert.Equal(42, env.GetValue("answer"));
        Assert.Equal(42, env.Values["answer"]);
    }

    [Fact]
    public void Declare_WhenNameAlreadyExistsInCurrentEnvironment_Throws()
    {
        var env = new FishboneEnvironment();
        env.Declare("answer", 42);

        Assert.Throws<Exception>(() => env.Declare("answer", 43));
    }

    [Fact]
    public void Assign_WhenNameExistsInCurrentEnvironment_UpdatesLocalValue()
    {
        var env = new FishboneEnvironment();
        env.Declare("counter", 1);

        env.Assign("counter", 2);

        Assert.Equal(2, env.GetValue("counter"));
    }

    [Fact]
    public void Assign_WhenNameExistsInParentEnvironment_UpdatesParentValue()
    {
        var parent = new FishboneEnvironment();
        var child = new FishboneEnvironment(parent);
        parent.Declare("counter", 1);

        child.Assign("counter", 2);

        Assert.Equal(2, parent.GetValue("counter"));
        Assert.Empty(child.Values);
    }

    [Fact]
    public void GetValue_WhenNameExistsInParentEnvironment_ReturnsParentValue()
    {
        var parent = new FishboneEnvironment();
        var child = new FishboneEnvironment(parent);
        parent.Declare("answer", 42);

        Assert.Equal(42, child.GetValue("answer"));
    }

    [Fact]
    public void GetValue_WhenNameDoesNotExist_Throws()
    {
        var env = new FishboneEnvironment();

        Assert.Throws<Exception>(() => env.GetValue("missing"));
    }

    [Fact]
    public void Assign_WhenNameDoesNotExist_Throws()
    {
        var env = new FishboneEnvironment();

        Assert.Throws<Exception>(() => env.Assign("missing", 1));
    }
}
