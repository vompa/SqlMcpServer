namespace SqlMcpServer.Data;

/// <summary>Wird geworfen, wenn eine Abfrage die Sicherheitsregeln verletzt. Die Meldung ist für den Agenten bestimmt.</summary>
public sealed class QueryRejectedException(string message) : Exception(message);
