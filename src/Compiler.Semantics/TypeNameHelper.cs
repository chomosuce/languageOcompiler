using System;

namespace Compiler.Semantics;

internal static class TypeNameHelper
{
    public static bool IsArrayType(string typeName) => TryExtractGenericArgument(typeName, "Array", out _);

    public static bool TryGetArrayElementType(string typeName, out string elementType) =>
        TryExtractGenericArgument(typeName, "Array", out elementType);

    private static bool TryExtractGenericArgument(string typeName, string containerName, out string elementType)
    {
        elementType = string.Empty;

        if (!typeName.StartsWith(containerName + "[", StringComparison.Ordinal) ||
            !typeName.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }

        var start = containerName.Length + 1;
        var length = typeName.Length - start - 1;
        if (length <= 0)
        {
            return false;
        }

        elementType = typeName.Substring(start, length);
        return true;
    }
}
