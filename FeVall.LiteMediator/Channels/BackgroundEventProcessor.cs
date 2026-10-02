using FeVall.LiteMediator.Messaging;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FeVall.LiteMediator.Internal;

namespace FeVall.LiteMediator.Channels
{
    /// <summary>
    /// Consume la cola de eventos en background con N workers.
    /// Al apagar el host, cierra la cola y drena los eventos pendientes hasta agotar el ShutdownTimeout del host;
    /// pasado ese tiempo cancela los handlers en curso y descarta lo que quede.
    /// </summary>
    internal sealed class BackgroundEventProcessor(
        IBackgroundEventQueue queue,
        HandlerRegistry registry,
        IServiceScopeFactory scopeFactory,
        LiteMediatorOptions options,
        ILogger<BackgroundEventProcessor> logger) : IHostedService, IDisposable
    {
        private readonly CancellationTokenSource _forceStop = new();
        private readonly Action<Exception, Type> _onHandlerError = (ex, handlerType) =>
            logger.LogError(ex, "FeVall.LiteMediator: el handler {Handler} falló procesando un evento en background.", handlerType.Name);

        private Task[] _workers = [];

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _workers = Enumerable.Range(0, options.BackgroundWorkers)
                .Select(_ => Task.Run(() => RunWorkerAsync(_forceStop.Token), CancellationToken.None))
                .ToArray();

            logger.LogInformation("FeVall.LiteMediator: {Count} worker(s) de eventos en background iniciados.", _workers.Length);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // 1. No se aceptan más eventos; los workers terminan cuando la cola queda vacía.
            queue.Complete();

            // 2. Si el host agota su tiempo de apagado, se fuerza la cancelación de los handlers en curso.
            using var registration = cancellationToken.Register(
                static state => ((CancellationTokenSource)state!).Cancel(), _forceStop);

            await Task.WhenAll(_workers);

            if (_forceStop.IsCancellationRequested)
                logger.LogWarning("FeVall.LiteMediator: tiempo de apagado agotado; eventos pendientes descartados.");
        }

        private async Task RunWorkerAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var @event in queue.ReadAllAsync(ct))
                    await ProcessAsync(@event, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Apagado forzado: salida silenciosa.
            }
        }

        private async Task ProcessAsync(IEvent @event, CancellationToken ct)
        {
            var eventType = @event.GetType();

            if (!registry.Events.TryGetValue(eventType, out var wrapper))
            {
                logger.LogWarning("FeVall.LiteMediator: no hay handlers registrados para el evento {EventType}.", eventType.Name);
                return;
            }

            try
            {
                // Scope propio por evento: aislado de la petición HTTP que lo originó.
                await using var scope = scopeFactory.CreateAsyncScope();
                await wrapper.HandleAsync(@event, scope.ServiceProvider, ct, _onHandlerError);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FeVall.LiteMediator: error procesando el evento en background {EventType}.", eventType.Name);
            }
        }

        public void Dispose() => _forceStop.Dispose();
    }
}
