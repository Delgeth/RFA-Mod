using System.Runtime.InteropServices;
using System.Text;

namespace RfaFormats.Shaders;

/// <summary>
/// Compiles HLSL to Direct3D shader code with Windows' own compiler (d3dcompiler_47.dll, part of
/// Windows 10 and 11), as the game's own shaders are: shader model 4 (vs_4_0 / ps_4_0). The compiler
/// hands its results back as ID3DBlob objects; their methods are called through their function
/// table (GetBufferPointer is its 4th, GetBufferSize its 5th, Release its 3rd), so nothing needs .NET's
/// COM support.
/// </summary>
public static class HlslCompiler
{
    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.Winapi)]
    static extern int D3DCompile(byte[] source, nint size, [MarshalAs(UnmanagedType.LPStr)] string? name, IntPtr defines, IntPtr include,
                                 [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string target,
                                 uint flags1, uint flags2, out IntPtr code, out IntPtr errors);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr BlobPointer(IntPtr blob);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate nint BlobSize(IntPtr blob);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint BlobRelease(IntPtr blob);

    const uint OptimizationLevel3 = 1 << 15;

    /// <summary>The compiled code of <paramref name="entry"/> in <paramref name="source"/> for <paramref name="target"/> (vs_4_0, ps_4_0).</summary>
    public static byte[] Compile(string source, string entry, string target, string name, IReadOnlyDictionary<string, string>? defines = null)
    {
        var text = new StringBuilder();
        if (defines != null)
            foreach (var (key, value) in defines) text.Append("#define ").Append(key).Append(' ').Append(value).Append('\n');
        text.Append("#line 1\n").Append(source);
        byte[] bytes = Encoding.ASCII.GetBytes(text.ToString());
        int result = D3DCompile(bytes, bytes.Length, name, IntPtr.Zero, IntPtr.Zero, entry, target, OptimizationLevel3, 0, out IntPtr code, out IntPtr errors);
        try
        {
            if (result != 0 || code == IntPtr.Zero)
            {
                string message = errors != IntPtr.Zero ? Encoding.ASCII.GetString(Contents(errors)).TrimEnd('\0').Trim() : $"error 0x{result:X8}";
                throw new InvalidDataException($"{name} {entry} ({target}) didn't compile: {message}");
            }
            return Contents(code);
        }
        finally
        {
            Release(code);
            Release(errors);
        }
    }

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.Winapi)]
    static extern int D3DDisassemble(byte[] code, nint size, uint flags, [MarshalAs(UnmanagedType.LPStr)] string? comments, out IntPtr text);

    /// <summary>Compiled shader code as readable instructions, with its signatures (Windows' own disassembler).</summary>
    public static string Disassemble(byte[] code)
    {
        if (D3DDisassemble(code, code.Length, 0, null, out IntPtr text) != 0 || text == IntPtr.Zero)
            throw new InvalidDataException("couldn't disassemble a shader");
        try { return Encoding.ASCII.GetString(Contents(text)).TrimEnd('\0'); }
        finally { Release(text); }
    }

    /// <summary>
    /// A shader's input or output signature (<paramref name="which"/>: "Input" or "Output"): each element's
    /// semantic name, index, register and mask, as its disassembly lists them.
    /// </summary>
    public static List<(string Name, int Index, int Register, string Mask)> Signature(byte[] code, string which)
    {
        var elements = new List<(string, int, int, string)>();
        bool inside = false;
        foreach (string raw in Disassemble(code).Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith($"// {which} signature:")) { inside = true; continue; }
            if (!inside) continue;
            if (!line.StartsWith("//") || line.Contains("signature:")) { if (elements.Count > 0 || !line.StartsWith("//")) break; continue; }
            string[] parts = line[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Name Index Mask Register SysValue Format Used
            if (parts.Length >= 6 && int.TryParse(parts[1], out int index) && int.TryParse(parts[3], out int register))
                elements.Add((parts[0], index, register, parts[2]));
        }
        return elements;
    }

    static T Method<T>(IntPtr blob, int number) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(blob), number * IntPtr.Size));

    static byte[] Contents(IntPtr blob)
    {
        IntPtr at = Method<BlobPointer>(blob, 3)(blob);
        var bytes = new byte[(int)Method<BlobSize>(blob, 4)(blob)];
        Marshal.Copy(at, bytes, 0, bytes.Length);
        return bytes;
    }

    static void Release(IntPtr blob)
    {
        if (blob != IntPtr.Zero) Method<BlobRelease>(blob, 2)(blob);
    }
}
