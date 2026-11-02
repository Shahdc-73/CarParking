using Microsoft.AspNetCore.SignalR;
using ParkWise.Application.Common;

namespace ParkWise.Api.Hubs;

/// <summary>Sends "this lot changed" to everyone watching it. A failure is logged and swallowed: the check-in already happened.</summary>
public sealed class HubAvailabilityNotifier(IHubContext<AvailabilityHub> hub, ILogger<HubAvailabilityNotifier> logger) : IAvailabilityNotifier
{
    public async Task LotChangedAsync(Guid lotId, CancellationToken ct = default)
    {
        try
        {
            await hub.Clients.Groups(AvailabilityHub.LotGroup(lotId), AvailabilityHub.AllLotsGroup)
                .SendAsync(AvailabilityHub.LotChangedMethod, lotId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not notify clients that lot {LotId} changed.", lotId);
        }
    }
}
