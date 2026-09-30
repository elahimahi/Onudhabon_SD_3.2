using Onudhabon.Models;

namespace Onudhabon.Services;

public interface IChatOrchestrator
{
    Task<string> SendMessageAsync(ChatRequest request, string ownerKey, CancellationToken cancellationToken = default);
}
