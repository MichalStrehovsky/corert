// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices
{
    class ForceLazyDictionaryAttribute : Attribute { }
}

class Gen<T> { }

internal class Program
{
    [ForceLazyDictionary]
    public static Type Fhtagn<T>(int cthulhu)
    {
        Console.WriteLine(typeof(T));
        if (cthulhu > 0)
            return Fhtagn<Gen<T>>(cthulhu - 1);
        return typeof(T);
    }

    private static void Main(string[] args)
    {
        Console.WriteLine(Fhtagn<object>(3));
    }
}
