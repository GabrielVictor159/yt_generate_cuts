using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Builder;
using YT.Generate.Cuts.Application.VideoPublish;
using YT.Generate.Cuts.Infra.Data;
using YT.Generate.Cuts.Worker.VideoPublish;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationVideoPublish();
builder.Services.AddInfrastructureData(builder.Configuration);
builder.Services.AddHangfire(builder.Configuration, ["publish"], "publish_jobs");
builder.Services.AddPublishServices();

var app = builder.Build();

app.ApplyMigrations();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new AllowAllConnectionsFilter() }
});

app.UseHangfireDashboard();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.AddPublishServicesHangfire();
}

app.Run();

public class AllowAllConnectionsFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}