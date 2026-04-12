namespace Tracentic;

/// <summary>
/// Data about a single LLM call. Pass to RecordSpan after the
/// call completes. StartedAt and EndedAt are required; all other
/// fields are optional.
/// </summary>
public class TracenticSpan
{
    /// <summary>UTC timestamp when the LLM call started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>UTC timestamp when the LLM call completed.</summary>
    public DateTimeOffset EndedAt { get; set; }

    /// <summary>"anthropic", "openai", "google", etc.</summary>
    public string? Provider { get; set; }

    /// <summary>The model identifier used for this call.</summary>
    public string? Model { get; set; }

    /// <summary>Number of input/prompt tokens consumed.</summary>
    public int? InputTokens { get; set; }

    /// <summary>Number of output/completion tokens generated.</summary>
    public int? OutputTokens { get; set; }

    /// <summary>"chat", "completion", "embedding"</summary>
    public string? OperationType { get; set; }

    /// <summary>
    /// Call-specific attributes. These have the highest merge priority —
    /// they override scope and global attributes on key collision.
    /// </summary>
    public Dictionary<string, object>? Attributes { get; set; }
}
