// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;

public class Unused
{
    public static int X;
}

internal unsafe class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW", ExactSpelling = true)]
    public static extern int MessageBox(int hWnd, byte* text, byte* caption, uint type);

    private static void Main(string[] args)
    {
        MessageBox(Unused.X, null, null, 0);
    }
}
