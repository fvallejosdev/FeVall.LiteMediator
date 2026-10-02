using FeVall.LiteMediator.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace FeVall.LiteMediator.Internal
{
    internal abstract class EventHandlerWrapper
    {
        /// <summary>
        /// Ejecuta los handlers del evento de forma SECUENCIAL (seguro con DbContext, que no es thread-safe).
        /// Si onHandlerError es null, la primera excepción se propaga (modo PublishAsync).
        /// Si se indica, cada handler queda aislado: uno que falla no impide que se ejecuten los demás (modo background).
        /// maxRetries/retryDelay/onRetry solo aplican al modo aislado: el reintento es POR HANDLER,
        /// de modo que los que ya tuvieron éxito no se vuelven a ejecutar.
        /// </summary>
        public abstract Task HandleAsync(
            IEvent @event,
            IServiceProvider serviceProvider,
            CancellationToken ct,
            Action<Exception, Type>? onHandlerError = null,
            int maxRetries = 0,
            TimeSpan retryDelay = default,
            Action<Exception, Type, int, TimeSpan>? onRetry = null);
    }

    internal sealed class EventHandlerWrapperImpl<TEvent> : EventHandlerWrapper where TEvent : IEvent
    {
        public override async Task HandleAsync(
            IEvent @event,
            IServiceProvider serviceProvider,
            CancellationToken ct,
            Action<Exception, Type>? onHandlerError = null,
            int maxRetries = 0,
            TimeSpan retryDelay = default,
            Action<Exception, Type, int, TimeSpan>? onRetry = null)
        {
            var typedEvent = (TEvent)@event;

            foreach (var handler in serviceProvider.GetServices<IEventHandler<TEvent>>())
            {
                if (onHandlerError is null)
                {
                    await handler.HandleAsync(typedEvent, ct);
                    continue;
                }

                await ExecuteIsolatedAsync(handler, typedEvent, ct, onHandlerError, maxRetries, retryDelay, onRetry);
            }
        }

        /// <summary>
        /// Ejecuta un handler con aislamiento y reintentos (backoff exponencial).
        /// La cancelación nunca se reintenta: se propaga para que el apagado forzado responda al instante.
        /// </summary>
        private static async Task ExecuteIsolatedAsync(
            IEventHandler<TEvent> handler,
            TEvent typedEvent,
            CancellationToken ct,
            Action<Exception, Type> onHandlerError,
            int maxRetries,
            TimeSpan retryDelay,
            Action<Exception, Type, int, TimeSpan>? onRetry)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await handler.HandleAsync(typedEvent, ct);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * Math.Pow(2, attempt));
                    onRetry?.Invoke(ex, handler.GetType(), attempt + 1, delay);
                    await Task.Delay(delay, ct);
                }
                catch (Exception ex)
                {
                    // Reintentos agotados (o desactivados): handler perdido, se continúa con el resto.
                    onHandlerError(ex, handler.GetType());
                    return;
                }
            }
        }
    }
}