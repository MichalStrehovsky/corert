// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

internal class Program
{
    public static T Gen<T>(T value)
    {
        Console.WriteLine(typeof(T));
        return value;
    }

    enum Mine { }
    enum Yours { }
    private static void Main(string[] args)
    {
        Console.WriteLine(Gen<int>(123));
        Console.WriteLine(Gen<Mine>(0));

        typeof(Program).GetMethod(nameof(Gen)).MakeGenericMethod(typeof(Yours)).Invoke(null, new object[] { (Yours)0 });
    }
}
