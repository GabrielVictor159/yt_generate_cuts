using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Hangfire;
using Hangfire.InMemory;

// Camadas da Aplicação
using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoExtraction.Commands.MonitoringChannel;
using YT.Generate.Cuts.Application.VideoExtraction.Commands.QuerysVideoExtraction;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.ProcessCuts;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.QuerysVideoProcessing;
using YT.Generate.Cuts.Application.VideoPublish.Commands;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Domain.Enums;

// Extensões e Infra
using YT.Generate.Cuts.Application.VideoExtraction;
using YT.Generate.Cuts.Application.VideoProcessing;
using YT.Generate.Cuts.Application.VideoPublish;
using YT.Generate.Cuts.Infra.Data.Context;
using YT.Generate.Cuts.Infra.Data.Abstractions.Interfaces.Repositories;
using YT.Generate.Cuts.Infra.Data.Repositories;

// Extensões de Workers
using YT.Generate.Cuts.Worker.VideoExtraction;
using YT.Generate.Cuts.Worker.VideoPublish;

namespace YT.Generate.Cuts.McpServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        // --- BLINDAGEM DE STDOUT ---
        var utf8NoBom = new UTF8Encoding(false);
        var standardOutput = Console.OpenStandardOutput();
        var mcpWriter = new StreamWriter(standardOutput, utf8NoBom) { AutoFlush = true };
        Console.SetOut(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

        // --- CONFIGURAÇÃO DO HOST ---
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging => {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.None);
            })
            .ConfigureServices((context, services) => {
                services.AddDbContext<CutContext>(options => {
                    options.UseSqlite("Data Source=mcp_local.db");
                    options.UseLoggerFactory(LoggerFactory.Create(b => b.AddFilter(_ => false)));
                });

                services.AddScoped<IUnitOfWork, UnitOfWork>();
                services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

                services.AddApplicationVideoExtraction();
                services.AddApplicationVideoProcessing();
                services.AddApplicationVideoPublish();

                services.AddServices(); 
                services.AddPublishServices();

                services.AddHangfire(config => config.UseInMemoryStorage());
                services.AddHangfireServer(options => {
                    options.Queues = new[] { "extraction", "publish", "default" };
                    options.WorkerCount = 10;
                });
            })
            .Build();

        // --- INICIALIZAÇÃO ---
        using (var scope = host.Services.CreateScope()) {
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<CutContext>().Database.EnsureCreated();
            sp.AddServicesHangfire();
            sp.AddPublishServicesHangfire();
        }

        await host.StartAsync();
        var dispatcher = host.Services.GetRequiredService<IAppDispatcher>();

        // --- LOOP DO MCP ---
        using var stdin = new StreamReader(Console.OpenStandardInput(), utf8NoBom);

        while (!stdin.EndOfStream)
        {
            var line = await stdin.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var request = JsonNode.Parse(line);
                var id = request?["id"]?.DeepClone(); 
                var method = request?["method"]?.GetValue<string>();

                if (method == "initialize") {
                    await SendResponse(mcpWriter, id, new {
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = "YT-Cuts-Manager", version = "1.2.0" }
                    });
                }
                else if (method == "tools/list") {
                    await SendResponse(mcpWriter, id, new {
                        tools = new object[] {
                            new { 
                                name = "add_channel", 
                                description = "Cadastra um novo canal do YouTube para monitoramento automático.", 
                                inputSchema = new { 
                                    type = "object", 
                                    properties = new { 
                                        url = new { type = "string", description = "URL completa do canal do YouTube (ex: https://youtube.com/@nome)" }, 
                                        name = new { type = "string", description = "Nome identificador amigável para o canal" } 
                                    }, 
                                    required = new[] { "url", "name" } 
                                } 
                            },
                            new { 
                                name = "trigger_extraction", 
                                description = "Força o download e extração de vídeos recentes de um canal monitorado.", 
                                inputSchema = new { 
                                    type = "object", 
                                    properties = new { 
                                        channelId = new { type = "number", description = "O ID numérico do canal salvo no banco" } 
                                    }, 
                                    required = new[] { "channelId" } 
                                } 
                            },
                            new { 
                                name = "list_videos", 
                                description = "Retorna a lista de vídeos baixados e seu status atual.", 
                                inputSchema = new { 
                                    type = "object", 
                                    properties = new { 
                                        channelId = new { type = "number", description = "Opcional: filtrar vídeos de um canal específico" } 
                                    } 
                                } 
                            },
                            new { 
                                name = "process_cut", 
                                description = "Inicia o corte físico do vídeo original baseado nos tempos interessantes gerados pelo Ollama.", 
                                inputSchema = new { 
                                    type = "object", 
                                    properties = new { 
                                        cutId = new { type = "number", description = "O ID numérico do registro de corte (Cut)" } 
                                    }, 
                                    required = new[] { "cutId" } 
                                } 
                            },
                            new { 
                                name = "publish_tiktok", 
                                description = "Envia um vídeo de corte processado diretamente para o TikTok.", 
                                inputSchema = new { 
                                    type = "object", 
                                    properties = new { 
                                        cutId = new { type = "number", description = "ID do corte que deve ser publicado" } 
                                    }, 
                                    required = new[] { "cutId" } 
                                } 
                            }
                        }
                    });
                }
                else if (method == "tools/call") {
                    var toolName = request?["params"]?["name"]?.GetValue<string>();
                    var toolArgs = request?["params"]?["arguments"];
                    string resultText = "";

                    using var scope = host.Services.CreateScope();
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                    switch (toolName)
                    {
                        case "add_channel":
                            var ch = new MonitoringChannel { Name = toolArgs?["name"]?.GetValue<string>() ?? "", Url = toolArgs?["url"]?.GetValue<string>() ?? "" };
                            await dispatcher.Send(new MonitoringChannelCommand(ch));
                            resultText = $"Canal {ch.Name} registrado. O monitoramento foi enfileirado.";
                            break;

                        case "trigger_extraction":
                            var teId = toolArgs?["channelId"]?.GetValue<long>() ?? 0;
                            var teChan = await uow.Repository<MonitoringChannel>().GetByIdAsync(teId);
                            if (teChan != null) {
                                // Força a execução imediata da extração
                                await dispatcher.Send(new MonitoringChannelCommand(teChan, false));
                                resultText = $"Extração iniciada para o canal: {teChan.Name}. Verifique os logs do Worker.";
                            } else resultText = "Canal não encontrado.";
                            break;

                        case "list_videos":
                            var cId = toolArgs?["channelId"]?.GetValue<long>();
                            var videos = cId.HasValue 
                                ? await dispatcher.Send(new GetAllVideo(v => v.ChannelId == cId.Value))
                                : await dispatcher.Send(new GetAllVideo(null));
                            resultText = JsonSerializer.Serialize(videos);
                            break;

                        case "process_cut":
                            var pId = toolArgs?["cutId"]?.GetValue<long>() ?? 0;
                            var pCut = await uow.Repository<Cut>().GetByIdAsync(pId);
                            if (pCut != null) {
                                var resp = await dispatcher.Send(new ProcessCutsCommand(pCut));
                                resultText = $"Corte finalizado. Caminho: {resp.CutPath} | Duração: {resp.Duration}";
                            } else resultText = "Corte não localizado.";
                            break;

                        case "publish_tiktok":
                            var pbId = toolArgs?["cutId"]?.GetValue<long>() ?? 0;
                            var pbCut = await uow.Repository<Cut>().GetByIdAsync(pbId);
                            if (pbCut != null) {
                                var resp = await dispatcher.Send(new PublishCutCommand(pbCut));
                                resultText = resp.Success ? $"Publicado com sucesso! Post ID: {resp.PlatformPostId}" : $"Erro na publicação: {resp.ErrorMessage}";
                            } else resultText = "Corte não encontrado.";
                            break;
                    }

                    await SendResponse(mcpWriter, id, new { content = new[] { new { type = "text", text = resultText } } });
                }
            }
            catch (Exception ex) {
                await SendError(mcpWriter, null, -32603, ex.Message);
            }
        }
        await host.StopAsync();
    }

    private static async Task SendResponse(StreamWriter writer, JsonNode? id, object result) {
        var response = new { jsonrpc = "2.0", id, result };
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static async Task SendError(StreamWriter writer, JsonNode? id, int code, string message) {
        var response = new { jsonrpc = "2.0", id, error = new { code, message } };
        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
    }
}