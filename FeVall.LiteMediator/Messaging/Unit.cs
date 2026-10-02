using System;
using System.Collections.Generic;
using System.Text;

namespace FeVall.LiteMediator.Messaging
{
    /// <summary>
    /// Representa un resultado vacío para Commands que no devuelven datos (equivalente a void).
    /// </summary>
    public readonly struct Unit
    {
        public static readonly Unit Value = new();
    }
}
