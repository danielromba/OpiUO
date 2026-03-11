using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using ClassicUO.Game;
using ClassicUO.Utility.Logging;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace ClassicUO.LegionScripting;

public partial class ScriptFile : IDisposable
{
    public string Path;
    public string FileName;
    public string FullPath;
    public string Group = string.Empty;
    public string SubGroup = string.Empty;
    public string[] FileContents;
    public string FileContentsJoined;
    public Thread ScriptThread;
    public ScriptEngine PythonEngine;
    public ScriptScope PythonScope;
    public LegionAPI ScopedApi;
    public ScriptType Type;
    public Assembly CSharpCompiledAssembly;
    public Type CSharpScriptType;

    public bool IsPlaying => ScriptThread != null;

    public enum ScriptType
    {
        Python,
        CSharp
    }

    private World World;
    private bool _disposed;

    public ScriptFile(World world, string path, string fileName)
    {
        World = world;
        Path = path;

        string cleanPath = path.Replace(System.IO.Path.DirectorySeparatorChar, '/');
        string cleanBasePath = LegionScripting.ScriptPath.Replace(System.IO.Path.DirectorySeparatorChar, '/');
        cleanPath = cleanPath.Substring(cleanPath.IndexOf(cleanBasePath, StringComparison.Ordinal) + cleanBasePath.Length);

        if (cleanPath.Length > 0)
        {
            string[] paths = cleanPath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length > 0)
                Group = paths[0];
            if (paths.Length > 1)
                SubGroup = paths[1];
        }

        FileName = fileName;
        FullPath = System.IO.Path.Combine(Path, FileName);
        FileContents = ReadFromFile();

        // Determine script type based on extension
        if (fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            Type = ScriptType.CSharp;
        else
            Type = ScriptType.Python;
    }

    public void OverrideFileContents(string contents)
    {
        string temp = System.IO.Path.GetTempFileName();

        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, FullPath, true);

