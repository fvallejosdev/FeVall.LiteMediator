using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;

namespace FeVall.LiteMediator.Internal
{
    /// <summary>Mapas inmutables construidos una sola vez al arrancar (request -> wrapper, evento -> wrapper).</summary>
    internal sealed class HandlerRegistry(
        FrozenDictionary<Type, RequestHandlerBase> requests,
        FrozenDictionary<Type, EventHandlerWrapper> events)
    {
        public FrozenDictionary<Type, RequestHandlerBase> Requests { get; } = requests;
        public FrozenDictionary<Type, EventHandlerWrapper> Events { get; } = events;
    }
}
