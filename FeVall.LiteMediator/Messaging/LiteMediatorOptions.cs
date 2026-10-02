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

        internal void Validate()
        {
            if (BackgroundWorkers < 1)
                throw new ArgumentOutOfRangeException(nameof(BackgroundWorkers), "Debe ser al menos 1.");

            if (BackgroundQueueCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(BackgroundQueueCapacity), "No puede ser negativa (0 = ilimitada).");
        }
    }

}
