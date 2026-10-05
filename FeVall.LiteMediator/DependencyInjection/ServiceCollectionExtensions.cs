using FeVall.LiteMediator.Channels;
using FeVall.LiteMediator.Internal;
using FeVall.LiteMediator.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace FeVall.LiteMediator.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        private const string ScanWarning =
            "El escaneo de assemblies usa reflexión y MakeGenericType; no es compatible con trimming/Native AOT.";

        /// <summary>Registra el mediador con la configuración por defecto. Llamar UNA sola vez con todos los assemblies.</summary>
        [RequiresUnreferencedCode(ScanWarning)]
        [RequiresDynamicCode(ScanWarning)]
        public static IServiceCollection AddFeVallLiteMediator(this IServiceCollection services, params Assembly[] assemblies)
            => services.AddFeVallLiteMediator(configure: null, assemblies);

        /// <summary>Registra el mediador permitiendo configurar workers, cola, reintentos y validación.</summary>
        [RequiresUnreferencedCode(ScanWarning)]
        [RequiresDynamicCode(ScanWarning)]
        public static IServiceCollection AddFeVallLiteMediator(
            this IServiceCollection services,
            Action<LiteMediatorOptions>? configure,
            params Assembly[] assemblies)
        {
            ArgumentNullException.ThrowIfNull(services);

            if (assemblies.Length == 0)
                throw new ArgumentException("Debes indicar al menos un assembly a escanear.", nameof(assemblies));

            if (services.Any(d => d.ServiceType == typeof(HandlerRegistry)))
                throw new InvalidOperationException(
                    "AddFeVallLiteMediator ya fue llamado. Pásale todos los assemblies en una sola llamada.");

            var options = new LiteMediatorOptions();
            configure?.Invoke(options);
            options.Validate();

            var requestWrappers = new Dictionary<Type, RequestHandlerBase>();
            var requestOwners = new Dictionary<Type, Type>();
            var eventWrappers = new Dictionary<Type, EventHandlerWrapper>();

            // Los AddScoped se difieren: si alguna validación falla, el contenedor queda intacto.
            var registrations = new List<(Type ServiceType, Type ImplementationType)>();

            // Distinct() por tipo: si el mismo assembly llega por dos rutas (o dos assemblies exponen
            // el mismo tipo por type-forwarding), los handlers no se registran ni ejecutan dos veces.
            var scannedTypes = assemblies.Distinct().SelectMany(a => a.GetTypes()).Distinct().ToList();

            foreach (var type in scannedTypes)
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

                // Una clase puede implementar varios handlers: se recorren TODAS sus interfaces.
                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType)
                        continue;

                    var definition = iface.GetGenericTypeDefinition();

                    if (definition == typeof(IRequestHandler<,>))
                    {
                        var args = iface.GetGenericArguments();
                        var requestType = args[0];

                        if (requestOwners.TryGetValue(requestType, out var existing))
                            throw new InvalidOperationException(
                                $"FeVall.LiteMediator: la solicitud {requestType.Name} tiene más de un handler " +
                                $"({existing.Name} y {type.Name}). Solo se permite uno.");

                        requestOwners[requestType] = type;
                        requestWrappers[requestType] = (RequestHandlerBase)Activator.CreateInstance(
                            typeof(RequestHandlerWrapperImpl<,>).MakeGenericType(args))!;

                        registrations.Add((iface, type));
                    }
                    else if (definition == typeof(IEventHandler<>))
                    {
                        var eventType = iface.GetGenericArguments()[0];

                        if (!eventWrappers.ContainsKey(eventType))
                            eventWrappers[eventType] = (EventHandlerWrapper)Activator.CreateInstance(
                                typeof(EventHandlerWrapperImpl<>).MakeGenericType(eventType))!;

                        registrations.Add((iface, type));
                    }
                }
            }

            if (options.ValidateHandlersOnStartup)
                ValidateAllRequestsHaveHandler(scannedTypes, requestWrappers);

            foreach (var (serviceType, implementationType) in registrations)
                services.AddScoped(serviceType, implementationType);

            // Mapas inmutables: se construyen una vez y se leen sin sincronización.
            services.AddSingleton(options);
            services.AddSingleton(new HandlerRegistry(
                requestWrappers.ToFrozenDictionary(),
                eventWrappers.ToFrozenDictionary()));

            services.AddSingleton<IBackgroundEventQueue, BackgroundEventQueue>();

            // Registrado como tipo concreto para que el health check pueda consultar su estado;
            // el hosted service es el MISMO singleton.
            services.AddSingleton<BackgroundEventProcessor>();
            services.AddHostedService(sp => sp.GetRequiredService<BackgroundEventProcessor>());

            // Transient como MediatR: la impl es liviana y captura el IServiceProvider de quien la recibe.
            // Ojo: inyectarla en un singleton hará que los handlers scoped fallen al resolverse (restricción de DI, no del mediador).
            services.AddTransient<ILiteMediator, LiteMediatorImpl>();

            return services;
        }

        /// <summary>
        /// Fail-fast: toda solicitud concreta encontrada en los assemblies escaneados debe tener handler.
        /// Los handlers abstractos y las definiciones genéricas abiertas se ignoran.
        /// </summary>
        private static void ValidateAllRequestsHaveHandler(
            List<Type> scannedTypes, Dictionary<Type, RequestHandlerBase> requestWrappers)
        {
            var orphans = scannedTypes
                .Where(t => !t.IsAbstract && !t.IsInterface && !t.IsGenericTypeDefinition)
                .Where(t => t.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
                .Where(t => !requestWrappers.ContainsKey(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();

            if (orphans.Count > 0)
            {
                throw new InvalidOperationException(
                    "FeVall.LiteMediator: hay solicitudes sin handler: " +
                    string.Join(", ", orphans.Select(t => t.Name)) +
                    ". Registra un handler para cada una, incluye el assembly que la contiene en la llamada " +
                    "o desactiva LiteMediatorOptions.ValidateHandlersOnStartup.");
            }
        }

        /// <summary>
        /// Registra un pipeline genérico abierto. El orden de registro define el anidamiento:
        /// el primero registrado es el más externo.
        /// </summary>
        public static IServiceCollection AddLiteMediatorPipeline(this IServiceCollection services, Type pipelineBehaviorType)
        {
            ArgumentNullException.ThrowIfNull(pipelineBehaviorType);

            if (!pipelineBehaviorType.IsGenericTypeDefinition || pipelineBehaviorType.IsAbstract)
                throw new ArgumentException(
                    $"El pipeline debe ser un tipo genérico abierto (ej. typeof(MiBehavior<,>)). Recibido: {pipelineBehaviorType.Name}.",
                    nameof(pipelineBehaviorType));

            if (!pipelineBehaviorType.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
                throw new ArgumentException(
                    $"El tipo {pipelineBehaviorType.Name} no implementa IPipelineBehavior<,>.",
                    nameof(pipelineBehaviorType));

            services.AddScoped(typeof(IPipelineBehavior<,>), pipelineBehaviorType);
            return services;
        }
    }
}