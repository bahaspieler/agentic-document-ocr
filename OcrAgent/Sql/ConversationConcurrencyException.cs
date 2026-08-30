namespace OcrAgent.Sql;

/// <summary>Signals that another request updated the conversation before this one saved.</summary>
public sealed class ConversationConcurrencyException(string conversationId)
    : Exception($"Conversation '{conversationId}' changed while this turn was running.");
