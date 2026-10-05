using FeVall.LiteMediator.Channels;
using FeVall.LiteMediator.Messaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace FeVall.LiteMediator.Internal
{
    internal sealed class LiteMediatorImpl(
        IServiceProvider serviceProvider,
        HandlerRegistry registry,
        IBackgroundEventQueue backgroundQueue,
        ILogger<LiteMediatorImpl> logger) : ILiteMediator
    {
        public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var requestType = request.GetType();
            var tags = new TagList { { "litemediator.request_type", requestType.FullName } };

            using var activity = LiteMediatorDiagnostics.Source.StartActivity("litemediator.send");
            activity?.SetTag("litemediator.request_type", requestType.FullName);
            LiteMediatorDiagnostics.RequestsSent.Add(1, tags);

            var start = Stopwatch.GetTimestamp();
            try
            {
                if (registry.Requests.TryGetValue(requestType, out var handler))
                    return await ((RequestHandlerWrapper<TResponse>)handler).HandleAsync(request, serviceProvider, ct);

                throw new InvalidOperationException(
                    $"FeVall.LiteMediator: no se encontró handler para la solicitud {requestType.Name}.");
            }
            catch (Exception ex)
            {
                LiteMediatorDiagnostics.RequestsFailed.Add(1, tags);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
            finally
            {
                LiteMediatorDiagnostics.RequestDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, tags);
            }
        }

        public Task SendAsync(ICommand command, CancellationToken ct = default)
            => SendAsync<Unit>(command, ct);

        public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(@event);

            var eventType = @event.GetType();
            var tags = new TagList { { "litemediator.event_type", eventType.FullName } };

            using var activity = LiteMediatorDiagnostics.Source.StartActivity("litemediator.publish");
            activity?.SetTag("litemediator.event_type", eventType.FullName);
            LiteMediatorDiagnostics.EventsPublished.Add(1, tags);

            // Se resuelve por el tipo en runtime: funciona aunque la variable esté declarada como IEvent.
            if (!registry.Events.TryGetValue(eventType, out var wrapper))
            {
                // En pub/sub, 0 suscriptores es legítimo; se deja rastro en Debug para depurar "¿por qué no pasó nada?".
                logger.LogDebug("FeVall.LiteMediator: PublishAsync de {EventType} sin handlers registrados.", eventType.Name);
                return;
            }

            var start = Stopwatch.GetTimestamp();
            try
            {
                await wrapper.HandleAsync(@event, serviceProvider, ct);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
            finally
            {
                LiteMediatorDiagnostics.PublishDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, tags);
            }
        }

        public ValueTask EnqueueBackgroundAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(@event);

            var eventType = @event.GetType();

            // Fail-fast: encolar un evento sin handlers no tendría efecto; mejor un error aquí que un warning a destiempo.
            if (!registry.Events.ContainsKey(eventType))
                throw new InvalidOperationException(
                    $"FeVall.LiteMediator: el evento {eventType.Name} no tiene handlers registrados; encolarlo no tendría efecto.");

            LiteMediatorDiagnostics.BackgroundEnqueued.Add(1,
                new TagList { { "litemediator.event_type", eventType.FullName } });
            return backgroundQueue.EnqueueAsync(@event, ct);
        }
    }
}