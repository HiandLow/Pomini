using System;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Http;
using System.IO;
using PokemonHelper.Hubs;
using PokemonHelper.Services;

namespace PokemonHelper
{
    public partial class App : System.Windows.Application
    {
        private IHost? _host;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            for (int i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--test-video" && i + 1 < e.Args.Length)
                {
                    ScreenCaptureService.TestVideoPath = e.Args[i + 1];
                    break;
                }
            }

            // 원본 PCH.App과 동일하게 Host를 빌드하여 의존성 주입(DI)과 웹 서버를 함께 켭니다.
            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseUrls("http://localhost:5000");
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddSignalR();
                        // 기존처럼 백그라운드 서비스가 아닌, 일반 싱글톤으로 등록 (WPF 타이머나 고정 스레드에서 구동 예정)
                        services.AddSingleton<ScreenCaptureService>(); 
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseDefaultFiles();
                        app.UseStaticFiles();
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = new PhysicalFileProvider(
                                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCH", "data-cache", "sprites")),
                            RequestPath = "/img/pokemon"
                        });
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = new PhysicalFileProvider(
                                Path.Combine(Environment.CurrentDirectory, "type-icons")),
                            RequestPath = "/img/types"
                        });
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapHub<PokemonHub>("/pokemonHub");
                            endpoints.MapGet("/api/myparty", async context =>
                            {
                                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCH", "users", "local", "parties.json");
                                if (File.Exists(path))
                                {
                                    context.Response.ContentType = "application/json";
                                    await context.Response.WriteAsync(await File.ReadAllTextAsync(path));
                                }
                                else
                                {
                                    context.Response.StatusCode = 404;
                                }
                            });
                            endpoints.MapGet("/api/usage", async context =>
                            {
                                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCH", "data-cache", "move-usage.json");
                                if (File.Exists(path))
                                {
                                    context.Response.ContentType = "application/json";
                                    await context.Response.WriteAsync(await File.ReadAllTextAsync(path));
                                }
                                else
                                {
                                    context.Response.StatusCode = 404;
                                }
                            });
                            endpoints.MapGet("/api/master", async context =>
                            {
                                var path = Path.Combine(Directory.GetCurrentDirectory(), "Data", "master.json");
                                if (!File.Exists(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "master.json");
                                if (!File.Exists(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Data", "master.json");
                                
                                if (File.Exists(path))
                                {
                                    context.Response.ContentType = "application/json";
                                    await context.Response.WriteAsync(await File.ReadAllTextAsync(path));
                                }
                                else
                                {
                                    context.Response.StatusCode = 404;
                                }
                            });
                            endpoints.MapGet("/api/sprites", async context =>
                            {
                                var path = Path.Combine(Directory.GetCurrentDirectory(), "sprites.json");
                                if (!File.Exists(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sprites.json");
                                if (!File.Exists(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "sprites.json");
                                
                                if (File.Exists(path))
                                {
                                    context.Response.ContentType = "application/json";
                                    await context.Response.WriteAsync(await File.ReadAllTextAsync(path));
                                }
                                else
                                {
                                    context.Response.StatusCode = 404;
                                }
                            });
                        });
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddTransient<MainWindow>();
                })
                .Build();

            await _host.StartAsync();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            
            if (ScreenCaptureService.TestVideoPath != null)
            {
                var ocrService = _host.Services.GetRequiredService<ScreenCaptureService>();
                ocrService.Start();
                mainWindow.WindowState = WindowState.Minimized;
            }

            mainWindow.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
            base.OnExit(e);
            Environment.Exit(0);
        }
    }
}
