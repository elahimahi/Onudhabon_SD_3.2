namespace Onudhabon.Services
{
    public interface ILlmChatService
    {
        Task<string> GetChatResponseAsync(string userMessage, string? systemContext = null, CancellationToken cancellationToken = default);
    }
}
