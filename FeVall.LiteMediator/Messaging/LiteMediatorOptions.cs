using System;
using System.Collections.Generic;
using System.Text;

namespace FeVall.LiteMediator.Messaging
{
    public sealed class LiteMediatorOptions
    {
        /// <summary>
        /// Número de workers que consumen la cola de eventos en background. Por defecto 1 (orden FIFO estricto).
        /// Con más de 1, los eventos se procesan en paralelo y el orden ya no está garantizado.
        /// </summary>
        public int BackgroundWorkers { get; set; } = 1;

        /// <summary>
        /// Capacidad máxima de la cola en background. 0 = ilimitada.
        /// Si es mayor que 0 y la cola se llena, EnqueueBackgroundAsync espera hasta que haya espacio (backpressure).
        /// </summary>
        public int BackgroundQueueCapacity { get; set; } = 0;

        /// <summary>
        /// Reintentos por handler en background ante excepciones. Por defecto 0 (desactivado:
        /// un handler que falla se loguea y se continúa con el siguiente).
        /// El reintento es POR HANDLER, no por evento: los que ya tuvieron éxito no se repiten.
        /// Agotados los reintentos, el handler se da por perdido y se continúa con el resto.
        /// </summary>
        public int BackgroundMaxRetries { get; set; } = 0;

        /// <summary>
        /// Espera base entre reintentos en background. Se aplica backoff exponencial:
        /// delay, 2×delay, 4×delay, …
        /// </summary>
        public TimeSpan BackgroundRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

        internal void Validate()
        {
            if (BackgroundWorkers < 1)
                throw new ArgumentOutOfRangeException(nameof(BackgroundWorkers), "Debe ser al menos 1.");

            if (BackgroundQueueCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(BackgroundQueueCapacity), "No puede ser negativa (0 = ilimitada).");

            if (BackgroundMaxRetries < 0)
                throw new ArgumentOutOfRangeException(nameof(BackgroundMaxRetries), "No puede ser negativo (0 = desactivado).");

            if (BackgroundMaxRetries > 0 && BackgroundRetryDelay <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(BackgroundRetryDelay), "Debe ser mayor que cero si hay reintentos activados.");
        }
    }
}