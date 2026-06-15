using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UnityEngine;

/// <summary>
/// Compiles a C# source string at runtime via Roslyn and, if successful,
/// attaches the resulting MonoBehaviour to a target GameObject.
///
/// NOTE: Assembly.Load(byte[]) only works on the Mono scripting backend
/// (Editor, Windows/Mac/Linux standalone). It does NOT work on IL2CPP
/// (e.g. standalone Quest builds), since IL2CPP is ahead-of-time compiled
/// and cannot load new assemblies at runtime.
/// </summary>
public static class RuntimeCodeInjector
{
    public class InjectionResult
    {
        public bool CompileSucceeded;
        public bool InjectionSucceeded;
        public Component InjectedComponent;
        public List<string> Diagnostics = new List<string>();
    }

    public static InjectionResult InjectScript(string sourceCode, GameObject target)
    {
        var result = new InjectionResult();

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        // Reference every currently-loaded, non-dynamic assembly so the
        // generated script can call UnityEngine, XR Interaction Toolkit,
        // your own project types, etc.
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "DynamicAssembly_" + Guid.NewGuid().ToString("N"),
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        foreach (var diag in emitResult.Diagnostics)
        {
            if (diag.Severity == DiagnosticSeverity.Error || diag.Severity == DiagnosticSeverity.Warning)
                result.Diagnostics.Add(diag.ToString());
        }

        if (!emitResult.Success)
        {
            result.CompileSucceeded = false;
            return result;
        }

        result.CompileSucceeded = true;
        ms.Seek(0, SeekOrigin.Begin);

        Assembly assembly;
        try
        {
            assembly = Assembly.Load(ms.ToArray());
        }
        catch (Exception e)
        {
            result.Diagnostics.Add($"Assembly load failed: {e.Message}");
            return result;
        }

        var componentType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(MonoBehaviour).IsAssignableFrom(t));

        if (componentType == null)
        {
            result.Diagnostics.Add("No MonoBehaviour subclass found in generated code.");
            return result;
        }

        try
        {
            result.InjectedComponent = target.AddComponent(componentType);
            result.InjectionSucceeded = true;
        }
        catch (Exception e)
        {
            result.Diagnostics.Add($"AddComponent failed: {e.Message}");
        }

        return result;
    }

    /// <summary>
    /// Minimal rollback: removes the injected component.
    /// Does NOT restore any state the component's Awake/Start/Update may
    /// have already mutated on other components (material colors,
    /// transform changes, etc). Full state-snapshot rollback is a TODO
    /// once you've decided which mutations the error-recovery condition
    /// actually needs to undo.
    /// </summary>
    public static void RemoveInjectedComponent(Component component)
    {
        if (component != null)
        {
            UnityEngine.Object.Destroy(component);
        }
    }
}