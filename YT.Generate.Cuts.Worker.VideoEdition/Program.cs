using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Builder;
using YT.Generate.Cuts.Application.VideoEdition;
using YT.Generate.Cuts.Infra.Data;
using YT.Generate.Cuts.Worker.VideoEdition;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationVideoEdition();
builder.Services.AddInfrastructureData(builder.Configuration);
builder.Services.AddHangfire(builder.Configuration, ["edition"], "edition_jobs");
builder.Services.AddEditionServices();

var app = builder.Build();

app.ApplyMigrations();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new AllowAllConnectionsFilter() }
});

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.AddEditionServicesHangfire();
}

app.Run();

public class AllowAllConnectionsFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
