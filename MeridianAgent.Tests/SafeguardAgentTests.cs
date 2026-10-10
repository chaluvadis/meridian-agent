using MeridianAgent.Domain;
using MeridianAgent.Domain.Exceptions;
using MeridianAgent.Infrastructure.AI;
using MeridianAgent.Infrastructure.AI.Agents;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeridianAgent.Tests;

public class SafeguardAgentTests
{
    private static SafeguardAgent CreateAgent(params string[] responses) =>
        new(new ScriptedLlmClient(responses), NullLogger<SafeguardAgent>.Instance);

    [Fact]
    public async Task CheckAsync_ParsesClearedReport()
    {
        var agent = CreateAgent("""{"status": "CLEARED", "warnings": []}""");

        var report = await agent.CheckAsync(TestData.Itinerary, TestData.Request());

        Assert.Equal(SafeguardStatus.Cleared, report.Status);
        Assert.Empty(report.Warnings);
        Assert.False(report.IsFlagged);
    }

    [Fact]
    public async Task CheckAsync_ParsesFlaggedReport()
    {
        var agent = CreateAgent("""
            {
              "status": "FLAGGED",
              "warnings": [
                {
                  "applies_to_venue": "Ippodo Tea Room",
                  "severity": "medium",
                  "rule_category": "photography",
                  "risk_description": "Photography is prohibited inside the main hall.",
                  "gentle_fix": "Keep phones pocketed inside the hall."
                }
              ]
            }
            """);

        var report = await agent.CheckAsync(TestData.Itinerary, TestData.Request());

        Assert.True(report.IsFlagged);
        var warning = Assert.Single(report.Warnings);
        Assert.Equal("Ippodo Tea Room", warning.AppliesToVenue);
        Assert.Equal(RiskSeverity.Medium, warning.Severity);
        Assert.Equal(RuleCategory.Photography, warning.RuleCategory);
        Assert.Equal("Keep phones pocketed inside the hall.", warning.GentleFix);
    }

    [Fact]
    public async Task CheckAsync_AcceptsLowercaseStatusToken()
    {
        var agent = CreateAgent("""{"status": "cleared", "warnings": []}""");

        var report = await agent.CheckAsync(TestData.Itinerary, TestData.Request());

        Assert.Equal(SafeguardStatus.Cleared, report.Status);
    }

    [Fact]
    public async Task CheckAsync_NormalizesFlaggedWithNoWarningsToCleared()
    {
        var agent = CreateAgent("""{"status": "FLAGGED", "warnings": []}""");

        var report = await agent.CheckAsync(TestData.Itinerary, TestData.Request());

        Assert.False(report.IsFlagged);
        Assert.Equal(SafeguardStatus.Cleared, report.Status);
    }

    [Fact]
    public async Task CheckAsync_ToleratesMissingOptionalWarningFields()
    {
        var agent = CreateAgent("""
            {
              "status": "FLAGGED",
              "warnings": [{"applies_to_venue": "Ippodo Tea Room"}]
            }
            """);

        var report = await agent.CheckAsync(TestData.Itinerary, TestData.Request());

        var warning = Assert.Single(report.Warnings);
        Assert.Equal(RiskSeverity.Low, warning.Severity);
        Assert.Equal(RuleCategory.Other, warning.RuleCategory);
    }

    [Fact]
    public async Task CheckAsync_RequiresStatus()
    {
        var agent = CreateAgent("""{"warnings": []}""", """{"warnings": []}""");

        await Assert.ThrowsAsync<AgentInvocationException>(() =>
            agent.CheckAsync(TestData.Itinerary, TestData.Request()));
    }

    [Fact]
    public async Task CheckAsync_PromptCarriesRulebookAndUserContext()
    {
        var client = new ScriptedLlmClient("""{"status": "CLEARED", "warnings": []}""");
        var agent = new SafeguardAgent(client, NullLogger<SafeguardAgent>.Instance);

        await agent.CheckAsync(TestData.Itinerary, TestData.Request());

        var prompt = client.Calls[0].Prompt;
        Assert.Contains("Kyoto, Japan", prompt);
        Assert.Contains("Photography is prohibited inside the main hall of Kiyomizu-dera.", prompt);
        Assert.Contains("Ippodo Tea Room", prompt);
        Assert.Contains("first-time visitor", prompt);
        Assert.Contains("light scarf in bag", prompt);
        Assert.Contains("Couple", prompt);
    }
}
