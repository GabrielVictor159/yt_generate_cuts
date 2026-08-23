using YT.Generate.Cuts.App.WebAssembly.Components;
using YT.Generate.Cuts.App.WebAssembly.Services;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Interatividade via circuito SignalR (render mode InteractiveServer).
// Nao usamos InteractiveWebAssembly porque nao existe projeto cliente .Client
// no repositorio; sem ele o render mode WASM fica inerte e nenhum handler e ligado.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// API Service
builder.Services.AddHttpClient<ApiService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiUrl"] ?? "http://localhost:8386/");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
