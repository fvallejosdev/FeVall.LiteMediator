using FeVall.LiteMediator.Messaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Si BackgroundMaxRetries > 0, cada handler se reintenta con backoff exponencial antes de darse por perdido.
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
        {
            LiteMediatorDiagnostics.BackgroundHandlerFailures.Add(1,
                new TagList { { "litemediator.handler", handlerType.FullName } });
            logger.LogError(ex, "FeVall.LiteMediator: el handler {Handler} falló procesando un evento en background.", handlerType.Name);
        };

        private readonly Action<Exception, Type, int, TimeSpan> _onHandlerRetry = (ex, handlerType, attempt, delay) =>
        {
            LiteMediatorDiagnostics.BackgroundHandlerRetries.Add(1,
                new TagList { { "litemediator.handler", handlerType.FullName } });
            logger.LogWarning(ex, "FeVall.LiteMediator: el handler {Handler} falló (intento {Attempt}); reintento en {Delay}.",
                handlerType.Name, attempt, delay);
        };

        private Task[] _workers = [];
        private volatile Exception? _workerFault;

        /// <summary>True si algún worker murió por una excepción no controlada (lo consulta el health check).</summary>
        internal bool HasFaulted => _workerFault is not null;
        internal Exception? WorkerFault => _workerFault;

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
            catch (Exception ex)
            {
                // Defensa en profundidad: ProcessAsync ya atrapa todo, pero si algo se escapa,
                // el worker muere y el health check lo reporta como Unhealthy.
                _workerFault = ex;
                logger.LogCritical(ex, "FeVall.LiteMediator: un worker de eventos en background murió; la cola ya no se drena al completo.");
            }
        }

        private async Task ProcessAsync(IEvent @event, CancellationToken ct)
        {
            var eventType = @event.GetType();
            var tags = new TagList { { "litemediator.event_type", eventType.FullName } };

            using var activity = LiteMediatorDiagnostics.Source.StartActivity("litemediator.background.process");
            activity?.SetTag("litemediator.event_type", eventType.FullName);

            if (!registry.Events.TryGetValue(eventType, out var wrapper))
            {
                logger.LogWarning("FeVall.LiteMediator: no hay handlers registrados para el evento {EventType}.", eventType.Name);
                return;
            }

            var start = Stopwatch.GetTimestamp();
            try
            {
                // Scope propio por evento: aislado de la petición HTTP que lo originó.
                await using var scope = scopeFactory.CreateAsyncScope();
                await wrapper.HandleAsync(@event, scope.ServiceProvider, ct, _onHandlerError,
                    options.BackgroundMaxRetries, options.BackgroundRetryDelay, _onHandlerRetry);

                LiteMediatorDiagnostics.BackgroundProcessed.Add(1, tags);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "FeVall.LiteMediator: error procesando el evento en background {EventType}.", eventType.Name);
            }
            finally
            {
                LiteMediatorDiagnostics.BackgroundEventDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, tags);
            }
        }

        public void Dispose() => _forceStop.Dispose();
    }
}