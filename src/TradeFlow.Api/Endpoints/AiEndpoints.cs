using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TradeFlow.Api.Common.Errors;
using TradeFlow.Api.Common.Security;
using TradeFlow.Application.AI.Commands.ParseOrderDraft;
using TradeFlow.Application.AI.Commands.ReindexKnowledgeBase;
using TradeFlow.Application.AI.Exceptions;
using TradeFlow.Application.AI.Models;
using TradeFlow.Application.AI.Queries.AskKnowledgeQuestion;
using TradeFlow.Application.AI.Queries.StreamTradeBriefing;
using TradeFlow.Application.AI.Rag;

namespace TradeFlow.Api.Endpoints;

public static class AiEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai")
            .WithTags("AI Copilot");

        group.MapPost("/orders/parse-draft", ParseOrderDraftAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderWritePolicy)
            .WithName("ParseOrderDraft")
            .WithSummary("Extracts structured commodity order intent via Microsoft.Extensions.AI and verifies prices/MOQs against the authoritative Catalog.")
            .Produces<OrderDraftProposalResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/orders/stream-briefing", StreamTradeBriefingAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderReadPolicy)
            .WithName("StreamTradeBriefing")
            .WithSummary("Streams real-time commodity desk advisory tokens over Server-Sent Events (SSE).")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/knowledge/reindex", ReindexKnowledgeAsync)
            .RequireAuthorization(TradeFlowPolicies.AdminPolicy)
            .WithName("ReindexKnowledgeBase")
            .WithSummary("Chunks, embeds, and indexes active Catalog specifications and Customer contracts into PostgreSQL pgvector.")
            .Produces<KnowledgeReindexResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/knowledge/ask", AskKnowledgeAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderReadPolicy)
            .WithName("AskKnowledgeQuestion")
            .WithSummary("Executes multi-tenant Hybrid RAG (pgvector HNSW + PostgreSQL Full-Text RRF) with mandatory customer_id pre-filtering and inline citations.")
            .Produces<KnowledgeAnswerResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> ReindexKnowledgeAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReindexKnowledgeBaseCommand(), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> AskKnowledgeAsync(
        AskKnowledgeRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var query = new AskKnowledgeQuestionQuery(
            request.Question,
            request.TopK ?? 5,
            request.FilterCustomerId);

        var result = await sender.Send(query, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ParseOrderDraftAsync(
        ParseOrderDraftRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ParseOrderDraftCommand(request.Prompt), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task StreamTradeBriefingAsync(
        StreamTradeBriefingRequest request,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (var delta in sender.CreateStream(new StreamTradeBriefingQuery(request.Question), cancellationToken))
            {
                var payload = JsonSerializer.Serialize(new SseTokenDelta("delta", delta), SseJsonOptions);
                await httpContext.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }

            var donePayload = JsonSerializer.Serialize(new SseTokenDelta("done", string.Empty), SseJsonOptions);
            await httpContext.Response.WriteAsync($"data: {donePayload}\n\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }
        catch (AiProviderException ex)
        {
            var errPayload = JsonSerializer.Serialize(new SseTokenDelta("error", ex.Message, ex.ErrorCode), SseJsonOptions);
            await httpContext.Response.WriteAsync($"data: {errPayload}\n\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected cleanly
        }
        catch (Exception)
        {
            var errPayload = JsonSerializer.Serialize(
                new SseTokenDelta("error", "Order Copilot stream is temporarily unavailable.", "AI_COPILOT_UNAVAILABLE"),
                SseJsonOptions);
            await httpContext.Response.WriteAsync($"data: {errPayload}\n\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }
    }
}

public sealed record ParseOrderDraftRequest(string Prompt);

public sealed record StreamTradeBriefingRequest(string Question);

public sealed record AskKnowledgeRequest(
    string Question,
    int? TopK = 5,
    Guid? FilterCustomerId = null);

public sealed record SseTokenDelta(string Type, string Text, string? Code = null);
