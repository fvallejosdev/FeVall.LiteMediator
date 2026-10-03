using FeVall.LiteMediator.Channels;
using FeVall.LiteMediator.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FeVall.LiteMediator.DependencyInjection
{
    /// <summary>
    /// Health check del pipeline de eventos en background ("litemediator"):
    /// - Unhealthy: algún worker murió (la cola ya no se drena por completo).
    /// - Degraded: eventos pendientes por encima de HealthCheckQueueThreshold.
    /// </summary>
    internal sealed class LiteMediatorHealthCheck(
        BackgroundEventProcessor processor,
        IBackgroundEventQueue queue,
        LiteMediatorOptions options) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (processor.HasFaulted)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Un worker de eventos en background murió; la cola ya no se está drenando al completo.",
                    processor.WorkerFault));
            }

            var pending = queue.PendingCount;

            if (options.HealthCheckQueueThreshold > 0 && pending > options.HealthCheckQueueThreshold)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Cola de eventos en background saturada: {pending} pendientes (umbral: {options.HealthCheckQueueThreshold})."));
            }

            return Task.FromResult(HealthCheckResult.Healthy($"Eventos pendientes en background: {pending}."));
        }
    }

    public static class LiteMediatorHealthCheckExtensions
    {
        /// <summary>
        /// Añade el health check "litemediator". Requiere haber llamado antes a AddFeVallLiteMediator.
        /// </summary>
        public static IHealthChecksBuilder AddLiteMediatorHealthCheck(this IHealthChecksBuilder builder)
            => builder.AddCheck<LiteMediatorHealthCheck>("litemediator");
    }
}