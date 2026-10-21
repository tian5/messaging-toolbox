namespace MessagingToolbox.Application.Common.Dtos;

/// <summary>Exported messages as a downloadable JSON file; content is base64-encoded UTF-8.</summary>
public sealed record ExportedMessagesFileDto(
    string FileName,
    string Base64Content);
