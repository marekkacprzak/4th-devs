using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace ProxyAgent.Services;

public class SessionManager
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _sessions = new();

    public List<ChatMessage> GetOrCreateSession(string sessionId)
    {
        return _sessions.GetOrAdd(sessionId, _ => new List<ChatMessage>());
    }

    public void AddMessage(string sessionId, ChatMessage message)
    {
        var session = GetOrCreateSession(sessionId);
        lock (session) { session.Add(message); }
    }

    public List<ChatMessage> GetMessages(string sessionId)
    {
        var session = GetOrCreateSession(sessionId);
        lock (session) { return new List<ChatMessage>(session); }
    }
}
