using Atlas.Core.Contracts;

namespace Atlas.AI;

public sealed class VoiceIntentSafetyGuard
{
    private static readonly string[] RiskyActionMarkers =
    [
        "delete",
        "remove",
        "wipe",
        "purge",
        "clear",
        "clean",
        "move",
        "reorganize",
        "reorganise",
        "archive"
    ];

    private static readonly string[] BulkScopeMarkers =
    [
        "everything",
        "all files",
        "entire drive",
        "whole drive",
        "entire computer",
        "whole computer",
        "clean it up",
        "clean up my pc"
    ];

    private static readonly string[] ProtectedTargetMarkers =
    [
        "appdata",
        "windows",
        "program files",
        "programdata",
        "system32",
        "registry",
        ".ssh",
        ".git"
    ];

    public VoiceIntentResponse Apply(string transcript, VoiceIntentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.NeedsConfirmation)
        {
            return response;
        }

        var combined = $"{transcript} {response.ParsedIntent}".Trim();
        if (string.IsNullOrWhiteSpace(combined))
        {
            response.NeedsConfirmation = true;
            return response;
        }

        if (ContainsMarker(combined, RiskyActionMarkers)
            || ContainsMarker(combined, BulkScopeMarkers)
            || ContainsMarker(combined, ProtectedTargetMarkers))
        {
            response.NeedsConfirmation = true;
        }

        return response;
    }

    private static bool ContainsMarker(string text, IEnumerable<string> markers)
    {
        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
