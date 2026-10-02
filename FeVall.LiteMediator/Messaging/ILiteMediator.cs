using System;
using System.Collections.Generic;
using System.Text;

namespace FeVall.LiteMediator.Messaging
{
    public interface ILiteMediator
    {
        /// <summary>
        /// Envía un Command o Query y espera su resultado (Relación 1 a 1).
        /// </summary>
        Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);

        /// <summary>
        /// Publica un evento de dominio ejecutando los handlers en la misma petición (Relación 1 a N síncrona).
        /// </summary>
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent;

        /// <summary>
        /// Encola un evento en memoria vía Channels para ser procesado en segundo plano sin bloquear el hilo actual (Fire-and-Forget).
        /// </summary>
        ValueTask EnqueueBackgroundAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent;
    }
}
