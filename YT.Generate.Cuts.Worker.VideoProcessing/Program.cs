using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Builder;
using YT.Generate.Cuts.Application.Abstractions;
using YT.Generate.Cuts.Application.VideoProcessing;
using YT.Generate.Cuts.Infra.Data;
using YT.Generate.Cuts.Worker.VideoProcessing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOllama(builder.Configuration);
builder.Services.AddApplicationVideoProcessing();
builder.Services.AddInfrastructureData(builder.Configuration);
builder.Services.AddHangfire(builder.Configuration, ["processing"], "processing_jobs");
builder.Services.AddServices();


var app = builder.Build();

app.ApplyMigrations();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new AllowAllConnectionsFilter() }
});

app.UseHangfireDashboard();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.AddServicesHangfire();
}

app.Run();

public class AllowAllConnectionsFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}