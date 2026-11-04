using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace ParkJomV2.DTOs;

/// <summary>One prior chat turn sent back by the client (stateless conversation).</summary>
public class CopilotMessageDto
{
    /// <summary>"user" or "model".</summary>
    [Required]
    [RegularExpression("^(user|model)$")]
    public string Role { get; set; } = "user";

    [Required]
    [StringLength(4000)]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Opaque, server-generated summary of what the model turn showed (bay IDs, station IDs),
    /// so follow-ups such as "book the second one" can be resolved without server-side state.
    /// </summary>
    [StringLength(4000)]
    public string? Context { get; set; }
}

public class CopilotChatRequest
{
    [MaxLength(20)]
    public List<CopilotMessageDto> History { get; set; } = new();

    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;

    [Range(-90, 90)]
    public double? Lat { get; set; }

    [Range(-180, 180)]
    public double? Lng { get; set; }
}

/// <summary>Structured UI card rendered by the frontend. Type: "station" | "parking" | "departures" | "bookingProposal".</summary>
public class CopilotCardDto
{
    public string Type { get; set; } = string.Empty;
    public JsonNode? Data { get; set; }
}

public class CopilotChatResponse
{
    public int Code { get; set; } = 200;
    public bool Success { get; set; } = true;
    public string Message { get; set; } = "OK";
    public string Reply { get; set; } = string.Empty;
    public string? Context { get; set; }
    public List<CopilotCardDto> Cards { get; set; } = new();
    public List<string> ToolsUsed { get; set; } = new();
}
