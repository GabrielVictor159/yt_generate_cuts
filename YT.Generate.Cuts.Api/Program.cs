using YT.Generate.Cuts.Application.VideoExtraction;
using YT.Generate.Cuts.Application.VideoEdition;
using YT.Generate.Cuts.Application.VideoProcessing;
using YT.Generate.Cuts.Application.VideoPublish;
using YT.Generate.Cuts.Infra.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    // As entidades tem navegacao nos dois sentidos (Cut.Video <-> Video.Cuts).
    // Sem isto, qualquer endpoint que faca Include devolve HTTP 500 no meio da
    // serializacao: "A possible object cycle was detected". Media: /api/Cuts/{id}
    // e /api/Videos/{id} falhavam assim. IgnoreCycles corta a repeticao escrevendo
    // null no ponto do ciclo, em vez de inventar $id/$ref como o Preserve faria —
    // que mudaria o formato do JSON que o front-end ja consome.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);

// --- CONFIGURAÇÃO SWAGGER ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(); 
// ----------------------------


// Application layers
builder.Services.AddApplicationVideoExtraction();
builder.Services.AddOllama(builder.Configuration);
builder.Services.AddApplicationVideoProcessing();
builder.Services.AddApplicationVideoEdition();
builder.Services.AddApplicationVideoPublish();

// Infrastructure
builder.Services.AddInfrastructureData(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // --- MIDDLEWARE SWAGGER ---
    app.UseSwagger();
    app.UseSwaggerUI(); // Por padrão, fica em /swagger
    // --------------------------
}

app.UseCors();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();