using Microsoft.Agents.AI;

namespace OcrAgent.Agent;

/// <summary>
/// One multi-turn exchange with the agent: the model's accumulated state and the id it is recorded
/// under.
/// </summary>
/// <remarks>
/// The two belong together because they answer the same question from different ends. The session is
/// what lets a later turn say "binarize that one" and have it mean the reference from three turns
/// ago; the id is what lets the run records from all those turns be found as one exchange afterwards.
/// A caller that holds the pair holds the whole conversation.
/// <para>
/// A caller with more than one conversation in flight, which is what a web front end is, keeps one of
/// these per user rather than one per process.
/// </para>
/// </remarks>
public sealed class AgentConversation
{
    /// <summary>Id written to every run record produced by a turn of this conversation.</summary>
    public string Id { get; }

    /// <summary>Model-side state carrying the turns so far.</summary>
    /// <remarks>
    /// Updated in place as turns run. Phase 7 hands this to <c>AIAgent.SerializeSessionAsync</c> to
    /// keep a conversation alive across requests, which is the reason it is reachable at all.
    /// </remarks>
    public AgentSession Session { get; }

    /// <summary>Pairs a session with the id its runs are recorded under.</summary>
    /// <param name="id">Conversation id.</param>
    /// <param name="session">Session created or restored by <see cref="AgentService"/>.</param>
    public AgentConversation(string id, AgentSession session)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Conversation id is required.", nameof(id));

        Id = id;
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }
}