            GameActions.Print(World, $"Saved {FileName}.");
        }
        catch (Exception ex)
        {
            GameActions.Print(World, ex.ToString());
        }
    }

    public string[] ReadFromFile()
    {
        try
        {
            string[] c = File.ReadAllLines(FullPath, Encoding.UTF8);
            string newContents = string.Join("\n", c);

            // Check if contents changed for C# scripts and invalidate cache
            if (Type == ScriptType.CSharp && FileContentsJoined != newContents)
            {
                CSharpCompiledAssembly = null;
                CSharpScriptType = null;
            }

            FileContentsJoined = newContents;

            string pattern = @"^\s*(?:from\s+[\w.]+\s+import\s+API|import\s+API)\s*$";
            FileContentsJoined = System.Text.RegularExpressions.Regex.Replace(FileContentsJoined, pattern, string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);

            return c;
        }
        catch (Exception e)
        {
            Log.Error($"Error reading script file: {e}");
            return [];
        }
    }

    public bool FileExists() => File.Exists(FullPath);

    public void SetupPythonEngine()
    {
        if (PythonEngine != null && !LegionScripting.LScriptSettings.DisableModuleCache)
            return;

        PythonEngine = Python.CreateEngine();

        string dir = System.IO.Path.GetDirectoryName(FullPath);
        ICollection<string> paths = PythonEngine.GetSearchPaths();
        paths.Add(System.IO.Path.Combine(CUOEnviroment.ExecutablePath, "iplib"));
        paths.Add(System.IO.Path.Combine(CUOEnviroment.ExecutablePath, "LegionScripts"));

        paths.Add(!string.IsNullOrWhiteSpace(dir) ? dir : Environment.CurrentDirectory);

        PythonEngine.SetSearchPaths(paths);
    }

    public void SetupPythonScope()
    {
        PythonScope = PythonEngine.CreateScope();
        var api = new LegionAPI(new PythonCallbackChannel(PythonEngine), this);
        ScopedApi = api;
        PythonEngine.GetBuiltinModule().SetVariable("API", api);
    }

    public void PythonScriptStopped()
    {
        ScopedApi?.CloseGumps();
        ScopedApi?.Dispose();

        PythonScope = null;
        ScopedApi = null;
        if (LegionScripting.LScriptSettings.DisableModuleCache)
            PythonEngine = null;
    }

    public void SetupCSharpScript()
    {
        // Reuse cached compilation if available
        if (CSharpCompiledAssembly != null && CSharpScriptType != null && !LegionScripting.LScriptSettings.DisableModuleCache)
            return;

        // Collect all files to compile (main file + includes)
        var filesToCompile = new Dictionary<string, string>(); // fullPath -> content
        var processedFiles = new HashSet<string>(); // To prevent circular includes

        CollectFilesRecursively(FullPath, FileContentsJoined, filesToCompile, processedFiles);

        // Create syntax trees for all files
        var syntaxTrees = new List<SyntaxTree>();
        foreach (KeyValuePair<string, string> kvp in filesToCompile)
        {
            string filePath = kvp.Key;
            string content = kvp.Value;

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
                content,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                path: filePath,
                encoding: Encoding.UTF8
            );

            syntaxTrees.Add(syntaxTree);
        }

        // Gather required assembly references
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),                      // System.Runtime
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),                     // System.Console
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),                  // System.Linq
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),                      // System.Collections
            MetadataReference.CreateFromFile(typeof(LegionAPI).Assembly.Location),                   // ClassicUO.LegionScripting
            MetadataReference.CreateFromFile(typeof(Microsoft.Xna.Framework.Vector3).Assembly.Location), // Microsoft.Xna.Framework
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections.Concurrent").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Text.RegularExpressions").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Text.Json").Location),
            MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location)
        };

        // Try to add SolveCaptcha.Captcha reference if available
        try
        {
            string captchaPath = System.IO.Path.Combine(CUOEnviroment.ExecutablePath, "SolveCaptcha.dll");

            if (File.Exists(captchaPath))
            {
                Assembly captchaAssembly = Assembly.LoadFrom(captchaPath);
                references.Add(MetadataReference.CreateFromFile(captchaAssembly.Location));
                Log.Trace("Successfully loaded SolveCaptcha.Captcha assembly for script compilation");
            }
            else
            {
                Log.Trace($"SolveCaptcha.Captcha assembly not found at: {captchaPath}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Failed to load SolveCaptcha.Captcha assembly: {ex.Message}");
        }

        // Create compilation
        string assemblyName = $"LegionScript_{System.IO.Path.GetFileNameWithoutExtension(FileName)}_{Guid.NewGuid():N}";
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: false
            )
        );

        // Compile to memory
        using var ms = new MemoryStream();
        EmitResult result = compilation.Emit(ms);

        if (!result.Success)
        {
            // Compilation failed - throw exception with diagnostics
            IEnumerable<Diagnostic> failures = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
            throw new CSharpCompilationException(failures);
        }

        // Load the compiled assembly
        ms.Seek(0, SeekOrigin.Begin);
        CSharpCompiledAssembly = Assembly.Load(ms.ToArray());

        // Find the script class that implements ILegionScript
        CSharpScriptType = CSharpCompiledAssembly.GetTypes()
            .FirstOrDefault(t => typeof(ILegionScript).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        if (CSharpScriptType == null)
        {
            throw new InvalidOperationException(
                $"Script '{FileName}' does not contain a class that implements ILegionScript. " +
                "Please ensure your script has a class in the 'ClassicUO.LegionScripting.Scripts' namespace that implements ILegionScript."
            );
        }
    }

    private void CollectFilesRecursively(string currentFilePath, string content, Dictionary<string, string> filesToCompile, HashSet<string> processedFiles)
    {
        // Normalize path to prevent duplicate includes with different path formats
        string normalizedPath = System.IO.Path.GetFullPath(currentFilePath);

        // Check if already processed (prevents circular includes)
        if (processedFiles.Contains(normalizedPath))
            return;

        processedFiles.Add(normalizedPath);

        // Add current file to compilation list
        filesToCompile[normalizedPath] = content;

        // Parse include directives: //#include "filename.cs"
        string includePattern = @"^\s*//#include\s+""([^""]+)""\s*$";
        MatchCollection matches = Regex.Matches(content, includePattern, RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            string includedFileName = match.Groups[1].Value;

            // Resolve path relative to current file's directory
            string currentDirectory = System.IO.Path.GetDirectoryName(currentFilePath);
            string includedFilePath = System.IO.Path.Combine(currentDirectory, includedFileName);

            try
            {
                // Check if file exists
                if (!File.Exists(includedFilePath))
                {
                    throw new FileNotFoundException($"Included file not found: {includedFileName}");
                }

                // Read included file
                string includedContent = File.ReadAllText(includedFilePath, Encoding.UTF8);

                // Recursively process included file (supports nested includes)
                CollectFilesRecursively(includedFilePath, includedContent, filesToCompile, processedFiles);
            }
            catch (Exception ex)
            {
                Log.Error($"Error including file '{includedFileName}' from '{currentFilePath}': {ex.Message}");
                throw new InvalidOperationException($"Failed to include file '{includedFileName}' in '{System.IO.Path.GetFileName(currentFilePath)}': {ex.Message}", ex);
            }
        }
    }

    public void SetupCSharpGlobals()
    {
        var api = new LegionAPI(new CSharpCallbackChannel(), this);
        ScopedApi = api;
    }

    public void CSharpScriptStopped()
    {
        ScopedApi?.CloseGumps();
        ScopedApi?.Dispose();
        ScopedApi = null;

        // Clear compilation cache if module caching disabled
        if (LegionScripting.LScriptSettings.DisableModuleCache)
        {
            CSharpCompiledAssembly = null;
            CSharpScriptType = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (Type == ScriptType.Python)
            PythonScriptStopped();
        else
            CSharpScriptStopped();

        GC.SuppressFinalize(this);
        _disposed = true;
    }
}
