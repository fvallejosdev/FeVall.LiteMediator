using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FeVall.LiteMediator.Internal
{
    /// <summary>
    /// Métricas y trazas del mediador. Nombre del Meter y del ActivitySource: "FeVall.LiteMediator".
    /// Sin listeners (OpenTelemetry, Prometheus, dotnet-counters...) todo es prácticamente un no-op.
    /// </summary>
    internal static class LiteMediatorDiagnostics
    {
        public const string MeterName = "FeVall.LiteMediator";
        public const string ActivitySourceName = "FeVall.LiteMediator";

        public static readonly ActivitySource Source = new(ActivitySourceName);

        private static readonly Meter Meter = new(MeterName);

        // --- SendAsync ---
        public static readonly Counter<long> RequestsSent =
            Meter.CreateCounter<long>("litemediator.requests.sent",
                description: "Commands/Queries enviados vía SendAsync.");

        public static readonly Counter<long> RequestsFailed =
            Meter.CreateCounter<long>("litemediator.requests.failed",
                description: "Solicitudes que terminaron en excepción.");

        public static readonly Histogram<double> RequestDuration =
            Meter.CreateHistogram<double>("litemediator.requests.duration", unit: "ms",
                description: "Duración de SendAsync (incluye pipelines).");

        // --- PublishAsync ---
        public static readonly Counter<long> EventsPublished =
            Meter.CreateCounter<long>("litemediator.events.published",
                description: "Eventos publicados de forma síncrona vía PublishAsync.");

        public static readonly Histogram<double> PublishDuration =
            Meter.CreateHistogram<double>("litemediator.events.duration", unit: "ms",
                description: "Duración de PublishAsync (todos los handlers, secuencial).");

        // --- Background ---
        public static readonly Counter<long> BackgroundEnqueued =
            Meter.CreateCounter<long>("litemediator.background.enqueued",
                description: "Eventos encolados vía EnqueueBackgroundAsync.");

        public static readonly Counter<long> BackgroundProcessed =
            Meter.CreateCounter<long>("litemediator.background.processed",
                description: "Eventos de background procesados.");

        public static readonly Counter<long> BackgroundHandlerRetries =
            Meter.CreateCounter<long>("litemediator.background.handler.retries",
                description: "Reintentos de handlers en background.");

        public static readonly Counter<long> BackgroundHandlerFailures =
            Meter.CreateCounter<long>("litemediator.background.handler.failures",
                description: "Handlers de background dados por perdidos (reintentos agotados o desactivados).");

        public static readonly Histogram<double> BackgroundEventDuration =
            Meter.CreateHistogram<double>("litemediator.background.event.duration", unit: "ms",
                description: "Duración total de procesar un evento en background (incluye reintentos).");

        /// <summary>Gauge de eventos pendientes; lo registra BackgroundEventQueue al construirse.</summary>
        public static void RegisterQueueDepthGauge(Func<int> observe)
            => Meter.CreateObservableGauge("litemediator.background.queue.depth", observe,
                description: "Eventos pendientes en la cola en background.");
    }
}