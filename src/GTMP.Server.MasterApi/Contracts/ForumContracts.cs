using System.Text.Json.Serialization;

namespace GTMP.Server.MasterApi.Contracts;

/// <summary>A news or troubleshooting entry, shaped like the launcher's <c>ForumPostItem</c>.</summary>
/// <remarks>
/// Almost every field is snake_case because it maps onto a forum API rather than onto
/// something the launcher invented. <see cref="Title"/> is the exception.
/// </remarks>
public sealed class ForumPost
{
    /// <summary>Thread identifier, used to request a single article.</summary>
    [JsonPropertyName("thread_id")]
    public int Id { get; set; }

    /// <summary>
    /// Publication time in Unix seconds. The launcher runs this through a converter
    /// that calls <c>Convert.ToInt64</c> on the raw value, so a formatted date would throw.
    /// </summary>
    [JsonPropertyName("post_date")]
    public long Timestamp { get; set; }

    /// <summary>Headline.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Author name.</summary>
    [JsonPropertyName("username")]
    public string Author { get; set; } = string.Empty;

    /// <summary>Body text.</summary>
    [JsonPropertyName("message")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Canonical link to the article.</summary>
    [JsonPropertyName("absolute_url")]
    public string Url { get; set; } = string.Empty;
}
