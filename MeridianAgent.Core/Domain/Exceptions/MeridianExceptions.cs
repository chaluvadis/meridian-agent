namespace MeridianAgent.Domain.Exceptions;

/// <summary>The Search Agent could not find any venue that satisfies the hard constraints.</summary>
public sealed class NoVenuesFoundException : Exception
{
    public NoVenuesFoundException(string reason)
        : base(reason)
    {
    }

    public string Reason => Message;
}

/// <summary>An agent failed after exhausting its retry attempts.</summary>
public sealed class AgentInvocationException : Exception
{
    public AgentInvocationException(string agentName, string message)
        : base($"{agentName} agent failed: {message}")
    {
        AgentName = agentName;
    }

    public string AgentName { get; }
}
