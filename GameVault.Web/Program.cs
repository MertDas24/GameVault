using GameVault.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddGameVault();

var app = builder.Build();

if (args.Contains("seed-demo", StringComparer.Ordinal))
{
    return await DevCommands.SeedDemoAsync(app);
}

app.UseGameVault();
await DevCommands.ApplyMigrationsIfEnabledAsync(app);
await app.RunAsync();
return 0;

public partial class Program;
