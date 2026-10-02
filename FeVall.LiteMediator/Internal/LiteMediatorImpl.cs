using FeVall.LiteMediator.Channels;
using FeVall.LiteMediator.Messaging;
using System;
using System.Collections.Generic;
using System.Text;

namespace FeVall.LiteMediator.Internal
{
    internal sealed class LiteMediatorImpl(
    IServiceProvider serviceProvider,
    HandlerRegistry registry,
    IBackgroundEventQueue backgroundQueue) : ILiteMediator
    {
        public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (registry.Requests.TryGetValue(request.GetType(), out var handler))
                return ((RequestHandlerWrapper<TResponse>)handler).HandleAsync(request, serviceProvider, ct);

            throw new InvalidOperationException(
                $"FeVall.LiteMediator: no se encontró handler para la solicitud {request.GetType().Name}.");
        }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(@event);

            // Se resuelve por el tipo en runtime: funciona aunque la variable esté declarada como IEvent.
            return registry.Events.TryGetValue(@event.GetType(), out var wrapper)
                ? wrapper.HandleAsync(@event, serviceProvider, ct)
                : Task.CompletedTask;
        }

        public ValueTask EnqueueBackgroundAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
            => backgroundQueue.EnqueueAsync(@event, ct);
    }
}
