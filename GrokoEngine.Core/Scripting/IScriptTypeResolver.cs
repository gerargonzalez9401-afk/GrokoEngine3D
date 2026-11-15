using System;

namespace GrokoEngine
{
    /// <summary>
    /// Contrato mínimo que el runtime necesita para restaurar componentes de script
    /// desde una escena. Mantiene Core independiente de Roslyn y del host de edición.
    /// </summary>
    public interface IScriptTypeResolver
    {
        Type? FindTypeByName(string typeName);
        Type? FindScriptableObjectType(string typeName);
    }
}
