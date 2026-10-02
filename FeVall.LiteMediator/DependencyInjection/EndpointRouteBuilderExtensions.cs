namespace Microsoft.AspNetCore.Builder;

using System.Reflection;
using Microsoft.AspNetCore.Routing;
using FeVall.LiteMediator.Endpoints;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapFeVallEndpoints(this IEndpointRouteBuilder app, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var endpointTypes = assembly.GetTypes()
                .Where(t => typeof(IEndpoint).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var endpointType in endpointTypes)
            {
                var endpoint = (IEndpoint)Activator.CreateInstance(endpointType)!;
                endpoint.MapEndpoint(app);
            }
        }

        return app;
    }
}
