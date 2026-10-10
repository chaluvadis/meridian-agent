namespace MeridianAgent.Tests;

public class AgentBlackboardTests
{
    [Fact]
    public void SetAndGet_RoundTripsTypedValues()
    {
        var blackboard = new AgentBlackboard();

        blackboard.PublishConstraints(TestData.Constraints);
        blackboard.PublishItinerary(TestData.Itinerary);

        Assert.Equal(QueryIntent.Food, blackboard.Constraints!.QueryIntent);
        Assert.Single(blackboard.Itinerary!.Venues);
    }

    [Fact]
    public void Get_ReturnsDefaultForUnknownKey()
    {
        var blackboard = new AgentBlackboard();

        Assert.Null(blackboard.Constraints);
        Assert.Null(blackboard.SafeguardReport);
        Assert.Null(blackboard.Response);
        Assert.False(blackboard.TryGet<string>("missing", out _));
    }

    [Fact]
    public void History_RecordsEveryWriteInOrder()
    {
        var blackboard = new AgentBlackboard();

        blackboard.PublishQuery("anything open this late?");
        blackboard.PublishConstraints(TestData.Constraints);
        blackboard.PublishResponse("Top pick: Karak House.");

        var history = blackboard.History;
        Assert.Equal(3, history.Count);
        Assert.Equal(AgentBlackboard.Keys.Query, history[0].Key);
        Assert.Equal(AgentBlackboard.Keys.Constraints, history[1].Key);
        Assert.Equal(AgentBlackboard.Keys.Response, history[2].Key);
        Assert.Equal("Top pick: Karak House.", blackboard.Response);
    }

    [Fact]
    public void Clear_WipesValuesAndHistory()
    {
        var blackboard = new AgentBlackboard();
        blackboard.PublishQuery("hello");

        blackboard.Clear();

        Assert.Empty(blackboard.History);
        Assert.Null(blackboard.Query);
    }

    [Fact]
    public void Set_RejectsEmptyKeys()
    {
        var blackboard = new AgentBlackboard();

        Assert.Throws<ArgumentException>(() => blackboard.Set("  ", "value"));
    }

    [Fact]
    public void GenericSetGet_WorksForAnyPayload()
    {
        var blackboard = new AgentBlackboard();

        blackboard.Set("retry_count", 3);

        Assert.Equal(3, blackboard.Get<int>("retry_count"));
        Assert.Equal(0, blackboard.Get<int>("absent"));
    }

    [Fact]
    public void Keys_UseSnakeCaseNames()
    {
        Assert.Equal("extracted_constraints", AgentBlackboard.Keys.Constraints);
        Assert.Equal("proposed_itinerary", AgentBlackboard.Keys.Itinerary);
        Assert.Equal("safeguard_report", AgentBlackboard.Keys.SafeguardReport);
    }
}
