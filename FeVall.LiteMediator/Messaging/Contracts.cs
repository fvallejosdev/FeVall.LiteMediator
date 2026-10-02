using System;
using System.Collections.Generic;
using System.Text;

namespace FeVall.LiteMediator.Messaging
{
    // --- BASES ---
    public interface IRequest<TResponse> { }

    public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
    }

    // --- CQRS ---
    public interface ICommand<TResponse> : IRequest<TResponse> { }
    public interface ICommand : IRequest<Unit> { }

    public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    { }

    public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Unit>
        where TCommand : ICommand
    { }

    public interface IQuery<TResponse> : IRequest<TResponse> { }
    public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    { }

    // --- EVENTOS (PUB/SUB) ---
    public interface IEvent { }

    public interface IEventHandler<in TEvent> where TEvent : IEvent
    {
        Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
    }

    // --- PIPELINES (MIDDLEWARES) ---
    public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

    public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
    }
}
