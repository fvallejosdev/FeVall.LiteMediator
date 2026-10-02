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
        /// </summary>
        public abstract Task HandleAsync(
            IEvent @event,
            IServiceProvider serviceProvider,
            CancellationToken ct,
            Action<Exception, Type>? onHandlerError = null);
    }

    internal sealed class EventHandlerWrapperImpl<TEvent> : EventHandlerWrapper where TEvent : IEvent
    {
        public override async Task HandleAsync(
            IEvent @event,
            IServiceProvider serviceProvider,
            CancellationToken ct,
            Action<Exception, Type>? onHandlerError = null)
        {
            var typedEvent = (TEvent)@event;

            foreach (var handler in serviceProvider.GetServices<IEventHandler<TEvent>>())
            {
                if (onHandlerError is null)
                {
                    await handler.HandleAsync(typedEvent, ct);
                    continue;
                }

                try
                {
                    await handler.HandleAsync(typedEvent, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    onHandlerError(ex, handler.GetType());
                }
            }
        }
    }

}
