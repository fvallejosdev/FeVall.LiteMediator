using FeVall.LiteMediator.Messaging;
using System.Threading.Channels;

namespace FeVall.LiteMediator.Channels
{
    internal interface IBackgroundEventQueue
    {
        ValueTask EnqueueAsync(IEvent @event, CancellationToken ct = default);
        IAsyncEnumerable<IEvent> ReadAllAsync(CancellationToken ct);

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
        }

        public ValueTask EnqueueAsync(IEvent @event, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(@event);
            return _channel.Writer.WriteAsync(@event, ct);
        }

        public IAsyncEnumerable<IEvent> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

        public void Complete() => _channel.Writer.TryComplete();
    }

}
