using System.Text.Json.Serialization;
using GTMP.Server.MasterApi.Infrastructure;

namespace GTMP.Server.MasterApi.Contracts;

/// <summary>Operational state of a status component.</summary>
/// <remarks>
/// Sent as a number because the launcher deserializes it into an enum without a
/// string converter. The values 1..4 are the only ones the launcher switches over.
/// </remarks>
public enum ComponentStatus
{
    /// <summary>Working normally.</summary>
    Operational = 1,

    /// <summary>Degraded, still usable.</summary>
    PerformanceIssues = 2,

    /// <summary>Part of the service is unavailable.</summary>
    PartialOutage = 3,

    /// <summary>Service is down.</summary>
    MajorOutage = 4
}

/// <summary>One line in the status page, shaped like the launcher's <c>ServerStatus</c>.</summary>
public sealed class StatusComponent
{
    /// <summary>Stable identifier.</summary>
    public int Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Short explanation of what this component does.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Status page link for this component.</summary>
    public string Link { get; set; } = string.Empty;

    /// <summary>Current state.</summary>
    public ComponentStatus Status { get; set; }

    /// <summary>Sort order on the status page.</summary>
    public int Order { get; set; }

    /// <summary>Grouping the component belongs to.</summary>
    [JsonPropertyName("group_id")]
    public int GroupId { get; set; }

    /// <summary>Whether the launcher should show this component.</summary>
    public bool Enabled { get; set; }

    /// <summary>Creation time, rendered as "yyyy-MM-dd HH:mm:ss".</summary>
    [JsonConverter(typeof(MicrosoftDateTimeConverter))]
    [JsonPropertyName("create_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Last modification, rendered as "yyyy-MM-dd HH:mm:ss".</summary>
    [JsonConverter(typeof(MicrosoftDateTimeConverter))]
    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>Deletion time, or null while the component is live.</summary>
    [JsonConverter(typeof(MicrosoftDateTimeConverter))]
    [JsonPropertyName("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    /// <summary>Human readable status.</summary>
    [JsonPropertyName("status_name")]
    public string StatusName { get; set; } = string.Empty;
}

/// <summary>Paging block for the status response.</summary>
/// <remarks>
/// The property on <see cref="StatusMeta"/> is misspelled in the launcher, and that
/// misspelling is the contract.
/// </remarks>
public sealed class Pagination
{
    /// <summary>Total number of components.</summary>
    public int Total { get; set; }

    /// <summary>Number returned on this page.</summary>
    public int Count { get; set; }

    /// <summary>Page size.</summary>
    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }

    /// <summary>Page currently returned.</summary>
    [JsonPropertyName("current_page")]
    public int CurrentPage { get; set; }

    /// <summary>Total number of pages.</summary>
    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    /// <summary>Navigation links, null when there is no paging.</summary>
    public PaginationLinks Links { get; set; } = new();
}

/// <summary>Navigation links for a paginated response.</summary>
public sealed class PaginationLinks
{
    /// <summary>Link to the first page.</summary>
    public string? First { get; set; }

    /// <summary>Link to the last page.</summary>
    public string? Last { get; set; }

    /// <summary>Link to the previous page.</summary>
    public string? Prev { get; set; }

    /// <summary>Link to the next page.</summary>
    public string? Next { get; set; }
}

/// <summary>Envelope metadata for the status response.</summary>
public sealed class StatusMeta
{
    /// <summary>
    /// Paging block. Named after the launcher's misspelling ("Oagination"); renaming
    /// it would deserialize into null and break the status view.
    /// </summary>
    public Pagination Oagination { get; set; } = new();
}

/// <summary>Body of <c>api/v1/components</c> on the status server.</summary>
public sealed class StatusComponentsResponse
{
    /// <summary>Response metadata.</summary>
    public StatusMeta Meta { get; set; } = new();

    /// <summary>Status components, ordered by <see cref="StatusComponent.Order"/>.</summary>
    public List<StatusComponent> Data { get; set; } = new();
}
