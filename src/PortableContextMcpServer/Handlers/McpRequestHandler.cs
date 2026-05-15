using PortableContextMcpServer.Contracts;
using PortableContextMcpServer.Models;
using PortableContextMcpServer.Services;

namespace PortableContextMcpServer.Handlers;

public sealed class McpRequestHandler
{
    private readonly IContextRepository _repository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IPolicyService _policyService;

    public McpRequestHandler(
        IContextRepository repository,
        IEmbeddingService embeddingService,
        IPolicyService policyService)
    {
        _repository = repository;
        _embeddingService = embeddingService;
        _policyService = policyService;
    }

    public async Task<GetContextResponse> HandleGetContextAsync(GetContextRequest request)
    {
        var queryEmbedding = await _embeddingService.CreateEmbeddingAsync(request.Query);
        var entries = await _repository.SearchAsync(queryEmbedding, request.MaxResults);
        var visible = entries
            .Where(entry => _policyService.IsVisible(entry, request.ToolId))
            .Select(entry => new ContextEntryDto
            {
                Id = entry.Id,
                Category = entry.Category,
                Text = entry.Text,
                SourceTool = entry.SourceTool,
                CreatedAt = entry.CreatedAt,
                UpdatedAt = entry.UpdatedAt,
                Score = ComputeScore(queryEmbedding, entry.Embedding)
            })
            .OrderByDescending(dto => dto.Score)
            .ToList();

        return new GetContextResponse { Entries = visible };
    }

    public async Task<UpdateContextResponse> HandleUpdateContextAsync(UpdateContextRequest request)
    {
        var embedding = await _embeddingService.CreateEmbeddingAsync(request.Text);
        var entry = new ContextEntry
        {
            Category = request.Category,
            Text = request.Text,
            Embedding = embedding,
            SourceTool = request.SourceTool,
            VisibilityPolicy = request.VisibilityPolicy ?? VisibilityPolicy.Default,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _repository.SaveAsync(entry);

        return new UpdateContextResponse
        {
            Success = true,
            EntryId = entry.Id,
            Message = "Context entry saved."
        };
    }

    private static float ComputeScore(float[] query, float[] candidate)
    {
        if (query.Length != candidate.Length || query.Length == 0)
        {
            return 0f;
        }

        var score = 0f;
        for (var i = 0; i < query.Length; i++)
        {
            score += query[i] * candidate[i];
        }

        return score;
    }
}
