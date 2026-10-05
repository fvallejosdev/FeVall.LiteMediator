namespace Microsoft.AspNetCore.Builder;

using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using FeVall.LiteMediator.Endpoints;

public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Mapea todos los IEndpoint de los assemblies indicados.
    /// Se resuelven con ActivatorUtilities: si el endpoint está registrado en DI se usa esa instancia;
    /// si no, se crea inyectando las dependencias del constructor automáticamente.
    /// </summary>
    public static IEndpointRouteBuilder MapFeVallEndpoints(this IEndpointRouteBuilder app, params Assembly[] assemblies)
    {
        var endpointTypes = assemblies.Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IEndpoint).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Distinct();

        foreach (var endpointType in endpointTypes)
        {
            var endpoint = (IEndpoint)ActivatorUtilities.GetServiceOrCreateInstance(app.ServiceProvider, endpointType);
            endpoint.MapEndpoint(app);
        }

        return app;
    }
}