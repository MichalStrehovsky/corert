// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.CompilerServices;

interface IFoo<T>
{
    void Frob();
}

struct Foo<T> : IFoo<T>
{
    public void Frob()
    {
        Console.WriteLine("Hello");
    }
}

internal class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void DoFrob<T, U>(T t) where T : IFoo<U>
    {
        t.Frob();
    }

    private static void Main(string[] args)
    {
        DoFrob<Foo<object>, object>(new Foo<object>());
    }
}
