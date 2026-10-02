using FeVall.LiteMediator.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace FeVall.LiteMediator.Internal
{
    /// <summary>Marcador no genérico para poder guardar todos los wrappers en un mismo diccionario.</summary>
    internal abstract class RequestHandlerBase { }

    /// <summary>Wrapper tipado por respuesta: evita boxing de TResponse (Guid, Unit, etc.).</summary>
    internal abstract class RequestHandlerWrapper<TResponse> : RequestHandlerBase
    {
        public abstract Task<TResponse> HandleAsync(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken ct);
    }

    internal sealed class RequestHandlerWrapperImpl<TRequest, TResponse> : RequestHandlerWrapper<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> HandleAsync(IRequest<TResponse> request, IServiceProvider serviceProvider, CancellationToken ct)
        {
            var typedRequest = (TRequest)request;
            var handler = serviceProvider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

            var resolved = serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
            var behaviors = resolved as IPipelineBehavior<TRequest, TResponse>[] ?? resolved.ToArray();

            // Camino rápido: sin pipelines no se crean closures ni delegados.
            if (behaviors.Length == 0)
                return handler.HandleAsync(typedRequest, ct);

            RequestHandlerDelegate<TResponse> next = () => handler.HandleAsync(typedRequest, ct);

            // Se envuelve de atrás hacia adelante: el primer behavior registrado queda como el más externo.
            for (var i = behaviors.Length - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var current = next;
                next = () => behavior.HandleAsync(typedRequest, current, ct);
            }

            return next();
        }
    }

}
