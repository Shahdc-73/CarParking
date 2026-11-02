using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ParkWise.Api.Hubs;

/// <summary>
/// Real-time channel. The server sends ONE message, <c>LotChanged(lotId)</c>, with no parking data in it: browsers re-fetch through
/// the normal API, so what each person may see (a driver must not see other cars' plates) is decided in one place.
///
/// Clients choose what they listen to: <c>JoinLot</c> for one lot's board, <c>JoinAllLots</c> for overview screens.
/// Groups keep a busy garage's chatter away from people who are looking at a different lot.
/// </summary>
[Authorize]
public sealed class AvailabilityHub : Hub
{
    public const string AllLotsGroup = "lots";
    public const string LotChangedMethod = "LotChanged";

    public static string LotGroup(Guid lotId) => $"lot:{lotId:N}";

    public Task JoinLot(Guid lotId) => Groups.AddToGroupAsync(Context.ConnectionId, LotGroup(lotId));

    public Task LeaveLot(Guid lotId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, LotGroup(lotId));

    public Task JoinAllLots() => Groups.AddToGroupAsync(Context.ConnectionId, AllLotsGroup);

    public Task LeaveAllLots() => Groups.RemoveFromGroupAsync(Context.ConnectionId, AllLotsGroup);
}
