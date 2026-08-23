using YT.Generate.Cuts.Application.Abstractions.Interfaces.Commands;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.InterestingTimes;
using YT.Generate.Cuts.Application.VideoProcessing.Commands.QuerysVideoProcessing;
using YT.Generate.Cuts.Domain.Entities;
using YT.Generate.Cuts.Domain.Enums;

namespace YT.Generate.Cuts.Worker.VideoProcessing.Tests.Fakes;

/// <summary>
/// Dispatcher de teste: guarda os vídeos numa lista em memória e responde às
/// consultas <b>avaliando o predicado real</b> do <see cref="GetAllVideo"/>.
/// </summary>
/// <remarks>
/// Avaliar o predicado em vez de adivinhar qual consulta chegou é o que dá valor
/// ao teste: a releitura de status que o serviço faz dentro do lock passa a
/// funcionar como funcionaria no banco — se outra execução já mudou o vídeo para
/// <see cref="VideoStatusEnum.Process"/>, a consulta volta vazia de verdade.
/// <para>
/// A lista é compartilhada entre todas as instâncias criadas por um mesmo
/// <see cref="Store"/>, simulando duas execuções (dois escopos de DI, dois
/// processos) olhando o mesmo banco.
/// </para>
/// </remarks>
public sealed class RecordingDispatcher : IAppDispatcher
{
    public sealed class Store
    {
        private readonly object _gate = new();

        public List<Video> Videos { get; } = new();

        /// <summary>Quantas execuções de InterestingTimes estão ativas agora.</summary>
        public int Active;

        /// <summary>Maior número de execuções simultâneas observado.</summary>
        public int MaxActive;

        /// <summary>Ids processados, na ordem — repetição aqui é processamento duplicado.</summary>
        public List<long> Processed { get; } = new();

        /// <summary>Duração simulada de uma inferência.</summary>
        public TimeSpan Work { get; init; } = TimeSpan.FromMilliseconds(120);

        public void Enter()
        {
            var now = Interlocked.Increment(ref Active);
            lock (_gate) MaxActive = Math.Max(MaxActive, now);
        }

        public void Leave() => Interlocked.Decrement(ref Active);

        public void MarkProcessed(long id)
        {
            lock (_gate) Processed.Add(id);
        }

        public List<Video> Snapshot()
        {
            lock (_gate) return Videos.ToList();
        }
    }

    private readonly Store _store;

    public RecordingDispatcher(Store store) => _store = store;

    public Task Send(ICommand command, CancellationToken ct = default) => Task.CompletedTask;

    public async Task<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default)
    {
        switch (command)
        {
            case GetAllVideo query:
            {
                var all = _store.Snapshot();

                var result = query.expression is null
                    ? all
                    : all.Where(query.expression.Compile()).ToList();

                return (TResponse)(object)result;
            }

            case InterestingTimesCommand interesting:
            {
                _store.Enter();
                try
                {
                    await Task.Delay(_store.Work, ct);

                    // Mesmo efeito do handler real: o vídeo avança de etapa.
                    interesting.Video.Status = VideoStatusEnum.Process;
                    _store.MarkProcessed(interesting.Video.Id);
                }
                finally
                {
                    _store.Leave();
                }

                return (TResponse)(object)new InterestingTimesCommandResponse(new List<Cut>());
            }
        }

        throw new NotSupportedException($"Comando não previsto no teste: {command.GetType().Name}");
    }
}
