using System.Text.Json;

namespace AILedger.Core.ContextBriefing;

// A response packet cannot grow with the entire task/manual. The common fixture envelope keeps
// exact bindings. An exceptional oversized identity set is disclosed, never silently truncated
// into what appears to be a complete candidate or ready action.
public static class ContractPacketBudget
{
    public const int MaximumUtf8Bytes = 64 * 1024;
    private static readonly JsonSerializerOptions Measurement = new() { WriteIndented = true };

    internal static NextActionContract Apply(NextActionContract packet)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(packet, Measurement).Length <= MaximumUtf8Bytes) return packet;
        var actions = packet.Actions.Select(action => action with
        {
            Requirements = action.Requirements.Select(requirement => requirement with
            {
                Status = requirement.References.Count > 0 ? "unknown" : requirement.Status,
                References = [],
                Detail = requirement.Detail is null ? null : "Detailed observation exceeds packet bound; status is not a readiness certificate."
            }).ToArray()
        }).ToArray();
        return packet with
        {
            DeliveryStatus = "incomplete", Actions = actions,
            Bindings = new(null, null, null, null, null, null), Return = null,
            Diagnostic = "Exact bindings/outcome exceed the 64 KiB delivery bound. Critical authoring shapes and requirements retained; no complete binding or readiness observation is claimed. This oversized selection is outside bounded delivery coverage."
        };
    }
}
