using Discord;
using Discord.WebSocket;
using NeuroSync.Config;
using NeuroSync.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, lc) => lc
        .MinimumLevel.Information()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File("logs/neurosync-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14))
    .ConfigureServices((ctx, services) =>
    {
        var config = new NeuroConfig();
        ctx.Configuration.GetSection("Discord").Bind(config);

        if (string.IsNullOrWhiteSpace(config.Token) || config.Token == "YOUR_BOT_TOKEN_HERE")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ERROR: Set your bot token in appsettings.json (Discord:Token)");
            Console.ResetColor();
            Environment.Exit(1);
        }

        services.AddSingleton(config);

        var socketConfig = new DiscordSocketConfig
        {
            GatewayIntents =
                GatewayIntents.Guilds |
                GatewayIntents.GuildMembers |
                GatewayIntents.GuildPresences,
            AlwaysDownloadUsers = true,
            LogLevel = LogSeverity.Info,
            MessageCacheSize = 0,
        };

        var client = new DiscordSocketClient(socketConfig);
        services.AddSingleton(client);
        services.AddSingleton<SuspicionAnalyzer>();
        services.AddSingleton<SlashCommandHandler>();
        services.AddHostedService<ProtectionService>();
        services.AddHostedService<BotStartupService>();
    })
    .Build();

await host.RunAsync();

/// <summary>
/// Connects the Discord client and registers slash commands on application startup.
/// </summary>
file sealed class BotStartupService(
    DiscordSocketClient client,
    SlashCommandHandler commands,
    NeuroConfig config,
    ILogger<BotStartupService> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        client.Log += msg =>
        {
            var level = msg.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Debug => LogLevel.Debug,
                _ => LogLevel.Trace
            };
            log.Log(level, msg.Exception, "[Discord.Net] {Message}", msg.Message);
            return Task.CompletedTask;
        };

        client.Ready += () =>
        {
            log.LogInformation("Bot is ready — connected to {Count} guild(s)", client.Guilds.Count);
            foreach (var g in client.Guilds)
                log.LogInformation("  Guild: {Name} ({Id}) — {Members} members", g.Name, g.Id, g.MemberCount);
            return Task.CompletedTask;
        };

        await commands.RegisterAsync();
        await client.LoginAsync(TokenType.Bot, config.Token);
        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await client.StopAsync();
    }
}
