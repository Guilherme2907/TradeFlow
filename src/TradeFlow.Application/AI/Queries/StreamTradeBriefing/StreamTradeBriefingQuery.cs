using System.Runtime.CompilerServices;
using System.Text;
using MediatR;
using Microsoft.Extensions.AI;
using TradeFlow.Application.Catalog.Contracts;
using TradeFlow.Application.Common.Security;

namespace TradeFlow.Application.AI.Queries.StreamTradeBriefing;

public sealed record StreamTradeBriefingQuery(string Question) : IStreamRequest<string>;

public sealed class StreamTradeBriefingQueryHandler : IStreamRequestHandler<StreamTradeBriefingQuery, string>
{
    private readonly IChatClient _chatClient;
    private readonly ICatalogModule _catalogModule;
    private readonly ICurrentUserContext _currentUser;

    public StreamTradeBriefingQueryHandler(
        IChatClient chatClient,
        ICatalogModule catalogModule,
        ICurrentUserContext currentUser)
    {
        _chatClient = chatClient;
        _catalogModule = catalogModule;
        _currentUser = currentUser;
    }

    public async IAsyncEnumerable<string> Handle(
        StreamTradeBriefingQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(request.Question))
        {
            yield break;
        }

        var activeCatalog = (await _catalogModule.ListActiveProductsAsync(cancellationToken))
            .OrderByDescending(p => p.UnitPrice)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("You are the TradeFlow B2B Commodity Desk Advisor.");
        sb.AppendLine("Answer concisely (max 120 words) in the same language as the trader's question.");
        sb.AppendLine("Compare numeric prices carefully (the table below is already sorted from highest UnitPrice to lowest UnitPrice):");
        foreach (var p in activeCatalog)
        {
            sb.AppendLine($"- {p.Sku}: {p.Name} [{p.Category}] — {p.UnitPrice:N2} {p.Currency} per {p.UnitOfMeasure}, MOQ={p.MinimumOrderQuantity}");
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, sb.ToString()),
            new(ChatRole.User, request.Question.Trim())
        };

        var options = new ChatOptions
        {
            Temperature = 0.0f,
            MaxOutputTokens = 300
        };

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }
}
