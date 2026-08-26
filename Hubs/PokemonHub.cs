using Microsoft.AspNetCore.SignalR;

namespace PokemonHelper.Hubs
{
    public class PokemonHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            if (Services.ScreenCaptureService.LastPartyData != null)
            {
                await Clients.Caller.SendAsync("UpdateOpponentParty", Services.ScreenCaptureService.LastPartyData);
            }
            await base.OnConnectedAsync();
        }

        public async Task ResetBattle()
        {
            Services.ScreenCaptureService.Instance?.ResetBattleState();
            await Clients.All.SendAsync("BattleReset");
        }
    }
}
