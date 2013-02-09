﻿using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

static class ILTemplateWithTempAssembly
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);

    private static string tempBasePath;

    public static void Attach()
    {
        var currentDomain = AppDomain.CurrentDomain;
        currentDomain.AssemblyResolve += ResolveAssembly;

        //Create a unique Temp directory for the application path.
        var md5Hash = CreateMd5Hash(Assembly.GetExecutingAssembly().CodeBase);
        var prefixPath = Path.Combine(Path.GetTempPath(), "Costura");
        tempBasePath = Path.Combine(prefixPath, md5Hash);
        CreateDirectory();

        PreloadUnmanagedLibraries();
    }

    public static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name.ToLowerInvariant();
        var existingAssembly = ReadExistingAssembly(name);
        if (existingAssembly != null)
        {
            return existingAssembly;
        }

        var prefix = "costura.";

        var assemblyTempFilePath = Path.Combine(tempBasePath, string.Concat(name, ".dll"));
        if (File.Exists(assemblyTempFilePath))
        {
            return Assembly.LoadFile(assemblyTempFilePath);
        }

        var executingAssembly = Assembly.GetExecutingAssembly();

        var libInfo = executingAssembly.GetManifestResourceInfo(String.Concat(prefix, name, ".dll"));
        if (libInfo == null)
        {
            prefix = "costura32.";
            libInfo = executingAssembly.GetManifestResourceInfo(String.Concat(prefix, name, ".dll"));
        }
        if (libInfo == null)
        {
            prefix = "costura64.";
            libInfo = executingAssembly.GetManifestResourceInfo(String.Concat(prefix, name, ".dll"));
        }
        if (libInfo == null)
            return null;

        using (var assemblyStream = GetAssemblyStream(executingAssembly, String.Concat(prefix, name)))
        {
            if (assemblyStream == null)
            {
                return null;
            }
            var assemblyData = ReadStream(assemblyStream);
            File.WriteAllBytes(assemblyTempFilePath, assemblyData);
        }

        using (var pdbStream = GetDebugStream(executingAssembly, String.Concat(prefix, name)))
        {
            if (pdbStream != null)
            {
                var pdbData = ReadStream(pdbStream);
                var pdbTempFilePath = Path.Combine(tempBasePath, string.Concat(name, ".pdb"));
                var assemblyPdbTempFilePath = Path.Combine(tempBasePath, pdbTempFilePath);
                File.WriteAllBytes(assemblyPdbTempFilePath, pdbData);
            }
        }
        return Assembly.LoadFile(assemblyTempFilePath);
    }

    static void CreateDirectory()
    {
        if (Directory.Exists(tempBasePath))
        {
            try
            {
                Directory.Delete(tempBasePath, true);
            }
            catch
            { }
        }
        Directory.CreateDirectory(tempBasePath);
        MoveFileEx(tempBasePath, null, 0x4);
    }

    static string CreateMd5Hash(string input)
    {
        using (var md5 = MD5.Create())
        {
            var inputBytes = Encoding.ASCII.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);

            var sb = new StringBuilder();
            for (var i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }

    static byte[] ReadStream(Stream atream)
    {
        var data = new Byte[atream.Length];
        atream.Read(data, 0, data.Length);
        return data;
    }

    static Stream GetDebugStream(Assembly executingAssembly, string prefix)
    {
        var pdbName = string.Concat(prefix, ".pdb");
        return executingAssembly.GetManifestResourceStream(pdbName);
    }

    static Stream GetAssemblyStream(Assembly executingAssembly, string prefix)
    {
        var dllName = string.Concat(prefix, ".dll");
        return executingAssembly.GetManifestResourceStream(dllName);
    }

    public static Assembly ReadExistingAssembly(string name)
    {
        var currentDomain = AppDomain.CurrentDomain;
        var assemblies = currentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            var fullName = assembly.FullName.ToLowerInvariant();
            var indexOf = fullName.IndexOf(',');
            if (indexOf > 1)
            {
                fullName = fullName.Substring(0, indexOf);
            }

            if (fullName == name)
            {
                return assembly;
            }
        }
        return null;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr LoadLibrary(string dllToLoad);

    private static void PreloadUnmanagedLibraries()
    {
        // Preload correct library
        var bittyness = IntPtr.Size == 8 ? "64" : "32";

        var executingAssembly = Assembly.GetExecutingAssembly();

        foreach (var lib in executingAssembly.GetManifestResourceNames())
        {
            if (!lib.StartsWith("costura" + bittyness))
                continue;

            var assemblyTempFilePath = Path.Combine(tempBasePath, lib.Substring(10));

            if (!File.Exists(assemblyTempFilePath))
                using (var assemblyStream = executingAssembly.GetManifestResourceStream(lib))
                {
                    if (assemblyStream == null)
                    {
                        continue;
                    }
                    var assemblyData = ReadStream(assemblyStream);
                    File.WriteAllBytes(assemblyTempFilePath, assemblyData);
                }

            LoadLibrary(assemblyTempFilePath);
        }
    }
}