using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using PokemonHelper.Hubs;
using PokemonHelper.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PokemonHelper.Services
{
    public class DataBroadcastService : BackgroundService
    {
        private readonly IHubContext<PokemonHub> _hubContext;
        private List<Pokemon> _pokemonList = new();
        private Random _random = new();

        public DataBroadcastService(IHubContext<PokemonHub> hubContext)
        {
            _hubContext = hubContext;
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                string filePath = @"Data\master.json";
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var masterData = JsonSerializer.Deserialize<MasterData>(json);
                    if (masterData != null)
                    {
                        _pokemonList = masterData.Species;
                    }
                }
            }
            catch { /* 파싱 에러 무시 */ }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 3초마다 무작위 포켓몬 데이터를 웹 브라우저로 발송합니다.
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_pokemonList.Count > 0)
                {
                    var randomPokemon = _pokemonList[_random.Next(_pokemonList.Count)];
                    
                    // 연결된 모든 웹 브라우저 클라이언트에게 'ReceivePokemonUpdate' 이벤트를 날립니다.
                    await _hubContext.Clients.All.SendAsync("ReceivePokemonUpdate", randomPokemon, stoppingToken);
                }

                await Task.Delay(3000, stoppingToken);
            }
        }
    }
}
