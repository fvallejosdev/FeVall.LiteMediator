using FeVall.LiteMediator.Messaging;
using System.Threading.Channels;
using FeVall.LiteMediator.Internal;

namespace FeVall.LiteMediator.Channels
{
    internal interface IBackgroundEventQueue
    {
        ValueTask EnqueueAsync(IEvent @event, CancellationToken ct = default);
        IAsyncEnumerable<IEvent> ReadAllAsync(CancellationToken ct);

        /// <summary>Eventos pendientes de procesar (para métricas y health checks).</summary>
        int PendingCount { get; }

        /// <summary>Cierra la cola: no admite más eventos, pero los pendientes aún pueden leerse (drenaje).</summary>
        void Complete();
    }

    internal sealed class BackgroundEventQueue : IBackgroundEventQueue
    {
        private readonly Channel<IEvent> _channel;

        public BackgroundEventQueue(LiteMediatorOptions options)
        {
            var singleReader = options.BackgroundWorkers == 1;

            _channel = options.BackgroundQueueCapacity > 0
                ? Channel.CreateBounded<IEvent>(new BoundedChannelOptions(options.BackgroundQueueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = singleReader
                })
                : Channel.CreateUnbounded<IEvent>(new UnboundedChannelOptions
                {
                    SingleReader = singleReader
                });

            // Gauge de profundidad de cola: sin listener de métricas es un no-op.
            LiteMediatorDiagnostics.RegisterQueueDepthGauge(() => _channel.Reader.Count);
        }

        public int PendingCount => _channel.Reader.Count;

        public ValueTask EnqueueAsync(IEvent @event, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(@event);
            return _channel.Writer.WriteAsync(@event, ct);
        }

        public IAsyncEnumerable<IEvent> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

        public void Complete() => _channel.Writer.TryComplete();
    }
}