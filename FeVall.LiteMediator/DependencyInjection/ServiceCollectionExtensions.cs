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

        /// <summary>Registra el mediador permitiendo configurar workers y capacidad de la cola en background.</summary>
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

            foreach (var type in assemblies.Distinct().SelectMany(a => a.GetTypes()))
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

                        services.AddScoped(iface, type);
                    }
                    else if (definition == typeof(IEventHandler<>))
                    {
                        var eventType = iface.GetGenericArguments()[0];

                        if (!eventWrappers.ContainsKey(eventType))
                            eventWrappers[eventType] = (EventHandlerWrapper)Activator.CreateInstance(
                                typeof(EventHandlerWrapperImpl<>).MakeGenericType(eventType))!;

                        services.AddScoped(iface, type);
                    }
                }
            }

            // Mapas inmutables: se construyen una vez y se leen sin sincronización.
            services.AddSingleton(options);
            services.AddSingleton(new HandlerRegistry(
                requestWrappers.ToFrozenDictionary(),
                eventWrappers.ToFrozenDictionary()));

            services.AddSingleton<IBackgroundEventQueue, BackgroundEventQueue>();
            services.AddHostedService<BackgroundEventProcessor>();

            services.AddScoped<ILiteMediator, LiteMediatorImpl>();

            return services;
        }

        /// <summary>
        /// Registra un pipeline genérico abierto. El orden de registro define el anidamiento:
        /// el primero registrado es el más externo.
        /// </summary>
        public static IServiceCollection AddLiteMediatorPipeline(this IServiceCollection services, Type pipelineBehaviorType)
        {
            services.AddScoped(typeof(IPipelineBehavior<,>), pipelineBehaviorType);
            return services;
        }
    }

}
